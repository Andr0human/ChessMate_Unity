using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

// Owns the N worker threads for one distributed-arena run. Hands every worker
// the same two queues (pairs in, results out) and the same read-only
// OpeningBook; each worker is otherwise independent (its own
// HeadlessMatchManager + StopwatchClock). Dynamic dispatch falls out for free —
// a fast worker just dequeues the next pair while a straggler is still on its
// p99 game — which is why a static split was rejected in Phase 0.
//
// Lifecycle: construct → Start() → poll Finished in the controller's Update()
// → Shutdown() on completion or app-quit. Shutdown is the no-leak net: cancel,
// briefly join, then AbortEngines() on any worker still mid-game so no bot.exe
// is orphaned (a game can run ~80s on the fat tail, far longer than we can block
// the main thread, so we can't just join-and-wait).
public class WorkerPool
{
    private readonly List<ArenaWorker> _workers = new List<ArenaWorker>();
    private readonly List<Thread>      _threads = new List<Thread>();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    private int _crashes;
    private int _running;   // workers whose Run() has not yet returned

    // Per-worker launch offset. Without it all N workers spawn their first pair
    // (2 bot.exe each) in the same instant — a thundering herd of ~2N cold engine
    // starts that thrashes the scheduler and flags the earliest games on the 1s
    // base clock. Staggering spreads those cold starts so the herd never forms;
    // games desync within the first round and steady-state load is unaffected.
    private const int StaggerMs = 200;

    public int WorkerCount { get; }
    public int Crashes  => Volatile.Read(ref _crashes);
    public bool Finished => Volatile.Read(ref _running) == 0;

    // Live per-worker status for the dashboard (index = worker id).
    public IReadOnlyList<WorkerStatus> Statuses { get; }


    public
    WorkerPool(int n, string[] engineNames, OpeningBook ob, string streamingAssetsPath,
        float timePerSide, float increment, bool fixedTimePerMove, bool allowOpeningBook,
        string searchLogDir,
        ConcurrentQueue<PairSpec> pairs, ConcurrentQueue<GameResult> results)
    {
        WorkerCount = n;
        var statuses = new WorkerStatus[n];

        for (int i = 0; i < n; i++)
        {
            var worker = new ArenaWorker(i, engineNames, ob, streamingAssetsPath,
                timePerSide, increment, fixedTimePerMove, allowOpeningBook, searchLogDir,
                pairs, results, _cts.Token,
                () => Interlocked.Increment(ref _crashes));

            statuses[i] = worker.Status;
            _workers.Add(worker);

            int startupDelayMs = i * StaggerMs;   // local copy — don't close over the loop var
            var t = new Thread(() =>
            {
                try
                {
                    // Cancellation-aware wait: a Shutdown() during the stagger
                    // window unblocks immediately instead of sleeping it out.
                    if (startupDelayMs > 0)
                        _cts.Token.WaitHandle.WaitOne(startupDelayMs);
                    if (!_cts.IsCancellationRequested)
                        worker.Run();
                }
                finally { Interlocked.Decrement(ref _running); }
            })
            { IsBackground = true, Name = "arena-worker-" + i };
            _threads.Add(t);
        }

        Statuses = statuses;
    }


    public void
    Start()
    {
        _running = WorkerCount;
        foreach (var t in _threads) t.Start();
    }


    // Cooperative, non-blocking cancel. Workers finish their current game (its
    // engines torn down by the PlayGame finally) and exit; poll Finished.
    public void
    RequestStop() => _cts.Cancel();


    // Final teardown — call from OnApplicationQuit / OnDisable. Cancels, gives
    // threads a short window to exit between games, then force-kills any engines
    // still in flight so nothing leaks even on a hard mid-run quit.
    public void
    Shutdown(int joinMsPerThread = 1500)
    {
        _cts.Cancel();
        foreach (var t in _threads)
            t.Join(joinMsPerThread);

        // Anything that didn't exit in the join window is parked in PlayBlocking
        // on a long game; kill its engines so the worker unblocks and no bot.exe
        // is orphaned.
        foreach (var w in _workers)
            w.AbortEngines();
    }
}
