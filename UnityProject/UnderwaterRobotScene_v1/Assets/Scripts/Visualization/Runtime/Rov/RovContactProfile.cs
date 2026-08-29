using System;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Rov
{
    [Serializable]
    public sealed class RovContactProfile
    {
        [SerializeField] private Vector3 leftFrontOffset;
        [SerializeField] private Vector3 leftRearOffset;
        [SerializeField] private Vector3 rightFrontOffset;
        [SerializeField] private Vector3 rightRearOffset;
        [SerializeField] private Vector3 upperEnvelopeMinimum;
        [SerializeField] private Vector3 upperEnvelopeMaximum;
        [SerializeField] private float groundClearance;
        [SerializeField] private float probeStartHeightMeters;
        [SerializeField] private float probeDistanceMeters;
        [SerializeField] private float maximumSlopeDegrees;
        [SerializeField] private float maximumVerticalCorrectionMeters;
        [SerializeField] private float epsilonMeters;

        public RovContactProfile(
            Vector3 leftFrontOffset,
            Vector3 leftRearOffset,
            Vector3 rightFrontOffset,
            Vector3 rightRearOffset,
            Vector3 upperEnvelopeMinimum,
            Vector3 upperEnvelopeMaximum,
            float groundClearance,
            float probeStartHeightMeters,
            float probeDistanceMeters,
            float maximumSlopeDegrees,
            float maximumVerticalCorrectionMeters,
            float epsilonMeters)
        {
            this.leftFrontOffset = leftFrontOffset;
            this.leftRearOffset = leftRearOffset;
            this.rightFrontOffset = rightFrontOffset;
            this.rightRearOffset = rightRearOffset;
            this.upperEnvelopeMinimum = upperEnvelopeMinimum;
            this.upperEnvelopeMaximum = upperEnvelopeMaximum;
            this.groundClearance = groundClearance;
            this.probeStartHeightMeters = probeStartHeightMeters;
            this.probeDistanceMeters = probeDistanceMeters;
            this.maximumSlopeDegrees = maximumSlopeDegrees;
            this.maximumVerticalCorrectionMeters =
                maximumVerticalCorrectionMeters;
            this.epsilonMeters = epsilonMeters;
        }

        public Vector3 LeftFrontOffset => leftFrontOffset;
        public Vector3 LeftRearOffset => leftRearOffset;
        public Vector3 RightFrontOffset => rightFrontOffset;
        public Vector3 RightRearOffset => rightRearOffset;
        public Vector3 UpperEnvelopeMinimum => upperEnvelopeMinimum;
        public Vector3 UpperEnvelopeMaximum => upperEnvelopeMaximum;
        public int UpperEnvelopeCornerCount => 8;
        public float GroundClearance => groundClearance;
        public float ProbeStartHeightMeters => probeStartHeightMeters;
        public float ProbeDistanceMeters => probeDistanceMeters;
        public float MaximumSlopeDegrees => maximumSlopeDegrees;
        public float MaximumVerticalCorrectionMeters =>
            maximumVerticalCorrectionMeters;
        public float EpsilonMeters => epsilonMeters;

        public static RovContactProfile CreateApprovedDefault()
        {
            return new RovContactProfile(
                new Vector3(-0.4700000f, -0.7774999f, 1.0150001f),
                new Vector3(-0.4700000f, -0.7774999f, -1.0150002f),
                new Vector3(0.4700000f, -0.7774999f, 1.0150001f),
                new Vector3(0.4700000f, -0.7774999f, -1.0150002f),
                new Vector3(-0.767f, -0.816f, -1.426f),
                new Vector3(0.796f, 0.729f, 1.426f),
                0.015f,
                1f,
                2f,
                12f,
                0.30f,
                0.001f);
        }

        public bool TryValidate(out string error)
        {
            if (!IsFinite(leftFrontOffset) ||
                !IsFinite(leftRearOffset) ||
                !IsFinite(rightFrontOffset) ||
                !IsFinite(rightRearOffset) ||
                !IsFinite(upperEnvelopeMinimum) ||
                !IsFinite(upperEnvelopeMaximum))
            {
                error = "All four contact offsets must be finite.";
                return false;
            }

            if (upperEnvelopeMinimum.x >= upperEnvelopeMaximum.x ||
                upperEnvelopeMinimum.y >= upperEnvelopeMaximum.y ||
                upperEnvelopeMinimum.z >= upperEnvelopeMaximum.z)
            {
                error = "The upper-envelope minimum must be below its maximum on every axis.";
                return false;
            }

            if (leftFrontOffset == leftRearOffset ||
                leftFrontOffset == rightFrontOffset ||
                leftFrontOffset == rightRearOffset ||
                leftRearOffset == rightFrontOffset ||
                leftRearOffset == rightRearOffset ||
                rightFrontOffset == rightRearOffset)
            {
                error = "All four contact offsets must be unique.";
                return false;
            }

            if (!IsFinite(groundClearance) || groundClearance < 0f ||
                !IsFinite(probeStartHeightMeters) ||
                probeStartHeightMeters < 0f ||
                !IsFinite(probeDistanceMeters) ||
                probeDistanceMeters <= 0f ||
                !IsFinite(maximumSlopeDegrees) ||
                maximumSlopeDegrees < 0f || maximumSlopeDegrees > 90f ||
                !IsFinite(maximumVerticalCorrectionMeters) ||
                maximumVerticalCorrectionMeters < 0f ||
                !IsFinite(epsilonMeters) || epsilonMeters < 0f ||
                epsilonMeters > maximumVerticalCorrectionMeters)
            {
                error = "One or more contact constraint scalar values are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public Vector3 GetUpperEnvelopeCorner(int index)
        {
            if (index < 0 || index >= UpperEnvelopeCornerCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new Vector3(
                (index & 4) == 0
                    ? upperEnvelopeMinimum.x
                    : upperEnvelopeMaximum.x,
                (index & 2) == 0
                    ? upperEnvelopeMinimum.y
                    : upperEnvelopeMaximum.y,
                (index & 1) == 0
                    ? upperEnvelopeMinimum.z
                    : upperEnvelopeMaximum.z);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
