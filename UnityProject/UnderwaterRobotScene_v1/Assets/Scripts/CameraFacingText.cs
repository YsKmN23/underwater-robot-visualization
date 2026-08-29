using UnityEngine;

namespace UnderwaterRobotScene
{
    public class CameraFacingText : MonoBehaviour
    {
        private const float ScreenScaleReferenceDistance = 8f;
        private const float MinimumScreenScaleMultiplier = 0.85f;
        private const float MaximumScreenScaleMultiplier = 2.25f;

        public enum BillboardMode
        {
            FaceCameraPosition = 0,
            ScreenParallel = 1
        }

        [SerializeField]
        private BillboardMode billboardMode =
            BillboardMode.FaceCameraPosition;

        private Camera targetCamera;
        private Vector3 baseLocalScale;
        private bool baseScaleCaptured;

        public BillboardMode Mode => billboardMode;

        public void SetBillboardMode(BillboardMode mode)
        {
            billboardMode = mode;
        }

        private void Awake()
        {
            CapturePositiveBaseScale();
        }

        private void LateUpdate()
        {
            if (!baseScaleCaptured)
            {
                CapturePositiveBaseScale();
            }
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                return;
            }

            if (billboardMode == BillboardMode.ScreenParallel)
            {
                transform.rotation = targetCamera.transform.rotation;
                float distance = Vector3.Distance(
                    transform.position,
                    targetCamera.transform.position);
                float multiplier = Mathf.Clamp(
                    distance / ScreenScaleReferenceDistance,
                    MinimumScreenScaleMultiplier,
                    MaximumScreenScaleMultiplier);
                transform.localScale = baseLocalScale * multiplier;
                return;
            }

            Vector3 awayFromCamera = transform.position - targetCamera.transform.position;
            if (awayFromCamera.sqrMagnitude < 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(awayFromCamera.normalized, Vector3.up);
        }

        private void CapturePositiveBaseScale()
        {
            Vector3 scale = transform.localScale;
            baseLocalScale = new Vector3(
                Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
            baseScaleCaptured = true;
        }
    }
}
