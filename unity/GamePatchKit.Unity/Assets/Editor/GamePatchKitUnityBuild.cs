using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GamePatchKit.Unity.Editor
{
    public static class GamePatchKitUnityBuild
    {
        private const string BUILD_MARKER = "GAMEPATCHKIT_UNITY_MACOS_IL2CPP_BUILD_OK";
        private const string SCENE_PATH = "Assets/GamePatchKitUnityBuildSmoke.unity";

        public static void BuildMacOsIl2Cpp()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);

            try
            {
                var namedBuildTarget = NamedBuildTarget.Standalone;
                PlayerSettings.SetScriptingBackend(namedBuildTarget, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetApiCompatibilityLevel(namedBuildTarget, ApiCompatibilityLevel.NET_Standard);
                PlayerSettings.SetApplicationIdentifier(namedBuildTarget, "com.sidenation.gamepatchkit.unity.tests");

                string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                string outputPath = Path.Combine(
                    projectRoot,
                    "Build",
                    "macOS",
                    "GamePatchKitUnitySmoke.app");
                BuildReport report = BuildPipeline.BuildPlayer(
                    new[] { SCENE_PATH },
                    outputPath,
                    BuildTarget.StandaloneOSX,
                    BuildOptions.Development);

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Unity macOS IL2CPP build failed: {report.summary.result}");
                }

                Debug.Log($"{BUILD_MARKER}:{outputPath}");
            }
            finally
            {
                AssetDatabase.DeleteAsset(SCENE_PATH);
            }
        }
    }
}

