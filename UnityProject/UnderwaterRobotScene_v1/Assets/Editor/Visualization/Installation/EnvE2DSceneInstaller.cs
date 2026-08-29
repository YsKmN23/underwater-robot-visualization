using System;
using System.Linq;
using UnderwaterRobotScene.Visualization.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal sealed class EnvE2DInstallResult
    {
        internal bool Changed;
        internal bool ControllerAdded;
        internal int LabelsChanged;
        internal bool StatusPanelMoved;
        internal string MainCameraPath;
    }

    internal static class EnvE2DSceneInstaller
    {
        internal static EnvE2DInstallResult Apply(Scene scene)
        {
            EnvE2DInstallResult preVehicle = ApplyPreVehicle(scene);
            EnvE2DInstallResult postVehicle = ApplyPostVehicleLayout(scene);
            return new EnvE2DInstallResult
            {
                Changed = preVehicle.Changed || postVehicle.Changed,
                ControllerAdded = preVehicle.ControllerAdded,
                LabelsChanged = preVehicle.LabelsChanged,
                StatusPanelMoved = postVehicle.StatusPanelMoved,
                MainCameraPath = preVehicle.MainCameraPath
            };
        }

        internal static EnvE2DInstallResult ApplyPreVehicle(Scene scene)
        {
            EnvE2DConfiguration.Require(
                scene.IsValid() && scene.isLoaded,
                "A valid loaded Scene is required.");

            Camera[] activeMainCameras = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Camera>(true))
                .Where(camera =>
                    camera.enabled &&
                    camera.gameObject.activeInHierarchy &&
                    camera.CompareTag("MainCamera"))
                .ToArray();
            EnvE2DConfiguration.Require(
                activeMainCameras.Length == 1,
                "Expected exactly one active MainCamera.");
            Camera mainCamera = activeMainCameras[0];
            EnvE2DConfiguration.Require(
                mainCamera.gameObject.name ==
                EnvE2DConfiguration.MainCameraName,
                "Unexpected MainCamera object.");

            VehicleSelectionCameraController[] controllers =
                scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        VehicleSelectionCameraController>(true))
                    .ToArray();
            EnvE2DConfiguration.Require(
                controllers.Length <= 1,
                "More than one camera authority already exists.");
            EnvE2DConfiguration.Require(
                controllers.Length == 0 ||
                controllers[0].gameObject == mainCamera.gameObject,
                "Existing camera authority is not on Main Camera.");

            bool changed = false;
            bool controllerAdded = false;
            VehicleSelectionCameraController controller =
                mainCamera.GetComponent<
                    VehicleSelectionCameraController>();
            if (controller == null)
            {
                controller = mainCamera.gameObject.AddComponent<
                    VehicleSelectionCameraController>();
                controllerAdded = true;
                changed = true;
                EditorUtility.SetDirty(mainCamera.gameObject);
            }
            EnvE2DConfiguration.Require(
                controller != null && controller.enabled,
                "Authorized camera controller is not enabled.");

            int labelsChanged = 0;
            for (int i = 0;
                 i < EnvE2DConfiguration.LabelNames.Length;
                 i++)
            {
                CameraFacingText label = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            CameraFacingText>(true))
                    .Single(item =>
                        item.gameObject.name ==
                        EnvE2DConfiguration.LabelNames[i]);
                TextMesh text = label.GetComponent<TextMesh>();
                EnvE2DConfiguration.Require(
                    text != null &&
                    text.text ==
                    EnvE2DConfiguration.ExpectedLabelText[i],
                    "Vehicle label content changed: " +
                    EnvE2DConfiguration.LabelNames[i]);
                Vector3 scale = label.transform.localScale;
                EnvE2DConfiguration.Require(
                    scale.x > 0f && scale.y > 0f && scale.z > 0f,
                    "Vehicle label uses a negative scale: " +
                    label.gameObject.name);
                if (label.Mode !=
                    CameraFacingText.BillboardMode.ScreenParallel)
                {
                    label.SetBillboardMode(
                        CameraFacingText.BillboardMode.ScreenParallel);
                    EditorUtility.SetDirty(label);
                    labelsChanged++;
                    changed = true;
                }
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return new EnvE2DInstallResult
            {
                Changed = changed,
                ControllerAdded = controllerAdded,
                LabelsChanged = labelsChanged,
                StatusPanelMoved = false,
                MainCameraPath = mainCamera.gameObject.name
            };
        }

        internal static EnvE2DInstallResult ApplyPostVehicleLayout(Scene scene)
        {
            EnvE2DConfiguration.Require(
                scene.IsValid() && scene.isLoaded,
                "A valid loaded Scene is required.");

            GameObject[] panelRoots = scene.GetRootGameObjects()
                .Where(root => string.Equals(
                    root.name,
                    EnvE2DConfiguration.StatusPanelName,
                    StringComparison.Ordinal))
                .ToArray();
            EnvE2DConfiguration.Require(panelRoots.Length == 1,
                "Expected exactly one DataPanelText Scene root.");
            GameObject panel = panelRoots[0];
            TextMesh[] texts = panel.GetComponents<TextMesh>();
            CameraFacingText[] facings = panel.GetComponents<CameraFacingText>();
            VehicleStatusPanelPresenter[] presenters =
                panel.GetComponents<VehicleStatusPanelPresenter>();
            EnvE2DConfiguration.Require(
                texts.Length == 1 && facings.Length == 1 &&
                presenters.Length == 1,
                "Vehicle Status panel component binding is incomplete.");

            VehicleStatusPanelPresenter presenter = presenters[0];
            EnvE2DConfiguration.Require(
                ReferenceEquals(presenter.TargetText, texts[0]) &&
                presenter.AuvHost != null && presenter.AuvDriver != null &&
                presenter.AuvAuthority != null &&
                presenter.RovHost != null && presenter.RovDriver != null &&
                presenter.RovAuthority != null &&
                presenter.UsvHost != null && presenter.UsvDriver != null &&
                presenter.UsvAuthority != null &&
                !ReferenceEquals(presenter.AuvHost, presenter.RovHost) &&
                !ReferenceEquals(presenter.AuvHost, presenter.UsvHost) &&
                !ReferenceEquals(presenter.RovHost, presenter.UsvHost) &&
                !ReferenceEquals(presenter.AuvDriver, presenter.RovDriver) &&
                !ReferenceEquals(presenter.AuvDriver, presenter.UsvDriver) &&
                !ReferenceEquals(presenter.RovDriver, presenter.UsvDriver) &&
                !ReferenceEquals(
                    presenter.AuvAuthority, presenter.RovAuthority) &&
                !ReferenceEquals(
                    presenter.AuvAuthority, presenter.UsvAuthority) &&
                !ReferenceEquals(
                    presenter.RovAuthority, presenter.UsvAuthority),
                "Vehicle Status panel Presenter binding is incomplete or crossed.");

            Transform statusPanel = panel.transform;
            bool statusPanelMoved = false;
            if (!Approximately(
                    statusPanel.localPosition,
                    EnvE2DConfiguration.ApprovedStatusPanelPosition))
            {
                statusPanel.localPosition =
                    EnvE2DConfiguration.ApprovedStatusPanelPosition;
                EditorUtility.SetDirty(statusPanel);
                EditorSceneManager.MarkSceneDirty(scene);
                statusPanelMoved = true;
            }

            return new EnvE2DInstallResult
            {
                Changed = statusPanelMoved,
                ControllerAdded = false,
                LabelsChanged = 0,
                StatusPanelMoved = statusPanelMoved,
                MainCameraPath = string.Empty
            };
        }

        private static bool Approximately(Vector3 actual, Vector3 expected)
        {
            return Mathf.Abs(actual.x - expected.x) <= 0.000001f &&
                   Mathf.Abs(actual.y - expected.y) <= 0.000001f &&
                   Mathf.Abs(actual.z - expected.z) <= 0.000001f;
        }
    }
}
