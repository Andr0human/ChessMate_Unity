using System.IO;

// Order-independent tournament tally for the distributed arena. Unlike
// ArenaScoreSheet — which attributes each game to an engine by `results.Count %
// 2`, i.e. by dispatch order — this reads attribution straight off each
// GameResult.WhiteEngineIndex, so it stays correct when games finish out of
// order across N workers.
//
// Single-threaded by contract: the controller drains the result queue and calls
// Add()/WriteSummary() only on the Unity main thread, so there is no locking
// here. Unity-free (System.IO + an injected output dir), so it stays testable.
//
// Engine indices are 0-based throughout (engine 0 / engine 1), matching
// PairSpec / GameResult; the on-disk report keeps the existing "Engine 1 vs
// Engine 2" wording for continuity with the sequential arena's files.
public class DistributedScoreSheet
{
    private readonly string[] _names;        // [0], [1]
    private readonly float    _timePerGame;
    private readonly float    _increment;
    private readonly bool     _fixedTimePerMove;
    private readonly int      _workerCount;

    private readonly string _outputDir;      // e.g. <streaming>/distributed_arena
    private readonly string _gamesDir;       // _outputDir/Games
    private readonly string _csvPath;        // _outputDir/results_log.csv
    private readonly string _summaryPath;    // _outputDir/results.txt

    // Engine-relative tallies.
    private int _wins0, _wins1, _draws;
    private int _lossOnTime0, _lossOnTime1;
    private int _predictionAttempt, _predictionSuccess;

    // Engine-0 POV colour breakdown (engine 1's mirror is derivable).
    private int _w0_wins, _w0_draws, _w0_losses;   // engine 0 as White
    private int _b0_wins, _b0_draws, _b0_losses;   // engine 0 as Black

    public int      GamesPlayed { get; private set; }
    public string[] EngineNames => _names;
    public int EngineWins(int idx) => idx == 0 ? _wins0 : _wins1;
    public int Draws => _draws;

    // Games decided by a flag (either engine). A high share signals the run is
    // CPU-starved (too many workers for the cores) rather than a real contest.
    public int TimeLosses => _lossOnTime0 + _lossOnTime1;

    // Engine-0-relative Elo + 95% interval, for the live dashboard.
    public EloStats.Result Elo() => EloStats.FromCounts(_wins0, _draws, _wins1);


    public
    DistributedScoreSheet(string name0, string name1,
        float timePerGame, float increment, bool fixedTimePerMove,
        int workerCount, string outputDir)
    {
        _names            = new[] { name0, name1 };
        _timePerGame      = timePerGame;
        _increment        = increment;
        _fixedTimePerMove = fixedTimePerMove;
        _workerCount      = workerCount;

        _outputDir   = outputDir;
        _gamesDir    = Path.Combine(outputDir, "Games");
        _csvPath     = Path.Combine(outputDir, "results_log.csv");
        _summaryPath = Path.Combine(outputDir, "results.txt");

        Directory.CreateDirectory(_gamesDir);
        File.WriteAllText(_csvPath,
            "GameNo, Worker, Result, WhitePlayer, BlackPlayer, MoveCount, Remarks\n");
    }


    // Tally one finished game and write its PGN + a CSV row. Caller refreshes
    // the summary via WriteSummary() (kept separate so it can be throttled).
    public void
    Add(GameResult r)
    {
        GamesPlayed++;

        int we      = r.WhiteEngineIndex;   // engine that was White
        bool e0White = (we == 0);
        int e0Result = e0White ? r.Result : -r.Result;   // engine-0-relative

        // Engine win/draw tally
        if (r.Result == 0)
        {
            _draws++;
        }
        else
        {
            bool whiteWon     = (r.Result == 1);
            int  winnerEngine = whiteWon ? we : (we ^ 1);
            if (winnerEngine == 0) _wins0++; else _wins1++;
        }

        // Engine-0 colour breakdown
        if (e0White)
        {
            if      (e0Result > 0) _w0_wins++;
            else if (e0Result == 0) _w0_draws++;
            else                    _w0_losses++;
        }
        else
        {
            if      (e0Result > 0) _b0_wins++;
            else if (e0Result == 0) _b0_draws++;
            else                    _b0_losses++;
        }

        // Prediction accuracy (white-relative, same convention as Result)
        if (r.Prediction != 0)
        {
            _predictionAttempt++;
            if (r.Prediction == r.Result) _predictionSuccess++;
        }

        // Flag losses, attributed to the engine whose clock expired
        if (r.State == GameEndState.WhiteWinsOnTime)        // Black flagged
            BumpLossOnTime(we ^ 1);
        else if (r.State == GameEndState.BlackWinsOnTime)   // White flagged
            BumpLossOnTime(we);

        WritePgn(r);
        AppendCsv(r);
    }


