using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-time editor utility that wires up scene navigation:
//   1. Creates Assets/Scenes/MainMenu.unity (a plain empty scene) if it is missing.
//   2. Registers all four scenes in Build Settings with MainMenu first (index 0)
//      so it is the scene the built game launches into.
//
// Unity authors the scene and Build Settings entries, so there is no fragile
// hand-edited YAML. The operation is idempotent — running it again just
// re-asserts the desired Build Settings order and leaves an existing MainMenu
// scene untouched.
//
// Run via the menu: ChessMate ▸ Setup Scene Navigation.
public static class NavigationSetup
{
    private const string ScenesDir    = "Assets/Scenes";
    private const string MainMenuPath = ScenesDir + "/MainMenu.unity";

    // Build order. MainMenu must be index 0 (the launch scene). The rest are the
    // existing game scenes.
    private static readonly string[] GameScenes =
    {
        ScenesDir + "/Player vs AI.unity",
        ScenesDir + "/Arena.unity",
        ScenesDir + "/Distributed_Arena.unity",
    };


    [MenuItem("ChessMate/Setup Scene Navigation")]
    public static void
    Setup()
    {
        EnsureMainMenuScene();
        RegisterBuildSettings();

        Debug.Log("[NavigationSetup] Done. MainMenu is now the launch scene and "
                + "all scenes are in Build Settings. Press Play to use the menu "
                + "(or Esc / F1 / F2 / F3 to switch scenes).");
    }


    private static void
    EnsureMainMenuScene()
    {
        if (File.Exists(MainMenuPath))
            return;

        if (!Directory.Exists(ScenesDir))
            Directory.CreateDirectory(ScenesDir);

        // A standard empty scene (Main Camera + Directional Light). The
        // SceneNavigator self-bootstraps at runtime and draws the menu via IMGUI,
        // so this scene needs no objects of its own.
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        EditorSceneManager.SaveScene(scene, MainMenuPath);
        AssetDatabase.Refresh();

        Debug.Log($"[NavigationSetup] Created {MainMenuPath}.");
    }


    private static void
    RegisterBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MainMenuPath, true),
        };

        foreach (string path in GameScenes)
        {
            if (File.Exists(path))
                scenes.Add(new EditorBuildSettingsScene(path, true));
            else
                Debug.LogWarning($"[NavigationSetup] Scene not found, skipping: {path}");
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
