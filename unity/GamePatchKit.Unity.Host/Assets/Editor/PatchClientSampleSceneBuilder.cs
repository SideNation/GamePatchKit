using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 샘플 씬을 코드로 만든다. 메뉴 또는 batchmode의 -executeMethod PatchClientSampleSceneBuilder.Build로 실행한다.
public static class PatchClientSampleSceneBuilder
{
    private const string ScenePath = "Assets/PatchClientSample/PatchClientSample.unity";

    [MenuItem("GamePatchKit/Rebuild Sample Scene")]
    public static void Build()
    {
        // 새 씬을 열면 지금 열린 씬이 닫히므로, 대화형 실행에서는 저장 여부를 먼저 묻는다.
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var host = new GameObject("PatchClientSample");
        host.AddComponent<PatchClientSample>();

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            // batchmode에서 조용히 성공으로 끝나지 않도록 실패를 전파한다.
            throw new System.InvalidOperationException($"샘플 씬을 저장하지 못했습니다: {ScenePath}");
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
    }
}
