using System;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    internal static class EnvE2DConfiguration
    {
        internal const string FormalSceneAssetPath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        internal const string MainCameraName = "Main Camera";
        internal const string AuvRootName = "AUV_Yellow_Underwater";
        internal const string RovRootName = "ROV_Box_Seabed";
        internal const string UsvRootName = "USV_Blue_Surface";
        internal const string AuvLabelName = "AUV_Yellow_Label";
        internal const string RovLabelName = "ROV_Box_Label";
        internal const string UsvLabelName = "USV_Blue_Label";
        internal const string StatusPanelName = "DataPanelText";
        internal const string ExpectedTerrainSha256 =
            "8900397b6697c27f2b9be17599f3e2c76fc604b4b5f975b548b00f33401c934b";
        internal const string ExpectedDistantMeshSha256 =
            "af2b351819619508a9bb8353ca4da01ccd1185638b697f3ba5eaa96938db126b";
        internal const string ExpectedInputBaselineSha256 =
            "06fda2886a08238c88cbb81f69fc8f392d379f38b367d5a6daef75c6f9ebfd6f";
        internal const long ExpectedInputBaselineSize = 802509;
        internal static readonly Vector3 ApprovedStatusPanelPosition =
            new Vector3(-2.85f, 1.90f, -3.24f);

        internal static readonly string[] LabelNames =
        {
            AuvLabelName,
            RovLabelName,
            UsvLabelName
        };

        internal static readonly string[] ExpectedLabelText =
        {
            "AUV",
            "ROV",
            "USV"
        };

        internal static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
