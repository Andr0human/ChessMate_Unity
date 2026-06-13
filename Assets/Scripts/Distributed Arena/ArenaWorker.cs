using System;
using System.Collections.Concurrent;
using System.Threading;

// Coarse, display-only state a worker publishes for the dashboard. Written by
// the worker thread, read by the main thread; the fields are volatile and the
// data is purely cosmetic, so the mild cross-thread raciness is acceptable
// (live ply is deliberately NOT tracked — reading the worker's MatchData from
// the main thread mid-game would be a real data race; deferred).
public enum WorkerState { Idle, Playing, Stopped }

public class WorkerStatus
{
    public volatile WorkerState State = WorkerState.Idle;
    public volatile int CurrentPairId     = -1;
    public volatile int CurrentGameInPair = -1;
    public volatile int GamesCompleted    = 0;
}


// One arena worker: pulls pairs off the shared queue, plays both colour
// orientations through a reusable HeadlessMatchManager (spawn-per-game engines,
// per the Step D decision — HeadlessMatchManager creates + tears down the two
// bot.exe inside each PlayGame), and posts a GameResult per game. Runs on its
// own background thread; the only shared state it touches is the two queues,
// the read-only OpeningBook, and its own WorkerStatus.
public class ArenaWorker
{
    private readonly int    _id;
    private readonly string[] _engineNames;          // [0], [1] — tournament engines
    private readonly string _searchLogDir;           // null = no per-game search logs

    private readonly ConcurrentQueue<PairSpec>   _pairs;
    private readonly ConcurrentQueue<GameResult> _results;
    private readonly CancellationToken _token;

    // Bumped (Interlocked) on an unexpected throw from a single game so the
    // dashboard can surface a crash count; the worker then moves on.
    private readonly Action _onCrash;

    // One reusable instance per worker; PlayGame() resets board + clock and
    // spawns/tears down its own engines each game. Built in the ctor (no threads
    // / processes yet — just config + a StopwatchClock) so the pool can reach it
    // via AbortEngines() even before Run() starts.
    private readonly HeadlessMatchManager _hmm;

    public WorkerStatus Status { get; } = new WorkerStatus();


    public
    ArenaWorker(int id, string[] engineNames, OpeningBook ob, string streamingAssetsPath,
        float timePerSide, float increment, bool fixedTimePerMove, bool allowOpeningBook,
        string searchLogDir,
        ConcurrentQueue<PairSpec> pairs, ConcurrentQueue<GameResult> results,
        CancellationToken token, Action onCrash)
    {
        _id           = id;
        _engineNames  = engineNames;
        _searchLogDir = searchLogDir;
        _pairs        = pairs;
        _results      = results;
        _token        = token;
        _onCrash      = onCrash;

        _hmm = new HeadlessMatchManager(ob, streamingAssetsPath,
                                        timePerSide, increment,
                                        fixedTimePerMove, allowOpeningBook);
    }


    // Thread entry point.
    public void
    Run()
    {
        while (!_token.IsCancellationRequested && _pairs.TryDequeue(out PairSpec pair))
        {
            Status.State         = WorkerState.Playing;
            Status.CurrentPairId = pair.PairId;

            // Game 0: engine 0 White / engine 1 Black. Game 1: swap.
            PlayOne(pair, gameInPair: 0, whiteEngineIndex: 0);
            if (_token.IsCancellationRequested) break;
            PlayOne(pair, gameInPair: 1, whiteEngineIndex: 1);
        }

        Status.State             = WorkerState.Stopped;
        Status.CurrentPairId     = -1;
        Status.CurrentGameInPair = -1;
    }


    // Cross-thread engine kill for the pool's shutdown path (see
    // HeadlessMatchManager.AbortEngines).
    public void
    AbortEngines() => _hmm.AbortEngines();


    private void
    PlayOne(PairSpec pair, int gameInPair, int whiteEngineIndex)
    {
        Status.CurrentGameInPair = gameInPair;

        string white = _engineNames[whiteEngineIndex];
        string black = _engineNames[whiteEngineIndex ^ 1];
        int gameNumber = pair.PairId * 2 + gameInPair;

        try
        {
            GameEndState state = _hmm.PlayGame(white, black, pair.OpeningLine,
                                               _searchLogDir, gameNumber);

            int result     = ResultFromState(state);
            int divergence = _hmm.Data.DivergentEvalCount(3f);
            int weight     = _hmm.BoardPosition.PositionWeight();
            int imbalance  = _hmm.BoardPosition.MaterialImbalance();
            string resultToken = result == 1 ? "1-0" : (result == -1 ? "0-1" : "1/2-1/2");

            _results.Enqueue(new GameResult
            {
                WhiteEngineIndex = whiteEngineIndex,
                Result           = result,
                State            = state,
                Prediction       = _hmm.EndPrediction,
                PairId           = pair.PairId,
                GameInPair       = gameInPair,
                WorkerId         = _id,
                Plies            = _hmm.Data.MoveCount(),
                StartFen         = _hmm.Data.StartFen(),
                MoveListBody     = _hmm.Data.GetPgnMoveText(resultToken),
                Remark           = ArenaRemark.Build(result, _hmm.EndPrediction, state,
                                                     divergence, weight, imbalance),
            });

            Status.GamesCompleted++;
        }
        catch (Exception)
        {
            // HeadlessMatchManager's try/finally already Stop()ed both engines
            // before the exception reached us, so no bot.exe leaks; just count
            // the crash and let the worker pick up the next pair.
            _onCrash?.Invoke();
        }
    }


    // Mirrors Arena.GetResultFromState: white-relative +1 / 0 / -1.
    private static int
    ResultFromState(GameEndState state)
    {
        if (state == GameEndState.WhiteWinsByCheckmate || state == GameEndState.WhiteWinsOnTime) return  1;
        if (state == GameEndState.BlackWinsByCheckmate || state == GameEndState.BlackWinsOnTime) return -1;
        return 0;
    }
}
