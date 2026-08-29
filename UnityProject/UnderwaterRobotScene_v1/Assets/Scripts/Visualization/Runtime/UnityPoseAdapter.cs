using UnderwaterRobotScene.Visualization.Data;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    public static class UnityPoseAdapter
    {
        public static bool TryConvert(
            in Vector3d position,
            in Quaterniond orientation,
            out Vector3 unityPosition,
            out Quaternion unityOrientation)
        {
            unityPosition = default;
            unityOrientation = default;

            if (!position.IsFinite || !orientation.TryNormalize(out Quaterniond normalized))
            {
                return false;
            }

            float x = (float)position.X;
            float y = (float)position.Y;
            float z = (float)position.Z;
            float qx = (float)normalized.X;
            float qy = (float)normalized.Y;
            float qz = (float)normalized.Z;
            float qw = (float)normalized.W;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) ||
                !IsFinite(qx) || !IsFinite(qy) || !IsFinite(qz) || !IsFinite(qw))
            {
                return false;
            }

            var candidate = new Quaternion(qx, qy, qz, qw);
            float magnitudeSquared =
                candidate.x * candidate.x +
                candidate.y * candidate.y +
                candidate.z * candidate.z +
                candidate.w * candidate.w;
            if (!IsFinite(magnitudeSquared) || magnitudeSquared <= 1e-12f)
            {
                return false;
            }

            unityPosition = new Vector3(x, y, z);
            unityOrientation = candidate.normalized;
            return IsFinite(unityOrientation.x) &&
                   IsFinite(unityOrientation.y) &&
                   IsFinite(unityOrientation.z) &&
                   IsFinite(unityOrientation.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
