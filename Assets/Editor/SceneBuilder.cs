using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Собирает сцену Game.unity и настройки под Android (портрет). Вызов: unity command eval 'return SceneBuilder.Build();'
public static class SceneBuilder
{
    const string ScenePath = "Assets/Scenes/Game.unity";

    [MenuItem("Magic/Build Scene")]
    public static string Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 40;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.08f, 0.07f);
        camGo.transform.position = new Vector3(0, 0, -10);

        new GameObject("Game").AddComponent<Game>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.companyName = "neuraldeep";
        PlayerSettings.productName = "Magic Survivors";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "ru.neuraldeep.magicsurvivors");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        AssetDatabase.SaveAssets();
        return "ok: " + ScenePath;
    }
}
