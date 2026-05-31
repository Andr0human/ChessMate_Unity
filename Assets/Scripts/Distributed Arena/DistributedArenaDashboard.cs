using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Setup screen + live HUD for the Distributed Arena scene. The setup half
// mirrors ArenaDashboard (engine .exe + opening-file discovery into dropdowns,
// games count, time-control parse) and adds a worker-count (N) field. The live
// half polls the controller's snapshot each frame and renders it into TMP text
// fields — no per-frame allocation beyond the worker-state string.
//
// Kept deliberately text-only (no eval bar / board / move list) — this scene's
// job is throughput + score, not watching a single game.
public class DistributedArenaDashboard : MonoBehaviour
{
    [SerializeField] private DistributedArenaController controller;

    [Header("Panels")]
    [SerializeField] private GameObject SetupRoot;
    [SerializeField] private GameObject LiveRoot;

    [Header("Setup inputs")]
    public TMP_Dropdown[]   EngineDropdowns;     // [0], [1]
    public TMP_Dropdown     OpeningDropdown;
    public TMP_InputField   GamesCountField;
    public TMP_InputField   TimeControlField;    // "1 0.1" => 1s + 0.1s
    public TMP_InputField   WorkerCountField;    // N
    [SerializeField] private Toggle CalibrationToggle;   // reserved for D6

    [Header("Live fields")]
    public TextMeshProUGUI ProgressField;
    public TextMeshProUGUI ScoreField;
    public TextMeshProUGUI EloField;
    public TextMeshProUGUI EtaField;
    public TextMeshProUGUI CrashField;
    public TextMeshProUGUI WorkersField;
    public TextMeshProUGUI SummaryField;

    private string[] _engineNames;
    private string[] _openingFiles;
    private readonly StringBuilder _sb = new StringBuilder(256);

    // Rich-text palette (TMP). Labels are dimmed; only meaningful signals get
    // colour so the board reads like a scoreboard, not a rainbow.
    private const string Dim   = "#7E8AA0";   // labels / chrome
    private const string Pos   = "#5BD17E";   // positive Elo, worker done
    private const string Neg   = "#E0655F";   // negative Elo, crashes
    private const string Amber = "#E0B33A";   // worker playing


    private void
    Start()
    {
        string sa = Application.streamingAssetsPath;

        string[] exes = Directory.GetFiles(sa, "*.exe");
        _engineNames = new string[exes.Length];
        for (int i = 0; i < exes.Length; i++)
            _engineNames[i] = Path.GetFileNameWithoutExtension(exes[i]);

        string[] openings = Directory.GetFiles(sa + "/Utility/", "*.opening");
        _openingFiles = new string[openings.Length];
        for (int i = 0; i < openings.Length; i++)
            _openingFiles[i] = Path.GetFileNameWithoutExtension(openings[i]);

        PopulateDropdown(EngineDropdowns[0], _engineNames);
        PopulateDropdown(EngineDropdowns[1], _engineNames);
        PopulateDropdown(OpeningDropdown, _openingFiles);

        if (LiveRoot != null) LiveRoot.SetActive(false);
        if (SummaryField != null) SummaryField.text = "";
    }


    public void
    PopulateDropdown(TMP_Dropdown dropdown, string[] names)
    {
        var options = new List<TMP_Dropdown.OptionData>();
        foreach (string name in names)
            options.Add(new TMP_Dropdown.OptionData(name));
        dropdown.options = options;
    }


    // Wired to the Start button.
    public void
    StartButton()
    {
        controller.EngineNames = new[]
        {
            _engineNames[EngineDropdowns[0].value],
            _engineNames[EngineDropdowns[1].value],
        };
        controller.OpeningsFilePath = (_openingFiles.Length > 0)
            ? _openingFiles[OpeningDropdown.value] : "";

        controller.GamesToPlay = ParseInt(GamesCountField, controller.GamesToPlay);
        controller.WorkerCount = ParseInt(WorkerCountField, controller.WorkerCount);
        ParseTimeControl();

        if (CalibrationToggle != null && CalibrationToggle.isOn)
            Debug.LogWarning("Calibration mode is a follow-on (D6) and not yet "
                           + "implemented — running a normal tournament.");

        if (SetupRoot != null) SetupRoot.SetActive(false);
        if (LiveRoot  != null) LiveRoot.SetActive(true);

        controller.StartRun();
    }


