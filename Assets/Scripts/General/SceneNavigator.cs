using UnityEngine;
using UnityEngine.SceneManagement;

// Project-wide scene switcher. There is no hand-wiring to do: a single instance
// bootstraps itself after the first scene loads and survives scene changes
// (DontDestroyOnLoad), so it is present in every scene automatically.
//
//   - In the MainMenu scene it draws the full menu (centred buttons).
//   - In any game scene it draws a small "Menu" button in the top-left corner.
//   - Keyboard: Esc -> MainMenu, F1 -> Player vs AI, F2 -> Arena,
//     F3 -> Distributed Arena. (Legacy Input API; safe because the project's
//     Active Input Handling is set to "Both".)
//
// The public Load* methods are static so they can also be wired to uGUI buttons
// from the inspector later (point a Button's OnClick at SceneNavigator.LoadX),
// but that wiring is optional — the IMGUI overlay already works on its own.
//
// Run "ChessMate ▸ Setup Scene Navigation" once in the editor to create the
// MainMenu scene and register all scenes in Build Settings (see Editor/NavigationSetup.cs).
public class SceneNavigator : MonoBehaviour
{
    // Scene names == the .unity file names (without extension). Must match the
    // files under Assets/Scenes and the Build Settings entries.
    public const string MainMenu         = "MainMenu";
    public const string PlayerVsAI       = "Player vs AI";
    public const string Arena            = "Arena";
    public const string DistributedArena = "Distributed_Arena";

    private static SceneNavigator _instance;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void
    Bootstrap()
    {
        if (_instance != null)
            return;

        var go = new GameObject("SceneNavigator");
        go.AddComponent<SceneNavigator>();   // Awake assigns _instance + DontDestroyOnLoad
    }


    private void
    Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }


    // ---- Public navigation API (also wireable to uGUI buttons) ----------------

    public static void LoadMainMenu()         => Go(MainMenu);
    public static void LoadPlayerVsAI()       => Go(PlayerVsAI);
    public static void LoadArena()            => Go(Arena);
    public static void LoadDistributedArena() => Go(DistributedArena);
    public static void QuitGame()             => Application.Quit();


    private static void
    Go(string sceneName)
    {
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"[SceneNavigator] Scene '{sceneName}' is not in Build Settings. "
              + "Run \"ChessMate ▸ Setup Scene Navigation\" to register all scenes.");
            return;
        }

        // Time can be left scaled-to-zero by a paused game scene; reset so the
        // next scene starts running.
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }


    // ---- Input + overlay ------------------------------------------------------

    private void
    Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) LoadMainMenu();
        if (Input.GetKeyDown(KeyCode.F1))     LoadPlayerVsAI();
        if (Input.GetKeyDown(KeyCode.F2))     LoadArena();
        if (Input.GetKeyDown(KeyCode.F3))     LoadDistributedArena();
    }


    private void
    OnGUI()
    {
        bool inMenu = SceneManager.GetActiveScene().name == MainMenu;

        if (inMenu)
            DrawMainMenu();
        else
            DrawCornerButton();
    }


    private static void
    DrawMainMenu()
    {
        const float w = 280f, h = 52f, gap = 14f;
        float totalH = (h * 4) + (gap * 3);
        float x = (Screen.width  - w) * 0.5f;
        float y = (Screen.height - totalH) * 0.5f;

        var title = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = 28,
            fontStyle = FontStyle.Bold,
        };
        GUI.Label(new Rect(x, y - 70f, w, 50f), "ChessMate", title);

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 18 };

        if (GUI.Button(new Rect(x, y + (h + gap) * 0, w, h), "Play vs AI", btn))
            LoadPlayerVsAI();
        if (GUI.Button(new Rect(x, y + (h + gap) * 1, w, h), "Arena", btn))
            LoadArena();
        if (GUI.Button(new Rect(x, y + (h + gap) * 2, w, h), "Distributed Arena", btn))
            LoadDistributedArena();
        if (GUI.Button(new Rect(x, y + (h + gap) * 3, w, h), "Quit", btn))
            QuitGame();
    }


    private static void
    DrawCornerButton()
    {
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        if (GUI.Button(new Rect(10f, 10f, 96f, 30f), "← Menu", btn))
            LoadMainMenu();
    }
}
