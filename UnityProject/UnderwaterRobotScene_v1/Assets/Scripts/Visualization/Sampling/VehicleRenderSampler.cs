using System;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Transforms;

namespace UnderwaterRobotScene.Visualization.Sampling
{
    public static class VehicleRenderSampler
    {
        public static RenderPoseSample Sample(
            VehicleStateStore store,
            in RenderSampleRequest request)
        {
            if (store == null)
            {
                return Fail(RenderSampleFailureReason.InvalidRequest, "State store is required.", request);
            }

            if (string.IsNullOrWhiteSpace(request.SourceId) ||
                string.IsNullOrWhiteSpace(request.VehicleId) ||
                !IsFinite(request.TargetSourceTimeSeconds) ||
                !IsFinite(request.LocalMonotonicNowSeconds) ||
                request.LocalMonotonicNowSeconds < 0.0 ||
                !IsKnownSourceStatus(request.SourceStatus))
            {
                return Fail(
                    RenderSampleFailureReason.InvalidRequest,
                    "Source, vehicle, finite target time, finite non-negative local time, and source status are required.",
                    request);
            }

            if (!request.Policy.TryValidate(out string policyError))
            {
                return Fail(RenderSampleFailureReason.InvalidPolicy, policyError, request);
            }

            if (request.SourceStatus == DataSourceStatus.Faulted)
            {
                return Fail(
                    RenderSampleFailureReason.SourceFaulted,
                    "The selected data source is faulted.",
                    request);
            }

            if (request.SourceStatus == DataSourceStatus.Disposed)
            {
                return Fail(
                    RenderSampleFailureReason.SourceUnavailable,
                    "The selected data source is disposed.",
                    request);
            }

            if (!store.TryGetActiveEpoch(request.SourceId, out ulong activeEpoch))
            {
                return Fail(
                    RenderSampleFailureReason.NoData,
                    "No history exists for the selected data source.",
                    request);
            }

            if (activeEpoch != request.SourceEpoch)
            {
                return Fail(
                    RenderSampleFailureReason.EpochUnavailable,
                    "The requested source epoch is not active.",
                    request);
            }

            VehicleSnapshot snapshot;
            try
            {
                if (!store.TryReadSnapshot(
                        request.SourceId,
                        request.SourceEpoch,
                        request.VehicleId,
                        request.LocalMonotonicNowSeconds,
                        out snapshot))
                {
                    return Fail(
                        RenderSampleFailureReason.NoData,
                        "No history exists for the selected source epoch and vehicle.",
                        request);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return Fail(
                    RenderSampleFailureReason.LocalClockRegression,
                    "Local monotonic time precedes the latest receive time.",
                    request);
            }

            if (snapshot.IsTimedOut)
            {
                return RenderPoseSample.FailureWithSnapshot(
                    RenderSampleFailureReason.Stale,
                    "The latest received state is stale under the store timeout policy.",
                    request,
                    snapshot);
            }

            VehicleStateWindow window = snapshot.Window;
            if (!TryValidateHistory(window))
            {
                return Fail(
                    RenderSampleFailureReason.InvalidHistory,
                    "History source timestamps must be finite and strictly increasing.",
                    request);
            }

            int exactIndex = FindExactIndex(
                window,
                request.TargetSourceTimeSeconds,
                request.Policy.ExactTimeToleranceSeconds);
            if (exactIndex >= 0)
            {
                ReceivedVehicleState exact = window[exactIndex];
                return ConvertSingle(
                    exact,
                    RenderSampleMode.Exact,
                    request,
                    snapshot);
            }

            ReceivedVehicleState oldest = window[0];
            ReceivedVehicleState latest = window[window.Count - 1];
            if (request.TargetSourceTimeSeconds < oldest.State.SourceTimestampSeconds)
            {
                return Fail(
                    RenderSampleFailureReason.BeforeHistory,
                    "Target source time precedes the retained history.",
                    request);
            }

            if (request.TargetSourceTimeSeconds > latest.State.SourceTimestampSeconds)
            {
                return SampleAfterLatest(latest, window.Count, request, snapshot);
            }

            int afterIndex = FindFirstAfter(window, request.TargetSourceTimeSeconds);
            if (afterIndex <= 0 || afterIndex >= window.Count)
            {
                return Fail(
                    RenderSampleFailureReason.InvalidHistory,
                    "A valid source-time bracket could not be selected.",
                    request);
            }

            ReceivedVehicleState before = window[afterIndex - 1];
            ReceivedVehicleState after = window[afterIndex];
            double gap = after.State.SourceTimestampSeconds - before.State.SourceTimestampSeconds;
            if (!IsFinite(gap) || gap <= 0.0)
            {
                return Fail(
                    RenderSampleFailureReason.InvalidHistory,
                    "The selected source-time bracket is not strictly increasing.",
                    request);
            }

            if (gap > request.Policy.MaxInterpolationGapSeconds)
            {
                return Fail(
                    RenderSampleFailureReason.GapTooLarge,
                    "The selected source-time bracket exceeds the interpolation-gap policy.",
                    request);
            }

            double alpha =
                (request.TargetSourceTimeSeconds - before.State.SourceTimestampSeconds) / gap;
            if (!IsFinite(alpha) || alpha < 0.0 || alpha > 1.0)
            {
                return Fail(
                    RenderSampleFailureReason.InvalidHistory,
                    "The selected source-time bracket produced an invalid interpolation factor.",
                    request);
            }

            if (!VehiclePoseConverter.TryConvert(
                    before.State,
                    request.TransformProfile,
                    out ConvertedVehiclePose convertedBefore,
                    out ConversionError beforeError))
            {
                return ConversionFail(beforeError, "Before-sample conversion failed.", request);
            }

            if (!VehiclePoseConverter.TryConvert(
                    after.State,
                    request.TransformProfile,
                    out ConvertedVehiclePose convertedAfter,
                    out ConversionError afterError))
            {
                return ConversionFail(afterError, "After-sample conversion failed.", request);
            }

            if (!PoseInterpolation.TryLerpPosition(
                    convertedBefore.Position,
                    convertedAfter.Position,
                    alpha,
                    out Vector3d position) ||
                !PoseInterpolation.TrySlerp(
                    convertedBefore.Orientation,
                    convertedAfter.Orientation,
                    alpha,
                    out Quaterniond orientation))
            {
                return Fail(
                    RenderSampleFailureReason.InterpolationFailed,
                    "Pose interpolation did not produce a finite normalized pose.",
                    request);
            }

            return RenderPoseSample.Success(
                RenderSampleMode.Interpolated,
                request,
                snapshot,
                before,
                after,
                alpha,
                position,
                orientation);
        }

        private static RenderPoseSample SampleAfterLatest(
            in ReceivedVehicleState latest,
            int historyCount,
            in RenderSampleRequest request,
            in VehicleSnapshot snapshot)
        {
            if (request.Policy.AfterLatestBehavior == AfterLatestBehavior.Reject)
            {
                return Fail(
                    RenderSampleFailureReason.AfterLatestRejected,
                    "Target source time is after the latest state and holding is disabled.",
                    request);
            }

            if (historyCount == 1 && !request.Policy.AllowSingleSampleHold)
            {
                return Fail(
                    RenderSampleFailureReason.SingleSampleHoldDisabled,
                    "The history contains one sample and single-sample holding is disabled.",
                    request);
            }

            double holdAge =
                request.TargetSourceTimeSeconds - latest.State.SourceTimestampSeconds;
            if (!IsFinite(holdAge) || holdAge > request.Policy.MaxHoldSourceTimeSeconds)
            {
                return Fail(
                    RenderSampleFailureReason.HoldWindowExceeded,
                    "Target source time exceeds the bounded latest-hold window.",
                    request);
            }

            return ConvertSingle(
                latest,
                RenderSampleMode.HeldLatest,
                request,
                snapshot);
        }

        private static RenderPoseSample ConvertSingle(
            in ReceivedVehicleState source,
            RenderSampleMode mode,
            in RenderSampleRequest request,
            in VehicleSnapshot snapshot)
        {
            if (!VehiclePoseConverter.TryConvert(
                    source.State,
                    request.TransformProfile,
                    out ConvertedVehiclePose pose,
                    out ConversionError error))
            {
                return ConversionFail(error, "Pose conversion failed.", request);
            }

            return RenderPoseSample.Success(
                mode,
                request,
                snapshot,
                source,
                source,
                0.0,
                pose.Position,
                pose.Orientation);
        }

        private static bool TryValidateHistory(VehicleStateWindow window)
        {
            if (window == null || window.Count == 0)
            {
                return false;
            }

            double previous = window[0].State.SourceTimestampSeconds;
            if (!IsFinite(previous))
            {
                return false;
            }

            for (int index = 1; index < window.Count; index++)
            {
                double current = window[index].State.SourceTimestampSeconds;
                if (!IsFinite(current) || current <= previous)
                {
                    return false;
                }

                previous = current;
            }

            return true;
        }

        private static int FindExactIndex(
            VehicleStateWindow window,
            double targetSourceTime,
            double tolerance)
        {
            int bestIndex = -1;
            double bestDistance = double.PositiveInfinity;
            for (int index = 0; index < window.Count; index++)
            {
                double distance = Math.Abs(
                    window[index].State.SourceTimestampSeconds - targetSourceTime);
                if (distance <= tolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }

            return bestIndex;
        }

        private static int FindFirstAfter(VehicleStateWindow window, double targetSourceTime)
        {
            int low = 0;
            int high = window.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (window[middle].State.SourceTimestampSeconds <= targetSourceTime)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private static bool IsKnownSourceStatus(DataSourceStatus status)
        {
            return status == DataSourceStatus.Stopped ||
                   status == DataSourceStatus.Starting ||
                   status == DataSourceStatus.Running ||
                   status == DataSourceStatus.Stopping ||
                   status == DataSourceStatus.Faulted ||
                   status == DataSourceStatus.Disposed;
        }

        private static RenderPoseSample ConversionFail(
            ConversionError error,
            string context,
            in RenderSampleRequest request)
        {
            return RenderPoseSample.Failure(
                RenderSampleFailureReason.ConversionFailed,
                context + " " + error.Message,
                request,
                error);
        }

        private static RenderPoseSample Fail(
            RenderSampleFailureReason reason,
            string message,
            in RenderSampleRequest request)
        {
            return RenderPoseSample.Failure(reason, message, request);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
