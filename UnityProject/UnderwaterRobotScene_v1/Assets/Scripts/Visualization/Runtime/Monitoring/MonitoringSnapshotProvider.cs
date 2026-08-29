using UnderwaterRobotScene.Visualization.Data;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Monitoring
{
    [DisallowMultipleComponent]
    public sealed class MonitoringSnapshotProvider : MonoBehaviour
    {
        private VehicleSelectionCameraController selection;
        private VehicleRouteEditingController routeEditor;
        private VehicleDataRuntimeHost auvHost;
        private VehicleDataRuntimeHost rovHost;
        private VehicleDataRuntimeHost usvHost;
        private VehiclePoseDriver auvDriver;
        private VehiclePoseDriver rovDriver;
        private VehiclePoseDriver usvDriver;
        private VehiclePoseControlAuthority auvAuthority;
        private VehiclePoseControlAuthority rovAuthority;
        private VehiclePoseControlAuthority usvAuthority;

        public VehicleSelectionCameraController Selection => selection;

        public MonitoringFleetSnapshot Capture()
        {
            BindMissingReferences();
            VehicleSelectionKind selected = selection == null
                ? VehicleSelectionKind.None
                : selection.SelectedVehicle;
            string selectedOutcome = routeEditor == null
                ? string.Empty
                : routeEditor.LastApplyOutcome;
            return new MonitoringFleetSnapshot(
                VehicleMonitoringSnapshotBuilder.Capture(
                    VehicleType.Auv, auvHost, auvDriver, auvAuthority,
                    selected == VehicleSelectionKind.Auv
                        ? selectedOutcome
                        : string.Empty),
                VehicleMonitoringSnapshotBuilder.Capture(
                    VehicleType.Rov, rovHost, rovDriver, rovAuthority,
                    selected == VehicleSelectionKind.Rov
                        ? selectedOutcome
                        : string.Empty),
                VehicleMonitoringSnapshotBuilder.Capture(
                    VehicleType.Usv, usvHost, usvDriver, usvAuthority,
                    selected == VehicleSelectionKind.Usv
                        ? selectedOutcome
                        : string.Empty),
                selected);
        }

        public void SelectVehicle(VehicleSelectionKind kind)
        {
            BindMissingReferences();
            selection?.SelectVehicle(kind);
        }

        private void BindMissingReferences()
        {
            if (selection == null)
                selection = FindFirstObjectByType<
                    VehicleSelectionCameraController>();
            if (routeEditor == null)
                routeEditor = FindFirstObjectByType<
                    VehicleRouteEditingController>();

            if (auvHost == null || rovHost == null || usvHost == null)
            {
                VehicleDataRuntimeHost[] hosts =
                    FindObjectsByType<VehicleDataRuntimeHost>(
                        FindObjectsSortMode.None);
                for (int index = 0; index < hosts.Length; index++)
                {
                    VehicleDataRuntimeHost host = hosts[index];
                    if (host == null || host.IntegrationConfiguration == null)
                        continue;
                    switch (host.IntegrationConfiguration.VehicleType)
                    {
                        case VehicleType.Auv: auvHost = host; break;
                        case VehicleType.Rov: rovHost = host; break;
                        case VehicleType.Usv: usvHost = host; break;
                    }
                }
            }

            if (auvDriver == null || rovDriver == null || usvDriver == null ||
                auvAuthority == null || rovAuthority == null || usvAuthority == null)
            {
                VehiclePoseDriver[] drivers =
                    FindObjectsByType<VehiclePoseDriver>(
                        FindObjectsSortMode.None);
                for (int index = 0; index < drivers.Length; index++)
                {
                    VehiclePoseDriver driver = drivers[index];
                    if (driver == null || driver.IntegrationConfiguration == null)
                        continue;
                    switch (driver.IntegrationConfiguration.VehicleType)
                    {
                        case VehicleType.Auv:
                            auvDriver = driver;
                            auvAuthority = driver.ControlAuthority;
                            break;
                        case VehicleType.Rov:
                            rovDriver = driver;
                            rovAuthority = driver.ControlAuthority;
                            break;
                        case VehicleType.Usv:
                            usvDriver = driver;
                            usvAuthority = driver.ControlAuthority;
                            break;
                    }
                }
            }
        }
    }
}
