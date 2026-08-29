using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    public enum VehiclePoseControlMode
    {
        Demo = 0,
        PublicData = 1
    }

    [DisallowMultipleComponent]
    public sealed class VehiclePoseControlAuthority : MonoBehaviour
    {
        [SerializeField] private VehiclePoseControlMode mode = VehiclePoseControlMode.Demo;

        public VehiclePoseControlMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public bool DemoOwnsControl => mode == VehiclePoseControlMode.Demo;
        public bool PublicDataOwnsControl => mode == VehiclePoseControlMode.PublicData;
    }
}
