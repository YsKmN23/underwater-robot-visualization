using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Auv
{
    [Serializable]
    public sealed class AuvTerrainClearanceProfile : ISerializationCallbackReceiver
    {
        private const float ApprovedProbeDistanceMeters = 5.0f;
        private const float LegacyApprovedProbeDistanceMeters = 4.0f;
        private const float ApprovedMinimumSubmergenceMeters = 0.18f;
        private const float ApprovedMaximumClimbAngleDegrees = 45.0f;
        private const float ApprovedMaximumDescentAngleDegrees = 45.0f;

        private const float AuditedMinimumX = -3.174000f;
        private const float AuditedMaximumX = 3.053000f;
        private const float AuditedMinimumY = -0.512817f;
        private const float AuditedMaximumY = 0.8300005f;
        private const float AuditedMinimumZ = -0.499817f;
        private const float AuditedMaximumZ = 0.480817f;

        [SerializeField] private Vector3[] lowerEnvelopeProbeOffsets;
        [SerializeField] private Vector3[] hullEnvelopeCornerOffsets;
        [SerializeField] private float minimumHullClearanceMeters;
        [SerializeField] private float minimumHullSubmergenceMeters;
        [SerializeField] private float maximumUpwardCorrectionMeters;
        [SerializeField] private LayerMask terrainLayerMask;
        [SerializeField] private float probeStartHeightMeters;
        [SerializeField] private float probeDistanceMeters;
        [SerializeField] private float maximumSlopeDegrees;
        [SerializeField] private float samplingToleranceMeters;
        [SerializeField] private float segmentValidationSpacingMeters;
        [SerializeField] private float maximumClimbAngleDegrees;
        [SerializeField] private float maximumDescentAngleDegrees;

        public AuvTerrainClearanceProfile(
            Vector3[] configuredProbeOffsets,
            float configuredMinimumHullClearanceMeters,
            float configuredMaximumUpwardCorrectionMeters,
            LayerMask configuredTerrainLayerMask,
            float configuredProbeStartHeightMeters,
            float configuredProbeDistanceMeters,
            float configuredMaximumSlopeDegrees,
            float configuredSamplingToleranceMeters,
            float configuredSegmentValidationSpacingMeters)
            : this(
                configuredProbeOffsets,
                CreateAuditedHullEnvelopeCorners(),
                configuredMinimumHullClearanceMeters,
                ApprovedMinimumSubmergenceMeters,
                configuredMaximumUpwardCorrectionMeters,
                configuredTerrainLayerMask,
                configuredProbeStartHeightMeters,
                configuredProbeDistanceMeters,
                configuredMaximumSlopeDegrees,
                configuredSamplingToleranceMeters,
                configuredSegmentValidationSpacingMeters,
                ApprovedMaximumClimbAngleDegrees,
                ApprovedMaximumDescentAngleDegrees)
        {
        }

        public AuvTerrainClearanceProfile(
            Vector3[] configuredProbeOffsets,
            Vector3[] configuredHullEnvelopeCornerOffsets,
            float configuredMinimumHullClearanceMeters,
            float configuredMinimumHullSubmergenceMeters,
            float configuredMaximumUpwardCorrectionMeters,
            LayerMask configuredTerrainLayerMask,
            float configuredProbeStartHeightMeters,
            float configuredProbeDistanceMeters,
            float configuredMaximumSlopeDegrees,
            float configuredSamplingToleranceMeters,
            float configuredSegmentValidationSpacingMeters,
            float configuredMaximumClimbAngleDegrees,
            float configuredMaximumDescentAngleDegrees)
        {
            lowerEnvelopeProbeOffsets = configuredProbeOffsets == null
                ? null
                : (Vector3[])configuredProbeOffsets.Clone();
            hullEnvelopeCornerOffsets =
                configuredHullEnvelopeCornerOffsets == null
                    ? null
                    : (Vector3[])configuredHullEnvelopeCornerOffsets.Clone();
            minimumHullClearanceMeters =
                configuredMinimumHullClearanceMeters;
            minimumHullSubmergenceMeters =
                configuredMinimumHullSubmergenceMeters;
            maximumUpwardCorrectionMeters =
                configuredMaximumUpwardCorrectionMeters;
            terrainLayerMask = configuredTerrainLayerMask;
            probeStartHeightMeters = configuredProbeStartHeightMeters;
            probeDistanceMeters = configuredProbeDistanceMeters;
            maximumSlopeDegrees = configuredMaximumSlopeDegrees;
            samplingToleranceMeters = configuredSamplingToleranceMeters;
            segmentValidationSpacingMeters =
                configuredSegmentValidationSpacingMeters;
            maximumClimbAngleDegrees = configuredMaximumClimbAngleDegrees;
            maximumDescentAngleDegrees = configuredMaximumDescentAngleDegrees;
        }

        public IReadOnlyList<Vector3> LowerEnvelopeProbeOffsets =>
            lowerEnvelopeProbeOffsets;
        public int ProbeCount => lowerEnvelopeProbeOffsets == null
            ? 0
            : lowerEnvelopeProbeOffsets.Length;
        public IReadOnlyList<Vector3> HullEnvelopeCornerOffsets =>
            hullEnvelopeCornerOffsets;
        public int HullEnvelopeCornerCount => hullEnvelopeCornerOffsets == null
            ? 0
            : hullEnvelopeCornerOffsets.Length;
        public float MinimumHullClearanceMeters =>
            minimumHullClearanceMeters;
        public float MinimumHullSubmergenceMeters =>
            minimumHullSubmergenceMeters;
        public float MaximumUpwardCorrectionMeters =>
            maximumUpwardCorrectionMeters;
        public LayerMask TerrainLayerMask => terrainLayerMask;
        public float ProbeStartHeightMeters => probeStartHeightMeters;
        public float ProbeDistanceMeters => probeDistanceMeters;
        public float MaximumSlopeDegrees => maximumSlopeDegrees;
        public float SamplingToleranceMeters => samplingToleranceMeters;
        public float SegmentValidationSpacingMeters =>
            segmentValidationSpacingMeters;
        public float MaximumClimbAngleDegrees => maximumClimbAngleDegrees;
        public float MaximumDescentAngleDegrees => maximumDescentAngleDegrees;

        public static AuvTerrainClearanceProfile CreateApprovedDefault()
        {
            // Frozen from the 72-renderer combined local bounds:
            // center=(-0.061000,0.158592,-0.009500),
            // size=(6.225999,1.342817,0.980633).
            const float minX = AuditedMinimumX;
            const float centerX = -0.061000f;
            const float maxX = AuditedMaximumX;
            const float lowerY = AuditedMinimumY;
            const float centerZ = -0.009500f;
            const float minZ = AuditedMinimumZ;
            const float maxZ = AuditedMaximumZ;
            float quarterSpan = (maxX - minX) * 0.25f;
            return new AuvTerrainClearanceProfile(
                new[]
                {
                    new Vector3(minX, lowerY, centerZ),
                    new Vector3(minX + quarterSpan, lowerY, centerZ),
                    new Vector3(centerX, lowerY, centerZ),
                    new Vector3(maxX - quarterSpan, lowerY, centerZ),
                    new Vector3(maxX, lowerY, centerZ),
                    new Vector3(centerX, lowerY, minZ),
                    new Vector3(centerX, lowerY, maxZ)
                },
                CreateAuditedHullEnvelopeCorners(),
                // 1.55625 m maximum longitudinal probe gap at the frozen
                // 12-degree slope limit implies 0.1654 m midpoint rise.
                // 0.18 m covers that envelope with about 15 mm margin.
                0.18f,
                ApprovedMinimumSubmergenceMeters,
                0.50f,
                1 << 0,
                2.0f,
                // probeStartHeight=2.0; maximum current-scene probe-to-terrain
                // separation=2.703145; margin=0.106071; total=4.809216;
                // approved rounded reach=5.0.
                ApprovedProbeDistanceMeters,
                12.0f,
                0.002f,
                0.50f,
                ApprovedMaximumClimbAngleDegrees,
                ApprovedMaximumDescentAngleDegrees);
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            // Upgrade only the frozen legacy approved profile. This keeps the
            // checked-in Scene read-only while applying the corrected reach.
            if (probeDistanceMeters.Equals(LegacyApprovedProbeDistanceMeters) &&
                probeStartHeightMeters.Equals(2.0f) &&
                minimumHullClearanceMeters.Equals(0.18f) &&
                maximumUpwardCorrectionMeters.Equals(0.50f) &&
                terrainLayerMask.value == 1 &&
                maximumSlopeDegrees.Equals(12.0f) &&
                samplingToleranceMeters.Equals(0.002f) &&
                segmentValidationSpacingMeters.Equals(0.50f) &&
                ProbeCount == 7)
                probeDistanceMeters = ApprovedProbeDistanceMeters;

            if (IsApprovedLegacyProfile())
            {
                if (hullEnvelopeCornerOffsets == null ||
                    hullEnvelopeCornerOffsets.Length == 0)
                    hullEnvelopeCornerOffsets =
                        CreateAuditedHullEnvelopeCorners();
                if (minimumHullSubmergenceMeters.Equals(0f))
                    minimumHullSubmergenceMeters =
                        ApprovedMinimumSubmergenceMeters;
                if (maximumClimbAngleDegrees.Equals(0f))
                    maximumClimbAngleDegrees =
                        ApprovedMaximumClimbAngleDegrees;
                if (maximumDescentAngleDegrees.Equals(0f))
                    maximumDescentAngleDegrees =
                        ApprovedMaximumDescentAngleDegrees;
            }
        }

        public bool TryValidate(out string error)
        {
            if (lowerEnvelopeProbeOffsets == null ||
                lowerEnvelopeProbeOffsets.Length < 5 ||
                lowerEnvelopeProbeOffsets.Length > 7)
            {
                error = "AUV clearance requires five to seven probes.";
                return false;
            }

            var unique = new HashSet<Vector3>();
            float minimumX = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            for (int index = 0;
                 index < lowerEnvelopeProbeOffsets.Length;
                 index++)
            {
                Vector3 offset = lowerEnvelopeProbeOffsets[index];
                if (!IsFinite(offset) || !unique.Add(offset))
                {
                    error = "AUV clearance probes must be finite and unique.";
                    return false;
                }
                minimumX = Mathf.Min(minimumX, offset.x);
                maximumX = Mathf.Max(maximumX, offset.x);
            }

            if (maximumX - minimumX < 5.5f)
            {
                error = "AUV clearance probes do not cover the hull length.";
                return false;
            }

            if (hullEnvelopeCornerOffsets == null ||
                hullEnvelopeCornerOffsets.Length != 8)
            {
                error = "AUV surface safety requires eight hull-envelope corners.";
                return false;
            }
            unique.Clear();
            for (int index = 0;
                 index < hullEnvelopeCornerOffsets.Length;
                 index++)
            {
                Vector3 offset = hullEnvelopeCornerOffsets[index];
                if (!IsFinite(offset) || !unique.Add(offset))
                {
                    error = "AUV hull-envelope corners must be finite and unique.";
                    return false;
                }
            }

            if (!IsFinite(minimumHullClearanceMeters) ||
                minimumHullClearanceMeters < 0f ||
                !IsFinite(minimumHullSubmergenceMeters) ||
                minimumHullSubmergenceMeters < 0f ||
                !IsFinite(maximumUpwardCorrectionMeters) ||
                maximumUpwardCorrectionMeters <= 0f ||
                !IsFinite(probeStartHeightMeters) ||
                probeStartHeightMeters <= maximumUpwardCorrectionMeters ||
                !IsFinite(probeDistanceMeters) ||
                probeDistanceMeters <= probeStartHeightMeters ||
                !IsFinite(maximumSlopeDegrees) ||
                maximumSlopeDegrees < 0f || maximumSlopeDegrees > 90f ||
                !IsFinite(samplingToleranceMeters) ||
                samplingToleranceMeters < 0f ||
                samplingToleranceMeters > minimumHullClearanceMeters ||
                samplingToleranceMeters > minimumHullSubmergenceMeters ||
                !IsFinite(segmentValidationSpacingMeters) ||
                segmentValidationSpacingMeters <= 0f ||
                segmentValidationSpacingMeters > 0.5f ||
                !IsFinite(maximumClimbAngleDegrees) ||
                maximumClimbAngleDegrees <= 0f ||
                maximumClimbAngleDegrees >= 90f ||
                !IsFinite(maximumDescentAngleDegrees) ||
                maximumDescentAngleDegrees <= 0f ||
                maximumDescentAngleDegrees >= 90f ||
                terrainLayerMask.value == 0)
            {
                error = "One or more AUV clearance scalar values are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool IsApprovedLegacyProfile()
        {
            return probeDistanceMeters.Equals(ApprovedProbeDistanceMeters) &&
                probeStartHeightMeters.Equals(2.0f) &&
                minimumHullClearanceMeters.Equals(0.18f) &&
                maximumUpwardCorrectionMeters.Equals(0.50f) &&
                terrainLayerMask.value == 1 &&
                maximumSlopeDegrees.Equals(12.0f) &&
                samplingToleranceMeters.Equals(0.002f) &&
                segmentValidationSpacingMeters.Equals(0.50f) &&
                ProbeCount == 7;
        }

        private static Vector3[] CreateAuditedHullEnvelopeCorners()
        {
            return new[]
            {
                new Vector3(AuditedMinimumX, AuditedMinimumY, AuditedMinimumZ),
                new Vector3(AuditedMinimumX, AuditedMinimumY, AuditedMaximumZ),
                new Vector3(AuditedMinimumX, AuditedMaximumY, AuditedMinimumZ),
                new Vector3(AuditedMinimumX, AuditedMaximumY, AuditedMaximumZ),
                new Vector3(AuditedMaximumX, AuditedMinimumY, AuditedMinimumZ),
                new Vector3(AuditedMaximumX, AuditedMinimumY, AuditedMaximumZ),
                new Vector3(AuditedMaximumX, AuditedMaximumY, AuditedMinimumZ),
                new Vector3(AuditedMaximumX, AuditedMaximumY, AuditedMaximumZ)
            };
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