    private void
    BumpLossOnTime(int engineIdx)
    {
        if (engineIdx == 0) _lossOnTime0++; else _lossOnTime1++;
    }


    private void
    WritePgn(GameResult r)
    {
        string white = _names[r.WhiteEngineIndex];
        string black = _names[r.BlackEngineIndex];

        string resultStr = r.Result == 1 ? "1-0" : (r.Result == -1 ? "0-1" : "1/2-1/2");

        System.DateTime now = System.DateTime.Now;
        string date = now.Year + "." + now.Month + "." + now.Day;

        string header =
              "[Event \""  + _names[0] + " vs " + _names[1] + " Unit Testing\"]\n"
            + "[Site \"?\"]\n"
            + "[Date \""   + date + "\"]\n"
            + "[Round \""  + r.GameNumber + "\"]\n"
            + "[White \""  + white + "\"]\n"
            + "[Black \""  + black + "\"]\n"
            + "[Result \"" + resultStr + "\"]\n"
            + "[FEN \""    + r.StartFen + "\"]\n\n";

        File.WriteAllText(Path.Combine(_gamesDir, "game" + r.GameNumber + ".pgn"),
                          header + r.MoveListBody);
    }


    private void
    AppendCsv(GameResult r)
    {
        string resultStr = r.Result == 1 ? "1-0" : (r.Result == -1 ? "0-1" : "1/2-1/2");
        string white = _names[r.WhiteEngineIndex];
        string black = _names[r.BlackEngineIndex];
        int    moves = (r.Plies + 1) / 2;

        string line = r.GameNumber + ", " + r.WorkerId + ", " + resultStr + ", "
                    + white + ", " + black + ", " + moves + ", " + r.Remark + "\n";
        File.AppendAllText(_csvPath, line);
    }


    // Rewrites the human-readable results.txt. Cheap; the caller may still
    // throttle it (e.g. once per drained batch) rather than once per game.
    // elapsedSeconds is the controller's run stopwatch, so the on-disk file
    // matches the HUD's "Total time".
    public void
    WriteSummary(double elapsedSeconds)
    {
        int w0T = _w0_wins   + _b0_wins;
        int d0T = _w0_draws  + _b0_draws;
        int l0T = _w0_losses + _b0_losses;

        string timeControl = _fixedTimePerMove
            ? $"Fixed {_timePerGame:0.##}s per move"
            : $"{_timePerGame:0.##}s + {_increment:0.##}s increment";

        int nameWidth = System.Math.Max(_names[0].Length, _names[1].Length);
        string sep = new string('=', 50);

        File.WriteAllText(_summaryPath,
            sep + "\n"
          + "            DISTRIBUTED ARENA RESULTS\n"
          + sep + "\n"
          + _names[0] + "  vs  " + _names[1] + "\n\n"
          + "Time Control : " + timeControl + "\n"
          + "Games Played : " + GamesPlayed + "\n"
          + "Workers      : " + _workerCount + "\n"
          + "Elapsed Time : " + TimeFormat.Verbose(elapsedSeconds) + "\n\n"
          + "             Wins   Draws   Losses   (engine 1, " + _names[0] + ")\n"
          + string.Format("   White : {0,4}    {1,4}    {2,4}\n",  _w0_wins, _w0_draws, _w0_losses)
          + string.Format("   Black : {0,4}    {1,4}    {2,4}\n",  _b0_wins, _b0_draws, _b0_losses)
          + string.Format("   Total : {0,4}    {1,4}    {2,4}\n\n", w0T, d0T, l0T)
          + "Prediction Accuracy : " + _predictionSuccess + "/" + _predictionAttempt + "\n\n"
          + "Losses on Time\n"
          + "   " + _names[0].PadRight(nameWidth) + " : " + _lossOnTime0 + "\n"
          + "   " + _names[1].PadRight(nameWidth) + " : " + _lossOnTime1 + "\n"
          + sep + "\n");
    }
}
