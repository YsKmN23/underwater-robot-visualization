using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Usv
{
    [DisallowMultipleComponent]
    public sealed class FlatWaterSurfaceProvider : MonoBehaviour
    {
        public bool TrySample(
            Vector3 queryWorldPosition,
            out Vector3 surfacePoint,
            out Vector3 surfaceNormal)
        {
            surfacePoint = default;
            surfaceNormal = default;
            if (!isActiveAndEnabled ||
                !IsFinite(queryWorldPosition) ||
                !IsFinite(transform.position) ||
                !IsFinite(transform.up))
            {
                return false;
            }

            Vector3 normal = transform.up;
            float magnitude = normal.magnitude;
            if (!float.IsFinite(magnitude) || magnitude <= 0.000001f)
            {
                return false;
            }

            normal /= magnitude;
            Vector3 origin = transform.position;
            float signedDistance = Vector3.Dot(queryWorldPosition - origin, normal);
            Vector3 projected = queryWorldPosition - (normal * signedDistance);
            if (!float.IsFinite(signedDistance) ||
                !IsFinite(projected) ||
                !IsFinite(normal))
            {
                return false;
            }

            surfacePoint = projected;
            surfaceNormal = normal;
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }
    }
}
