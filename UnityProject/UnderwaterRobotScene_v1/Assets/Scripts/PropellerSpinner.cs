using UnityEngine;

namespace UnderwaterRobotScene
{
    public class PropellerSpinner : MonoBehaviour
    {
        public Vector3 localAxis = Vector3.forward;
        public float rpm = 720f;

        private void Update()
        {
            if (localAxis.sqrMagnitude < 0.001f)
            {
                return;
            }

            transform.Rotate(localAxis.normalized, rpm * 6f * Time.deltaTime, Space.Self);
        }
    }
}
