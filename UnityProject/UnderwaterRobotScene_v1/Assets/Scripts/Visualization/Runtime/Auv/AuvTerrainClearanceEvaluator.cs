using System;
using System.Globalization;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Auv
{
    public enum AuvTerrainClearanceState
    {
        Supported = 0,
        Corrected = 1,
        OutOfTerrainBounds = 2,
        NoValidTerrainSample = 3,
        SlopeRejected = 4,
        InvalidProfile = 5,
        InvalidPose = 6,
        CorrectionRejected = 7,
        InvalidWaterAuthority = 8,
        WaterSurfaceBreach = 9,
        InvalidSegmentGeometry = 10,
        ClimbAngleExceeded = 11,
        DescentAngleExceeded = 12
    }

    public readonly struct AuvTerrainProbeObservation
    {
        public AuvTerrainProbeObservation(
            Vector3 projectedPoint,
            bool hasValidSample,
            TerrainAuthoritySample sample,
            TerrainAuthorityFailure failureReason,
            float requiredCorrectionMeters)
        {
            ProjectedPoint = projectedPoint;
            HasValidSample = hasValidSample;
            Sample = sample;
            FailureReason = failureReason;
            RequiredCorrectionMeters = requiredCorrectionMeters;
        }

        public Vector3 ProjectedPoint { get; }
        public bool HasValidSample { get; }
        public TerrainAuthoritySample Sample { get; }
        public TerrainAuthorityFailure FailureReason { get; }
        public float RequiredCorrectionMeters { get; }
    }

    public readonly struct AuvTerrainClearanceResult
    {
        public AuvTerrainClearanceResult(
            AuvTerrainClearanceState state,
            Vector3 outputPosition,
            Quaternion outputRotation,
            float correctionMeters,
            float maximumSlopeDegrees,
            AuvTerrainProbeObservation[] observations,
            float maximumHullWorldY,
            float waterSurfaceY,
            float allowedMaximumHullWorldY,
            string reason)
        {
            State = state;
            OutputPosition = outputPosition;
            OutputRotation = outputRotation;
            CorrectionMeters = correctionMeters;
            MaximumSlopeDegrees = maximumSlopeDegrees;
            Observations = observations ?? Array.Empty<AuvTerrainProbeObservation>();
            MaximumHullWorldY = maximumHullWorldY;
            WaterSurfaceY = waterSurfaceY;
            AllowedMaximumHullWorldY = allowedMaximumHullWorldY;
            Reason = reason ?? string.Empty;
        }

        public AuvTerrainClearanceState State { get; }
        public Vector3 OutputPosition { get; }
        public Quaternion OutputRotation { get; }
        public float CorrectionMeters { get; }
        public float MaximumSlopeDegrees { get; }
        public AuvTerrainProbeObservation[] Observations { get; }
        public float MaximumHullWorldY { get; }
        public float WaterSurfaceY { get; }
        public float AllowedMaximumHullWorldY { get; }
        public string Reason { get; }
        public bool MayApply =>
            State == AuvTerrainClearanceState.Supported ||
            State == AuvTerrainClearanceState.Corrected;
    }

    public static class AuvTerrainClearanceEvaluator
    {
        private const double MinimumRotationSquaredMagnitude = 1e-12;
        private const double HorizontalDirectionEpsilonSquared = 1e-12;
        // Absorbs only single-precision comparison noise at the configured
        // water-envelope boundary; it is not part of the business tolerance.
        private const float WaterEnvelopeComparisonEpsilonMeters = 1e-6f;

        public static AuvTerrainClearanceResult Evaluate(
            Vector3 candidatePosition,
            Quaternion candidateRotation,
            AuvTerrainClearanceProfile profile,
            IAuthoritativeTerrainSurfaceProvider provider,
            float waterSurfaceY)
        {
            if (profile == null || !profile.TryValidate(out _))
            {
                return Hold(AuvTerrainClearanceState.InvalidProfile,
                    candidatePosition, candidateRotation,
                    Array.Empty<AuvTerrainProbeObservation>(), 0f,
                    "Invalid AUV operating-envelope profile.");
            }
            if (provider == null)
            {
                return Hold(AuvTerrainClearanceState.NoValidTerrainSample,
                    candidatePosition, candidateRotation,
                    Array.Empty<AuvTerrainProbeObservation>(), 0f,
                    "No authoritative terrain provider is available.");
            }
            if (!IsFinite(waterSurfaceY))
            {
                return Hold(AuvTerrainClearanceState.InvalidWaterAuthority,
                    candidatePosition, candidateRotation,
                    Array.Empty<AuvTerrainProbeObservation>(), 0f,
                    "The bound water surface Y is non-finite.");
            }

            if (!IsFinite(candidatePosition) ||
                !TryNormalize(candidateRotation,
                    out Quaternion normalizedRotation))
            {
                return Hold(AuvTerrainClearanceState.InvalidPose,
                    candidatePosition, candidateRotation,
                    Array.Empty<AuvTerrainProbeObservation>(), 0f,
                    "The candidate AUV pose is invalid.");
            }

            var observations = new AuvTerrainProbeObservation[
                profile.ProbeCount];
            float correction = 0f;
            float maximumSlope = 0f;
            bool hasCoverageFailure = false;
            bool hasOtherFailure = false;
            for (int index = 0; index < profile.ProbeCount; index++)
            {
                Vector3 projected = candidatePosition +
                    normalizedRotation *
                    profile.LowerEnvelopeProbeOffsets[index];
                // ProbeStartHeightMeters and ProbeDistanceMeters remain in the
                // serialized profile for legacy ray compatibility. AUV terrain
                // existence is intentionally independent of both values.
                bool succeeded = provider.TrySampleAtXZ(
                    projected.x,
                    projected.z,
                    out TerrainAuthoritySample sample,
                    out TerrainAuthorityFailure failure);
                float required = succeeded
                    ? sample.WorldPoint.y +
                      profile.MinimumHullClearanceMeters - projected.y
                    : 0f;
                observations[index] = new AuvTerrainProbeObservation(
                    projected, succeeded, sample, failure, required);
                if (!succeeded)
                {
                    bool coverageFailure =
                        failure == TerrainAuthorityFailure.OutsideCoverage ||
                        failure == TerrainAuthorityFailure.TopologyHole;
                    hasCoverageFailure |= coverageFailure;
                    hasOtherFailure |= !coverageFailure;
                    continue;
                }
                maximumSlope = Mathf.Max(maximumSlope,
                    sample.SlopeDegrees);
                correction = Mathf.Max(correction, required);
            }

            if (hasOtherFailure)
            {
                return Hold(AuvTerrainClearanceState.NoValidTerrainSample,
                    candidatePosition, candidateRotation,
                    observations, maximumSlope,
                    "No valid authoritative terrain sample exists.");
            }
            if (hasCoverageFailure)
            {
                return Hold(AuvTerrainClearanceState.OutOfTerrainBounds,
                    candidatePosition, candidateRotation,
                    observations, maximumSlope,
                    "The AUV hull envelope is outside terrain coverage.");
            }
            if (!IsFinite(maximumSlope) ||
                maximumSlope > profile.MaximumSlopeDegrees)
            {
                return Hold(AuvTerrainClearanceState.SlopeRejected,
                    candidatePosition, candidateRotation,
                    observations, maximumSlope,
                    "Terrain slope " + Format(maximumSlope) +
                    " degrees exceeds " +
                    Format(profile.MaximumSlopeDegrees) + " degrees.");
            }

            correction = Mathf.Max(0f, correction);
            if (!IsFinite(correction) ||
                correction > profile.MaximumUpwardCorrectionMeters +
                    profile.SamplingToleranceMeters)
            {
                return Hold(AuvTerrainClearanceState.CorrectionRejected,
                    candidatePosition, candidateRotation,
                    observations, maximumSlope,
                    "Required upward correction " + Format(correction) +
                    " m exceeds " +
                    Format(profile.MaximumUpwardCorrectionMeters) + " m.");
            }

            Vector3 output = candidatePosition +
                Vector3.up * correction;
            AuvTerrainClearanceState state =
                correction <= profile.SamplingToleranceMeters
                    ? AuvTerrainClearanceState.Supported
                    : AuvTerrainClearanceState.Corrected;

            float maximumHullWorldY = float.NegativeInfinity;
            for (int index = 0;
                 index < profile.HullEnvelopeCornerCount;
                 index++)
            {
                Vector3 corner = output + normalizedRotation *
                    profile.HullEnvelopeCornerOffsets[index];
                if (!IsFinite(corner))
                {
                    return Hold(AuvTerrainClearanceState.InvalidProfile,
                        candidatePosition, candidateRotation,
                        observations, maximumSlope,
                        "A transformed AUV hull-envelope corner is invalid.");
                }
                maximumHullWorldY = Mathf.Max(maximumHullWorldY, corner.y);
            }
            float allowedMaximumHullWorldY = waterSurfaceY -
                profile.MinimumHullSubmergenceMeters;
            if (!IsFinite(maximumHullWorldY) ||
                !IsFinite(allowedMaximumHullWorldY))
            {
                return Hold(AuvTerrainClearanceState.InvalidWaterAuthority,
                    candidatePosition, candidateRotation,
                    observations, maximumSlope,
                    "The AUV water-envelope calculation is non-finite.");
            }
            if (maximumHullWorldY > allowedMaximumHullWorldY +
                profile.SamplingToleranceMeters +
                WaterEnvelopeComparisonEpsilonMeters)
            {
                string reason = "WaterSurfaceBreach: hullMaxY=" +
                    Format(maximumHullWorldY) + " m, waterY=" +
                    Format(waterSurfaceY) + " m, minimumSubmergence=" +
                    Format(profile.MinimumHullSubmergenceMeters) +
                    " m, allowedHullMaxY=" +
                    Format(allowedMaximumHullWorldY) + " m.";
                return new AuvTerrainClearanceResult(
                    AuvTerrainClearanceState.WaterSurfaceBreach,
                    output,
                    candidateRotation,
                    correction,
                    maximumSlope,
                    observations,
                    maximumHullWorldY,
                    waterSurfaceY,
                    allowedMaximumHullWorldY,
                    reason);
            }
            return new AuvTerrainClearanceResult(
                state,
                output,
                candidateRotation,
                correction,
                maximumSlope,
                observations,
                maximumHullWorldY,
                waterSurfaceY,
                allowedMaximumHullWorldY,
                state.ToString());
        }

        public static bool TryValidateRoute(
            ActiveRouteSnapshot route,
            in CoordinateTransformProfile transformProfile,
            AuvTerrainClearanceProfile profile,
            IAuthoritativeTerrainSurfaceProvider provider,
            float waterSurfaceY,
            out string error)
        {
            return TryValidateRoute(
                route,
                in transformProfile,
                profile,
                provider,
                waterSurfaceY,
                out error,
                out _);
        }

        public static bool TryValidateRoute(
            ActiveRouteSnapshot route,
            in CoordinateTransformProfile transformProfile,
            AuvTerrainClearanceProfile profile,
            IAuthoritativeTerrainSurfaceProvider provider,
            float waterSurfaceY,
            out string error,
            out RouteSafetyFailureDiagnostic diagnostic)
        {
            error = string.Empty;
            diagnostic = RouteSafetyFailureDiagnostic.None;
            if (route == null || route.VehicleType != VehicleType.Auv)
            {
                error = "An AUV route is required for terrain validation.";
                return false;
            }
            if (profile == null || !profile.TryValidate(out error))
            {
                return false;
            }
            if (provider == null)
            {
                error = "An authoritative AUV terrain provider is required.";
                return false;
            }
            if (!IsFinite(waterSurfaceY))
            {
                error = "A finite bound water surface Y is required.";
                return false;
            }
            if (!provider.TryValidateAuthority(
                    out TerrainAuthorityFailure authorityFailure))
            {
                error = "AUV terrain authority validation failed: " +
                    authorityFailure + ".";
                return false;
            }

            for (int segment = 0;
                 segment < route.WaypointCount - 1;
                 segment++)
            {
                Vector3d start = route.GetWaypoint(segment);
                Vector3d end = route.GetWaypoint(segment + 1);
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double dz = end.Z - start.Z;
                if (!TryValidateSegmentAngle(
                        dx,
                        dy,
                        dz,
                        profile,
                        out AuvTerrainClearanceState angleState,
                        out double measuredAngle,
                        out double angleLimit,
                        out string angleError))
                {
                    diagnostic = new RouteSafetyFailureDiagnostic(
                        segment + 1,
                        0.0,
                        angleState.ToString());
                    error = "AUV operating envelope rejected segment " +
                        (segment + 1) + ": " + angleError +
                        " measuredAngle=" + Format(measuredAngle) +
                        " degrees, approvedLimit=" + Format(angleLimit) +
                        " degrees.";
                    return false;
                }
                double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                int intervals = Math.Max(1,
                    (int)Math.Ceiling(
                        length / profile.SegmentValidationSpacingMeters));
                Quaterniond routeOrientation =
                    VehicleRouteRuntime.BuildOrientation(
                        start, end, route.OrientationPolicy);
                int firstSample = segment == 0 ? 0 : 1;
                for (int sampleIndex = firstSample;
                     sampleIndex <= intervals;
                     sampleIndex++)
                {
                    double t = (double)sampleIndex / intervals;
                    var routePosition = new Vector3d(
                        start.X + dx * t,
                        start.Y + dy * t,
                        start.Z + dz * t);
                    if (!TryConvertRoutePose(
                            route.VehicleId,
                            routePosition,
                            routeOrientation,
                            transformProfile,
                            out Vector3 unityPosition,
                            out Quaternion unityRotation,
                            out error))
                    {
                        return false;
                    }

                    AuvTerrainClearanceResult result = Evaluate(
                        unityPosition,
                        unityRotation,
                        profile,
                        provider,
                        waterSurfaceY);
                    if (!result.MayApply)
                    {
                        diagnostic = new RouteSafetyFailureDiagnostic(
                            segment + 1,
                            t * 100.0,
                            result.State.ToString());
                        error = "AUV terrain safety rejected segment " +
                            (segment + 1) + " at " +
                            (t * 100.0).ToString("0.0") + "%: " +
                            result.State + ". " + result.Reason;
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateSegmentAngle(
            double dx,
            double dy,
            double dz,
            AuvTerrainClearanceProfile profile,
            out AuvTerrainClearanceState failureState,
            out double measuredAngleDegrees,
            out double approvedLimitDegrees,
            out string error)
        {
            failureState = AuvTerrainClearanceState.InvalidSegmentGeometry;
            measuredAngleDegrees = double.NaN;
            approvedLimitDegrees = double.NaN;
            error = string.Empty;
            if (!Numeric.IsFinite(dx) || !Numeric.IsFinite(dy) ||
                !Numeric.IsFinite(dz))
            {
                error = "Segment geometry is non-finite.";
                return false;
            }

            double horizontalSquared = dx * dx + dz * dz;
            double verticalMagnitude = Math.Abs(dy);
            if (!Numeric.IsFinite(horizontalSquared) ||
                !Numeric.IsFinite(verticalMagnitude))
            {
                error = "Segment geometry is non-finite.";
                return false;
            }
            approvedLimitDegrees = dy >= 0.0
                ? profile.MaximumClimbAngleDegrees
                : profile.MaximumDescentAngleDegrees;
            failureState = dy >= 0.0
                ? AuvTerrainClearanceState.ClimbAngleExceeded
                : AuvTerrainClearanceState.DescentAngleExceeded;
            if (horizontalSquared <= HorizontalDirectionEpsilonSquared &&
                verticalMagnitude > 0.0)
            {
                measuredAngleDegrees = 90.0;
                error = "Pure or numerically vertical AUV segment.";
                return false;
            }

            double horizontal = Math.Sqrt(horizontalSquared);
            double measuredAngleRadians = Math.Atan2(
                verticalMagnitude,
                horizontal);
            measuredAngleDegrees = measuredAngleRadians *
                (180.0 / Math.PI);
            if (!Numeric.IsFinite(measuredAngleRadians) ||
                !Numeric.IsFinite(measuredAngleDegrees))
            {
                failureState =
                    AuvTerrainClearanceState.InvalidSegmentGeometry;
                error = "Segment angle is non-finite.";
                return false;
            }
            double approvedLimitRadians = approvedLimitDegrees *
                (Math.PI / 180.0);
            if (measuredAngleRadians > approvedLimitRadians)
            {
                error = dy >= 0.0
                    ? "ClimbAngleExceeded."
                    : "DescentAngleExceeded.";
                return false;
            }

            failureState = AuvTerrainClearanceState.Supported;
            error = string.Empty;
            return true;
        }

        private static bool TryConvertRoutePose(
            string vehicleId,
            Vector3d position,
            Quaterniond orientation,
            in CoordinateTransformProfile profile,
            out Vector3 unityPosition,
            out Quaternion unityRotation,
            out string error)
        {
            var state = new VehicleState(
                vehicleId,
                VehicleType.Auv,
                0.0,
                0UL,
                position,
                orientation,
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position |
                    VehicleStateFields.Orientation,
                profile.SourceWorldFrame,
                profile.SourceBodyFrame);
            if (!VehiclePoseConverter.TryConvert(
                    in state,
                    in profile,
                    out ConvertedVehiclePose converted,
                    out ConversionError conversionError) ||
                !UnityPoseAdapter.TryConvert(
                    converted.Position,
                    converted.Orientation,
                    out unityPosition,
                    out unityRotation))
            {
                error = "AUV route pose conversion failed: " +
                    conversionError.Message;
                unityPosition = default;
                unityRotation = default;
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static AuvTerrainClearanceResult Hold(
            AuvTerrainClearanceState state,
            Vector3 position,
            Quaternion rotation,
            AuvTerrainProbeObservation[] observations,
            float maximumSlope,
            string reason)
        {
            return new AuvTerrainClearanceResult(
                state, position, rotation, 0f, maximumSlope,
                observations,
                float.NaN,
                float.NaN,
                float.NaN,
                reason);
        }

        private static string Format(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static bool TryNormalize(
            Quaternion value,
            out Quaternion normalized)
        {
            normalized = Quaternion.identity;
            if (!IsFinite(value.x) || !IsFinite(value.y) ||
                !IsFinite(value.z) || !IsFinite(value.w))
            {
                return false;
            }
            double squared = (double)value.x * value.x +
                (double)value.y * value.y +
                (double)value.z * value.z +
                (double)value.w * value.w;
            if (double.IsNaN(squared) || double.IsInfinity(squared) ||
                squared <= MinimumRotationSquaredMagnitude)
            {
                return false;
            }
            float inverse = (float)(1.0 / Math.Sqrt(squared));
            normalized = new Quaternion(
                value.x * inverse,
                value.y * inverse,
                value.z * inverse,
                value.w * inverse);
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
