using System;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Rov
{
    public enum RovTerrainContactDecision
    {
        Apply = 0,
        HoldCurrent = 1
    }

    public enum RovTerrainContactState
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

    public readonly struct RovContactProbeObservation
    {
        internal RovContactProbeObservation(RovSafetyProbeObservation value)
        {
            ProjectedContactPoint = value.ProjectedPoint;
            HasValidSample = value.HasValidSample;
            Sample = new TerrainSurfaceSample(
                value.Sample.WorldPoint,
                value.Sample.WorldNormal,
                0f,
                value.Sample.SlopeDegrees,
                value.Sample.TriangleIndex);
            AuthorityFailureReason = value.FailureReason;
            FailureReason = value.FailureReason == TerrainAuthorityFailure.None
                ? TerrainSurfaceSampleFailureReason.None
                : value.FailureReason == TerrainAuthorityFailure.OutsideCoverage ||
                  value.FailureReason == TerrainAuthorityFailure.TopologyHole
                    ? TerrainSurfaceSampleFailureReason.NoHit
                    : TerrainSurfaceSampleFailureReason.InvalidHit;
            RequiredVerticalCorrectionMeters = value.RequiredCorrectionMeters;
        }

        public Vector3 ProjectedContactPoint { get; }
        public bool HasValidSample { get; }
        public TerrainSurfaceSample Sample { get; }
        public TerrainSurfaceSampleFailureReason FailureReason { get; }
        public TerrainAuthorityFailure AuthorityFailureReason { get; }
        public float RequiredVerticalCorrectionMeters { get; }
    }

    public readonly struct RovTerrainContactResult
    {
        internal RovTerrainContactResult(RovSafetyResult value)
        {
            Decision = value.MayApply
                ? RovTerrainContactDecision.Apply
                : RovTerrainContactDecision.HoldCurrent;
            State = (RovTerrainContactState)value.State;
            OutputPosition = value.OutputPosition;
            OutputRotation = value.OutputRotation;
            DeltaY = value.MayApply ? value.CorrectionMeters : 0f;
            MaximumObservedSlopeDegrees = value.MaximumSlopeDegrees;
            ValidSampleCount = value.ValidSampleCount;
            LeftFront = new RovContactProbeObservation(value.LeftFront);
            LeftRear = new RovContactProbeObservation(value.LeftRear);
            RightFront = new RovContactProbeObservation(value.RightFront);
            RightRear = new RovContactProbeObservation(value.RightRear);
            MaximumEnvelopeWorldY = value.MaximumEnvelopeWorldY;
            WaterSurfaceY = value.WaterSurfaceY;
            Reason = value.Reason;
        }

        public RovTerrainContactDecision Decision { get; }
        public RovTerrainContactState State { get; }
        public Vector3 OutputPosition { get; }
        public Quaternion OutputRotation { get; }
        public float DeltaY { get; }
        public float MaximumObservedSlopeDegrees { get; }
        public int ValidSampleCount { get; }
        public RovContactProbeObservation LeftFront { get; }
        public RovContactProbeObservation LeftRear { get; }
        public RovContactProbeObservation RightFront { get; }
        public RovContactProbeObservation RightRear { get; }
        public float MaximumEnvelopeWorldY { get; }
        public float WaterSurfaceY { get; }
        public string Reason { get; }
    }

    [DisallowMultipleComponent]
    public sealed class RovTerrainContactConstraint : MonoBehaviour,
        IUnityPoseConstraint,
        IRouteSafetyValidator,
        IRouteSafetyDiagnosticProvider
    {
        [SerializeField] private TerrainSurfaceSampler surfaceSampler;
        [SerializeField] private RovContactProfile profile;
        [SerializeField] private FlatWaterSurfaceProvider waterSurfaceProvider;

        public TerrainSurfaceSampler SurfaceSampler => surfaceSampler;
        public RovContactProfile Profile => profile;
        public FlatWaterSurfaceProvider WaterSurfaceProvider =>
            waterSurfaceProvider;
        public RouteSafetyFailureDiagnostic LastRouteSafetyFailure { get; private set; } =
            RouteSafetyFailureDiagnostic.None;

        public void Configure(
            TerrainSurfaceSampler sampler,
            RovContactProfile contactProfile,
            FlatWaterSurfaceProvider waterAuthority)
        {
            surfaceSampler = sampler == null
                ? throw new ArgumentNullException(nameof(sampler))
                : sampler;
            profile = contactProfile == null
                ? throw new ArgumentNullException(nameof(contactProfile))
                : contactProfile;
            waterSurfaceProvider = waterAuthority == null
                ? throw new ArgumentNullException(nameof(waterAuthority))
                : waterAuthority;
        }

        public bool TryValidate(out string error)
        {
            error = string.Empty;
            if (surfaceSampler == null)
            {
                error = "A terrain surface sampler is required.";
                return false;
            }
            if (profile == null || !profile.TryValidate(out error))
                return false;
            if (!TryGetWaterSurfaceY(out _, out error))
                return false;
            if (!surfaceSampler.TryValidateAuthority(
                    out TerrainAuthorityFailure failure))
            {
                error = "ROV terrain authority validation failed: " +
                    failure + ".";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public RovTerrainContactResult Evaluate(
            Vector3 position,
            Quaternion rotation)
        {
            return new RovTerrainContactResult(RovSafetyEvaluator.Evaluate(
                position,
                rotation,
                profile,
                surfaceSampler,
                TryGetWaterSurfaceY(out float waterSurfaceY, out _)
                    ? waterSurfaceY
                    : float.NaN));
        }

        public UnityPoseConstraintResult Constrain(
            in UnityPoseConstraintRequest request)
        {
            RovTerrainContactResult result = Evaluate(
                request.Position, request.Rotation);
            return new UnityPoseConstraintResult(
                result.Decision == RovTerrainContactDecision.Apply
                    ? UnityPoseConstraintDecision.Apply
                    : UnityPoseConstraintDecision.HoldCurrent,
                result.OutputPosition,
                result.OutputRotation,
                string.IsNullOrEmpty(result.Reason)
                    ? result.State.ToString()
                    : result.Reason);
        }

        public bool TryValidateRoute(
            ActiveRouteSnapshot candidate,
            in CoordinateTransformProfile transformProfile,
            out string error)
        {
            LastRouteSafetyFailure = RouteSafetyFailureDiagnostic.None;
            if (!TryValidate(out error))
                return false;
            bool valid = RovSafetyEvaluator.TryValidateRoute(
                candidate,
                in transformProfile,
                profile,
                surfaceSampler,
                TryGetWaterSurfaceY(out float waterSurfaceY, out _)
                    ? waterSurfaceY
                    : float.NaN,
                out error,
                out RouteSafetyFailureDiagnostic diagnostic);
            LastRouteSafetyFailure = diagnostic;
            return valid;
        }

        public void ResetObservation()
        {
        }

        private bool TryGetWaterSurfaceY(
            out float waterSurfaceY,
            out string error)
        {
            waterSurfaceY = float.NaN;
            error = string.Empty;
            if (waterSurfaceProvider == null)
            {
                error = "A bound FlatWaterSurfaceProvider is required.";
                return false;
            }
            Vector3 query = waterSurfaceProvider.transform.position;
            if (!waterSurfaceProvider.TrySample(
                    query,
                    out Vector3 surfacePoint,
                    out Vector3 surfaceNormal) ||
                Vector3.Dot(surfaceNormal, Vector3.up) < 0.999999f)
            {
                error = "The bound ROV water authority must be a finite horizontal surface.";
                return false;
            }
            waterSurfaceY = surfacePoint.y;
            return true;
        }
    }
}
