using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

// Main-thread owner of a distributed-arena run. The new scene has no
// MatchManager, so this does MatchManager.Awake's job itself (TT.Init + build
// the OpeningBook), then on StartRun() it builds the pair queue, spins up the
// WorkerPool, and drains the results queue every Update() — feeding the
// scoresheet, writing files, and exposing a snapshot the dashboard renders.
//
// All Unity / file-system / scoresheet work happens here on the main thread;
// the worker threads only ever touch the two ConcurrentQueues and the read-only
// OpeningBook. That keeps the cross-thread surface tiny and lock-free.
public class DistributedArenaController : MonoBehaviour
{
    // ---- configuration, set by the dashboard before StartRun() ---------------
    [HideInInspector] public string[] EngineNames = new string[2];
    [HideInInspector] public string   OpeningsFilePath;       // "" = book-less (all from startpos)
    [HideInInspector] public int      GamesToPlay      = 100;
    [HideInInspector] public int      WorkerCount      = 6;
    [HideInInspector] public float    TimePerSide      = 1.0f;
    [HideInInspector] public float    Increment        = 0.1f;
    [HideInInspector] public bool     FixedTimePerMove = false;
    [HideInInspector] public bool     AllowOpeningBook = false; // arena games don't use the book by default

    // ---- live snapshot the dashboard polls -----------------------------------
    public bool Running   { get; private set; }
    public bool Completed { get; private set; }

    public int    TotalGames     { get; private set; }
    public int    GamesCompleted { get; private set; }
    public double ElapsedSeconds => _sw.Elapsed.TotalSeconds;
    public double EtaSeconds      { get; private set; }
    public int    Crashes        => _pool?.Crashes ?? 0;
    public int    TimeLosses     => _scores?.TimeLosses ?? 0;
    public IReadOnlyList<WorkerStatus> WorkerStatuses => _pool?.Statuses;

    public EloStats.Result Elo => _scores != null ? _scores.Elo() : default;
    public int Engine0Wins => _scores?.EngineWins(0) ?? 0;
    public int Engine1Wins => _scores?.EngineWins(1) ?? 0;
    public int Draws       => _scores?.Draws ?? 0;
    public string SummaryText { get; private set; }

    // ---- internals -----------------------------------------------------------
    private OpeningBook _ob;
    private string      _streamingAssetsPath;
    private string      _outputDir;

    private ConcurrentQueue<PairSpec>   _pairQueue;
    private ConcurrentQueue<GameResult> _resultQueue;
    private WorkerPool          _pool;
    private DistributedScoreSheet _scores;
    private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();


    private void
    Awake()
    {
        // Cap the render loop. Uncapped, this throughput-only scene spins ~1.5
        // cores just redrawing static HUD text — cores stolen from the bot.exe
        // workers, which biases the worker-count sweep (worse at high N).
        Application.targetFrameRate = 60;

        _streamingAssetsPath = Application.streamingAssetsPath;
        _outputDir = _streamingAssetsPath + "/distributed_arena";

        // Zobrist/TT tables must be seeded before the book hashes positions
        // (same ordering as MatchManager.Awake).
        TT.Init();
        _ob = new OpeningBook(_streamingAssetsPath);
        if (!_ob.GetOpeningBook())
            Debug.LogWarning("Opening book not found — engines will run without it.");
    }


    // Called by the dashboard's Start button once the config fields are filled.
    public void
    StartRun()
    {
        if (Running) return;

        List<string> openings = LoadOpenings(OpeningsFilePath);

        int pairs = (GamesToPlay + 1) / 2;     // round up; a pair is 2 games
        TotalGames = pairs * 2;

        _pairQueue   = new ConcurrentQueue<PairSpec>();
        _resultQueue = new ConcurrentQueue<GameResult>();

        for (int i = 0; i < pairs; i++)
        {
            string line = (openings != null && openings.Count > 0)
                          ? openings[i % openings.Count]
                          : "";
            _pairQueue.Enqueue(new PairSpec(i, line));
        }

        int n = Mathf.Clamp(WorkerCount, 1, 64);

        _scores = new DistributedScoreSheet(EngineNames[0], EngineNames[1],
            TimePerSide, Increment, FixedTimePerMove, n, _outputDir);

        _pool = new WorkerPool(n, EngineNames, _ob, _streamingAssetsPath,
            TimePerSide, Increment, FixedTimePerMove, AllowOpeningBook,
            searchLogDir: null,                // no per-game search logs in v1 (disk blowup)
            _pairQueue, _resultQueue);

        GamesCompleted = 0;
        EtaSeconds     = 0;
        Completed      = false;
        SummaryText    = null;

        _sw.Restart();
        _pool.Start();
        Running = true;
    }


    private void
    Update()
    {
        if (!Running) return;

        bool drainedAny = false;
        while (_resultQueue.TryDequeue(out GameResult r))
        {
            _scores.Add(r);
            GamesCompleted++;
            drainedAny = true;
        }

        if (drainedAny)
        {
            _scores.WriteSummary(_sw.Elapsed.TotalSeconds);
            if (GamesCompleted > 0)
            {
                double avg = _sw.Elapsed.TotalSeconds / GamesCompleted;
                EtaSeconds = avg * (TotalGames - GamesCompleted);
            }
        }

        // Done when every worker has exited AND we've drained the last result.
        if (_pool.Finished && _resultQueue.IsEmpty)
            Finish();
    }


    private void
    Finish()
    {
        _sw.Stop();
        Running   = false;
        Completed = true;
        EtaSeconds = 0;

        // Final rewrite with the stopped clock so results.txt's elapsed time
        // matches the HUD's "Total time" (the last drain wrote it mid-run).
        _scores.WriteSummary(_sw.Elapsed.TotalSeconds);

        var elo = _scores.Elo();
        float flagRate = GamesCompleted > 0 ? 100f * TimeLosses / GamesCompleted : 0f;
        SummaryText =
            "<color=#7E8AA0>──────────────────────</color>\n"
          + "<b>Tournament complete</b>\n"
          + $"{EngineNames[0]}  <b>{Engine0Wins}</b> — <b>{Engine1Wins}</b>  {EngineNames[1]}"
          + $"    {Draws} draw(s)   ({GamesCompleted} games)\n"
          + $"Elo (engine 1): <b>{elo.Elo:+0;-0;0}</b>  [{elo.EloLow:+0;-0;0}, {elo.EloHigh:+0;-0;0}]\n"
          + $"Total time: {TimeFormat.Verbose(_sw.Elapsed.TotalSeconds)}    Crashes: {Crashes}"
          + $"    Flagged: {TimeLosses} ({flagRate:0}%)";

        _pool.Shutdown();
    }


    private List<string>
    LoadOpenings(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        try
        {
            _ob.GetOpeningLines(filePath);
            return new List<string>(_ob.openings);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Openings '{filePath}' not loaded ({e.Message}); "
                             + "playing all pairs from the start position.");
            return null;
        }
    }


    private void OnDisable()         { _pool?.Shutdown(); }
    private void OnApplicationQuit() { _pool?.Shutdown(); }
}
