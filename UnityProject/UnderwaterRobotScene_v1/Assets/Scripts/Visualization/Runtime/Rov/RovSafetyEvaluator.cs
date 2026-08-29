using System;
using System.Collections.Generic;
using System.Globalization;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Rov
{
    public enum RovSafetyState
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
        WaterSurfaceBreach = 9
    }

    public readonly struct RovSafetyProbeObservation
    {
        public RovSafetyProbeObservation(
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

    public readonly struct RovSafetyResult
    {
        public RovSafetyResult(
            RovSafetyState state,
            Vector3 outputPosition,
            Quaternion outputRotation,
            float correctionMeters,
            float maximumSlopeDegrees,
            RovSafetyProbeObservation leftFront,
            RovSafetyProbeObservation leftRear,
            RovSafetyProbeObservation rightFront,
            RovSafetyProbeObservation rightRear,
            int validSampleCount,
            float maximumEnvelopeWorldY,
            float waterSurfaceY,
            string reason)
        {
            State = state;
            OutputPosition = outputPosition;
            OutputRotation = outputRotation;
            CorrectionMeters = correctionMeters;
            MaximumSlopeDegrees = maximumSlopeDegrees;
            LeftFront = leftFront;
            LeftRear = leftRear;
            RightFront = rightFront;
            RightRear = rightRear;
            ValidSampleCount = validSampleCount;
            MaximumEnvelopeWorldY = maximumEnvelopeWorldY;
            WaterSurfaceY = waterSurfaceY;
            Reason = reason ?? string.Empty;
        }

        public RovSafetyState State { get; }
        public Vector3 OutputPosition { get; }
        public Quaternion OutputRotation { get; }
        public float CorrectionMeters { get; }
        public float MaximumSlopeDegrees { get; }
        public RovSafetyProbeObservation LeftFront { get; }
        public RovSafetyProbeObservation LeftRear { get; }
        public RovSafetyProbeObservation RightFront { get; }
        public RovSafetyProbeObservation RightRear { get; }
        public int ValidSampleCount { get; }
        public float MaximumEnvelopeWorldY { get; }
        public float WaterSurfaceY { get; }
        public string Reason { get; }
        public bool MayApply =>
            State == RovSafetyState.Supported ||
            State == RovSafetyState.Corrected;
    }

    public static class RovSafetyEvaluator
    {
        private const double MinimumRotationSquaredMagnitude = 1e-12;
        private const double BreakpointEpsilon = 1e-9;
        private const float WaterComparisonEpsilonMeters = 1e-6f;

        public static RovSafetyResult Evaluate(
            Vector3 candidatePosition,
            Quaternion candidateRotation,
            RovContactProfile profile,
            IAuthoritativeTerrainSurfaceProvider provider,
            float waterSurfaceY)
        {
            if (profile == null || !profile.TryValidate(out _))
            {
                return Hold(RovSafetyState.InvalidProfile,
                    candidatePosition, candidateRotation,
                    default, default, default, default, 0, 0f,
                    "Invalid ROV safety profile.");
            }
            if (provider == null)
            {
                return Hold(RovSafetyState.NoValidTerrainSample,
                    candidatePosition, candidateRotation,
                    default, default, default, default, 0, 0f,
                    "No authoritative terrain provider is available.");
            }
            if (!IsFinite(waterSurfaceY))
            {
                return Hold(RovSafetyState.InvalidWaterAuthority,
                    candidatePosition, candidateRotation,
                    default, default, default, default, 0, 0f,
                    "The bound water surface Y is non-finite.");
            }
            if (!IsFinite(candidatePosition) ||
                !TryNormalize(candidateRotation, out Quaternion normalizedRotation))
            {
                return Hold(RovSafetyState.InvalidPose,
                    Vector3.zero, Quaternion.identity,
                    default, default, default, default, 0, 0f,
                    "The candidate ROV pose is invalid.");
            }

            RovSafetyProbeObservation leftFront = Observe(candidatePosition,
                normalizedRotation, profile.LeftFrontOffset, profile, provider);
            RovSafetyProbeObservation leftRear = Observe(candidatePosition,
                normalizedRotation, profile.LeftRearOffset, profile, provider);
            RovSafetyProbeObservation rightFront = Observe(candidatePosition,
                normalizedRotation, profile.RightFrontOffset, profile, provider);
            RovSafetyProbeObservation rightRear = Observe(candidatePosition,
                normalizedRotation, profile.RightRearOffset, profile, provider);
            int validCount = Valid(leftFront) + Valid(leftRear) +
                Valid(rightFront) + Valid(rightRear);
            float maximumSlope = MaximumSlope(leftFront, leftRear,
                rightFront, rightRear);

            if (HasOtherFailure(leftFront) || HasOtherFailure(leftRear) ||
                HasOtherFailure(rightFront) || HasOtherFailure(rightRear))
            {
                return Hold(RovSafetyState.NoValidTerrainSample,
                    candidatePosition, candidateRotation,
                    leftFront, leftRear, rightFront, rightRear,
                    validCount, maximumSlope,
                    "No valid authoritative terrain sample exists.");
            }
            if (validCount != 4)
            {
                return Hold(RovSafetyState.OutOfTerrainBounds,
                    candidatePosition, candidateRotation,
                    leftFront, leftRear, rightFront, rightRear,
                    validCount, maximumSlope,
                    "The ROV underside envelope is outside terrain coverage.");
            }
            if (!IsFinite(maximumSlope) ||
                maximumSlope > profile.MaximumSlopeDegrees)
            {
                return Hold(RovSafetyState.SlopeRejected,
                    candidatePosition, candidateRotation,
                    leftFront, leftRear, rightFront, rightRear,
                    validCount, maximumSlope,
                    "Terrain slope exceeds the approved ROV limit.");
            }

            float correction = Mathf.Max(0f,
                Mathf.Max(leftFront.RequiredCorrectionMeters,
                Mathf.Max(leftRear.RequiredCorrectionMeters,
                Mathf.Max(rightFront.RequiredCorrectionMeters,
                    rightRear.RequiredCorrectionMeters))));
            if (!IsFinite(correction) ||
                correction > profile.MaximumVerticalCorrectionMeters)
            {
                return Hold(RovSafetyState.CorrectionRejected,
                    candidatePosition, candidateRotation,
                    leftFront, leftRear, rightFront, rightRear,
                    validCount, maximumSlope,
                    "Required upward correction exceeds the approved ROV limit.");
            }

            Vector3 output = candidatePosition + Vector3.up * correction;
            float maximumEnvelopeY = float.NegativeInfinity;
            for (int index = 0; index < profile.UpperEnvelopeCornerCount; index++)
            {
                Vector3 corner = output + normalizedRotation *
                    profile.GetUpperEnvelopeCorner(index);
                if (!IsFinite(corner))
                {
                    return Hold(RovSafetyState.InvalidProfile,
                        candidatePosition, candidateRotation,
                        leftFront, leftRear, rightFront, rightRear,
                        validCount, maximumSlope,
                        "A transformed ROV upper-envelope corner is invalid.");
                }
                maximumEnvelopeY = Mathf.Max(maximumEnvelopeY, corner.y);
            }
            if (maximumEnvelopeY > waterSurfaceY +
                WaterComparisonEpsilonMeters)
            {
                return new RovSafetyResult(
                    RovSafetyState.WaterSurfaceBreach,
                    candidatePosition,
                    candidateRotation,
                    correction,
                    maximumSlope,
                    leftFront, leftRear, rightFront, rightRear,
                    validCount,
                    maximumEnvelopeY,
                    waterSurfaceY,
                    "WaterSurfaceBreach: envelopeMaxY=" +
                    maximumEnvelopeY.ToString("0.######",
                        CultureInfo.InvariantCulture) +
                    " m, waterY=" + waterSurfaceY.ToString("0.######",
                        CultureInfo.InvariantCulture) + " m.");
            }

            RovSafetyState state = correction <= profile.EpsilonMeters
                ? RovSafetyState.Supported
                : RovSafetyState.Corrected;
            return new RovSafetyResult(
                state, output, candidateRotation, correction, maximumSlope,
                leftFront, leftRear, rightFront, rightRear, validCount,
                maximumEnvelopeY, waterSurfaceY, state.ToString());
        }

        public static bool TryValidateRoute(
            ActiveRouteSnapshot route,
            in CoordinateTransformProfile transformProfile,
            RovContactProfile profile,
            TerrainSurfaceSampler provider,
            float waterSurfaceY,
            out string error,
            out RouteSafetyFailureDiagnostic diagnostic)
        {
            error = string.Empty;
            diagnostic = RouteSafetyFailureDiagnostic.None;
            if (route == null || route.VehicleType != VehicleType.Rov)
            {
                error = "A ROV route is required for safety validation.";
                return false;
            }
            if (profile == null || !profile.TryValidate(out error))
                return false;
            TerrainAuthorityFailure failure = TerrainAuthorityFailure.None;
            if (provider == null ||
                !provider.TryValidateAuthority(out failure))
            {
                error = "ROV terrain authority validation failed: " +
                    failure + ".";
                return false;
            }
            if (!IsFinite(waterSurfaceY))
            {
                error = "A finite bound water surface Y is required.";
                return false;
            }

            Vector2 authorityMin = provider.AuthorityLocalMin;
            Vector2 authorityMax = provider.AuthorityLocalMax;
            float spacing = provider.AuthorityGridSpacing;
            Vector3 terrainOrigin = provider.ContactTerrain.transform.position;
            if (!IsFinite(spacing) || spacing <= 0f)
            {
                error = "ROV terrain topology metadata is unavailable.";
                return false;
            }

            for (int segment = 0; segment < route.WaypointCount - 1; segment++)
            {
                Vector3d start = route.GetWaypoint(segment);
                Vector3d end = route.GetWaypoint(segment + 1);
                Quaterniond routeOrientation =
                    VehicleRouteRuntime.ResolveSegmentOrientation(route, segment);
                if (!TryConvertRoutePose(route.VehicleId, start,
                        routeOrientation, transformProfile,
                        out Vector3 startUnity, out Quaternion unityRotation,
                        out error) ||
                    !TryConvertRoutePose(route.VehicleId, end,
                        routeOrientation, transformProfile,
                        out Vector3 endUnity, out _, out error))
                {
                    return false;
                }

                var breakpoints = new List<double> { 0.0, 1.0 };
                AddFootprintBreakpoints(startUnity, endUnity, unityRotation,
                    profile.LeftFrontOffset, authorityMin, authorityMax,
                    terrainOrigin, spacing, breakpoints);
                AddFootprintBreakpoints(startUnity, endUnity, unityRotation,
                    profile.LeftRearOffset, authorityMin, authorityMax,
                    terrainOrigin, spacing, breakpoints);
                AddFootprintBreakpoints(startUnity, endUnity, unityRotation,
                    profile.RightFrontOffset, authorityMin, authorityMax,
                    terrainOrigin, spacing, breakpoints);
                AddFootprintBreakpoints(startUnity, endUnity, unityRotation,
                    profile.RightRearOffset, authorityMin, authorityMax,
                    terrainOrigin, spacing, breakpoints);
                breakpoints.Sort();

                double previous = double.NaN;
                for (int index = 0; index < breakpoints.Count; index++)
                {
                    double t = breakpoints[index];
                    if (!double.IsNaN(previous) &&
                        Math.Abs(t - previous) <= BreakpointEpsilon)
                        continue;
                    previous = t;
                    Vector3 position = Vector3.LerpUnclamped(
                        startUnity, endUnity, (float)t);
                    RovSafetyResult result = Evaluate(position, unityRotation,
                        profile, provider, waterSurfaceY);
                    if (!result.MayApply)
                    {
                        diagnostic = new RouteSafetyFailureDiagnostic(
                            segment + 1, t * 100.0, result.State.ToString());
                        error = "ROV safety rejected segment " +
                            (segment + 1) + " at " +
                            (t * 100.0).ToString("0.0",
                                CultureInfo.InvariantCulture) + "%: " +
                            result.State + ". " + result.Reason;
                        return false;
                    }
                }
            }
            return true;
        }

        private static void AddFootprintBreakpoints(
            Vector3 start,
            Vector3 end,
            Quaternion rotation,
            Vector3 offset,
            Vector2 authorityMin,
            Vector2 authorityMax,
            Vector3 terrainOrigin,
            float spacing,
            ICollection<double> values)
        {
            Vector3 first = start + rotation * offset - terrainOrigin;
            Vector3 last = end + rotation * offset - terrainOrigin;
            AddAxisCrossings(first.x, last.x,
                authorityMin.x, authorityMax.x, spacing, values);
            AddAxisCrossings(first.z, last.z,
                authorityMin.y, authorityMax.y, spacing, values);

            double firstDiagonal =
                (first.z - authorityMin.y) - (first.x - authorityMin.x);
            double lastDiagonal =
                (last.z - authorityMin.y) - (last.x - authorityMin.x);
            double delta = lastDiagonal - firstDiagonal;
            if (Math.Abs(delta) <= BreakpointEpsilon)
                return;
            double minimum = Math.Min(firstDiagonal, lastDiagonal);
            double maximum = Math.Max(firstDiagonal, lastDiagonal);
            int firstIndex = (int)Math.Ceiling(minimum / spacing);
            int lastIndex = (int)Math.Floor(maximum / spacing);
            for (int index = firstIndex; index <= lastIndex; index++)
            {
                AddBreakpoint((index * spacing - firstDiagonal) / delta,
                    values);
            }
        }

        private static void AddAxisCrossings(
            double start,
            double end,
            double minimum,
            double maximum,
            double spacing,
            ICollection<double> values)
        {
            double delta = end - start;
            if (Math.Abs(delta) <= BreakpointEpsilon)
                return;
            int count = (int)Math.Round((maximum - minimum) / spacing);
            for (int index = 0; index <= count; index++)
            {
                AddBreakpoint((minimum + index * spacing - start) / delta,
                    values);
            }
        }

        private static void AddBreakpoint(
            double value,
            ICollection<double> values)
        {
            if (value > BreakpointEpsilon &&
                value < 1.0 - BreakpointEpsilon)
                values.Add(value);
        }

        private static RovSafetyProbeObservation Observe(
            Vector3 position,
            Quaternion rotation,
            Vector3 offset,
            RovContactProfile profile,
            IAuthoritativeTerrainSurfaceProvider provider)
        {
            Vector3 projected = position + rotation * offset;
            bool succeeded = provider.TrySampleAtXZ(projected.x, projected.z,
                out TerrainAuthoritySample sample,
                out TerrainAuthorityFailure failure);
            float required = succeeded
                ? sample.WorldPoint.y + profile.GroundClearance - projected.y
                : 0f;
            return new RovSafetyProbeObservation(
                projected, succeeded, sample, failure, required);
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
            var state = new VehicleState(vehicleId, VehicleType.Rov,
                0.0, 0UL, position, orientation,
                Vector3d.Zero, Vector3d.Zero, Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                profile.SourceWorldFrame, profile.SourceBodyFrame);
            if (!VehiclePoseConverter.TryConvert(in state, in profile,
                    out ConvertedVehiclePose converted,
                    out ConversionError conversionError) ||
                !UnityPoseAdapter.TryConvert(converted.Position,
                    converted.Orientation, out unityPosition,
                    out unityRotation))
            {
                error = "ROV route pose conversion failed: " +
                    conversionError.Message;
                unityPosition = default;
                unityRotation = default;
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static RovSafetyResult Hold(
            RovSafetyState state,
            Vector3 position,
            Quaternion rotation,
            RovSafetyProbeObservation leftFront,
            RovSafetyProbeObservation leftRear,
            RovSafetyProbeObservation rightFront,
            RovSafetyProbeObservation rightRear,
            int validCount,
            float maximumSlope,
            string reason)
        {
            return new RovSafetyResult(state, position, rotation, 0f,
                maximumSlope, leftFront, leftRear, rightFront, rightRear,
                validCount, float.NaN, float.NaN, reason);
        }

        private static bool HasOtherFailure(RovSafetyProbeObservation value)
        {
            return !value.HasValidSample &&
                value.FailureReason != TerrainAuthorityFailure.OutsideCoverage &&
                value.FailureReason != TerrainAuthorityFailure.TopologyHole;
        }

        private static int Valid(RovSafetyProbeObservation value)
        {
            return value.HasValidSample ? 1 : 0;
        }

        private static float MaximumSlope(
            RovSafetyProbeObservation first,
            RovSafetyProbeObservation second,
            RovSafetyProbeObservation third,
            RovSafetyProbeObservation fourth)
        {
            float value = first.HasValidSample ? first.Sample.SlopeDegrees : 0f;
            if (second.HasValidSample) value = Mathf.Max(value, second.Sample.SlopeDegrees);
            if (third.HasValidSample) value = Mathf.Max(value, third.Sample.SlopeDegrees);
            if (fourth.HasValidSample) value = Mathf.Max(value, fourth.Sample.SlopeDegrees);
            return value;
        }

        private static bool TryNormalize(Quaternion value, out Quaternion normalized)
        {
            normalized = Quaternion.identity;
            if (!IsFinite(value.x) || !IsFinite(value.y) ||
                !IsFinite(value.z) || !IsFinite(value.w))
                return false;
            double squared = (double)value.x * value.x +
                (double)value.y * value.y +
                (double)value.z * value.z +
                (double)value.w * value.w;
            if (double.IsNaN(squared) || double.IsInfinity(squared) ||
                squared <= MinimumRotationSquaredMagnitude)
                return false;
            float inverse = (float)(1.0 / Math.Sqrt(squared));
            normalized = new Quaternion(value.x * inverse,
                value.y * inverse, value.z * inverse, value.w * inverse);
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
