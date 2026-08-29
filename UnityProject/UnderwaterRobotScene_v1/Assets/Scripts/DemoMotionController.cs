using System;
using UnityEngine;
using UnderwaterRobotScene.Visualization.Runtime;

namespace UnderwaterRobotScene
{
    public class DemoMotionController : MonoBehaviour
    {
        public Transform auv;
        public Transform usv;
        public Transform rov;
        public VehiclePoseControlAuthority auvControlAuthority;
        public VehiclePoseControlAuthority usvControlAuthority;
        public VehiclePoseControlAuthority rovControlAuthority;

        [Obsolete("V1 status text is owned by the dedicated status panel presenter.")]
        public TextMesh dataPanel
        {
            set { }
        }

        public bool DrivesAuv => false;
        public bool DrivesRov => false;
        public bool DrivesUsv => false;
        public bool AuvDemoSelected =>
            auvControlAuthority != null && auvControlAuthority.DemoOwnsControl;
        public bool RovDemoSelected =>
            rovControlAuthority != null && rovControlAuthority.DemoOwnsControl;
        public bool UsvDemoSelected =>
            usvControlAuthority != null && usvControlAuthority.DemoOwnsControl;

        private void Start()
        {
            if (auv == null)
            {
                GameObject found = GameObject.Find("AUV_Yellow_Underwater");
                if (found != null) auv = found.transform;
            }

            if (usv == null)
            {
                GameObject found = GameObject.Find("USV_Blue_Surface");
                if (found != null) usv = found.transform;
            }

            if (rov == null)
            {
                GameObject found = GameObject.Find("ROV_Box_Seabed");
                if (found != null) rov = found.transform;
            }

            if (auvControlAuthority == null && auv != null)
            {
                auvControlAuthority = auv.GetComponent<VehiclePoseControlAuthority>();
            }

            if (usvControlAuthority == null && usv != null)
            {
                usvControlAuthority = usv.GetComponent<VehiclePoseControlAuthority>();
            }
            if (rovControlAuthority == null && rov != null)
            {
                rovControlAuthority = rov.GetComponent<VehiclePoseControlAuthority>();
            }
        }

    }
}