    private void
    Update()
    {
        if (controller == null || (!controller.Running && !controller.Completed))
            return;

        if (ProgressField != null)
        {
            int done = controller.GamesCompleted, total = controller.TotalGames;
            float frac = total > 0 ? (float)done / total : 0f;
            ProgressField.text =
                $"<color={Dim}>Games</color>  <b>{done}</b> <color={Dim}>/ {total}</color>"
              + $"    {Bar(frac)} <color={Dim}>{frac * 100f:0}%</color>";
        }

        if (ScoreField != null)
            ScoreField.text =
                $"{controller.EngineNames[0]}   <b>{controller.Engine0Wins}</b> "
              + $"<color={Dim}>—</color> <b>{controller.Engine1Wins}</b>   {controller.EngineNames[1]}"
              + $"   <color={Dim}>({controller.Draws} draws)</color>";

        if (EloField != null)
        {
            var e = controller.Elo;
            string col = e.Elo > 0 ? Pos : e.Elo < 0 ? Neg : Dim;
            EloField.text =
                $"<color={Dim}>Elo (engine 1)</color>  <b><color={col}>{e.Elo:+0;-0;0}</color></b>"
              + $"  <color={Dim}>[{e.EloLow:+0;-0;0}, {e.EloHigh:+0;-0;0}]</color>";
        }

        if (EtaField != null)
            EtaField.text =
                $"<color={Dim}>Elapsed</color>  {TimeFormat.Verbose(controller.ElapsedSeconds)}"
              + (controller.Running
                    ? $"   <color={Dim}>·   ETA</color>  {TimeFormat.Verbose(controller.EtaSeconds)}"
                    : "");

        if (CrashField != null)
        {
            int c = controller.Crashes, tl = controller.TimeLosses;
            int done = controller.GamesCompleted;
            float rate = done > 0 ? (float)tl / done : 0f;

            string crash = c > 0
                ? $"<b><color={Neg}>Crashes: {c}</color></b>"
                : $"<color={Dim}>Crashes: 0</color>";

            // Flag rate is the headline health signal: a CPU-starved run shows a
            // high share of games decided on time. Green <2%, amber <10%, red above.
            string flagCol = rate > 0.10f ? Neg : rate > 0.02f ? Amber : Pos;
            string flagged = $"<color={flagCol}>Flagged: {tl} ({rate * 100f:0}%)</color>";

            CrashField.text = $"{crash}    {flagged}";
        }

        if (WorkersField != null)
            WorkersField.text = BuildWorkerLines();

        if (controller.Completed && SummaryField != null)
            SummaryField.text = controller.SummaryText;
    }


    // Per-worker grid. Columns are space-padded and rely on WorkersField using a
    // monospace font (set in the editor) so they line up: marker + state + count.
    private string
    BuildWorkerLines()
    {
        var statuses = controller.WorkerStatuses;
        if (statuses == null) return "";

        _sb.Clear();
        for (int i = 0; i < statuses.Count; i++)
        {
            WorkerStatus s = statuses[i];
            string state, col;
            switch (s.State)
            {
                case WorkerState.Playing:
                    col = Amber;
                    state = $"pair {s.CurrentPairId} g{s.CurrentGameInPair}";
                    break;
                case WorkerState.Stopped: col = Pos; state = "done"; break;
                default:                  col = Dim; state = "idle"; break;
            }
            // "·" (middle dot) is a status LED coloured by state; kept to
            // Latin-1 so it renders in any font (▶/✓ are missing from the default).
            _sb.AppendFormat(
                "<color={0}>W{1,-2}</color> <color={2}>· {3,-13}</color><color={0}>{4,3} played</color>\n",
                Dim, i, col, state, s.GamesCompleted);
        }
        return _sb.ToString();
    }


    // Unicode meter for the games-complete fraction (filled blocks + dim track).
    private static string
    Bar(float frac)
    {
        const int width = 14;
        int filled = Mathf.Clamp(Mathf.RoundToInt(frac * width), 0, width);
        return new string('█', filled)
             + $"<color=#3A4150>{new string('░', width - filled)}</color>";
    }


    private int
    ParseInt(TMP_InputField field, int fallback)
    {
        if (field == null) return fallback;
        string text = RemoveNonAlphaNumeric(field.text);
        return int.TryParse(text, out int v) && v > 0 ? v : fallback;
    }


    private void
    ParseTimeControl()
    {
        if (TimeControlField == null) return;
        string[] values = TimeControlField.text.Split();
        if (values.Length >= 1 && float.TryParse(RemoveNonAlphaNumeric(values[0]), out float t))
            controller.TimePerSide = t;
        if (values.Length >= 2 && float.TryParse(RemoveNonAlphaNumeric(values[1]), out float inc))
            controller.Increment = inc;
    }


    public string
    RemoveNonAlphaNumeric(string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text)
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                sb.Append(ch);
        return sb.ToString();
    }


    public void
    ExitButton()
    {
        Application.Quit();
    }
}
