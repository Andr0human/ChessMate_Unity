using System;
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
// (which also drives RampUp) → Shutdown() on completion or app-quit. Shutdown is
// the no-leak net: cancel, briefly join, then AbortEngines() on any worker still
// mid-game so no bot.exe is orphaned (a game can run ~80s on the fat tail, far
// longer than we can block the main thread, so we can't just join-and-wait).
public class WorkerPool
{
    private readonly List<ArenaWorker> _workers = new List<ArenaWorker>();
    private readonly List<Thread>      _threads = new List<Thread>();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    private int _crashes;
    private int _running;   // workers whose Run() has not yet returned

    // --- Cold-start ramp (incremental additive) -------------------------------
    // All N workers launching their first pair at t=0 is a thundering herd of ~2N
    // cold engine starts + N concurrent searches; on a 6-physical-core box at N=8
    // that oversubscription flagged ~the first 60 games on the 1s base clock (the
    // N-sweep, distributed-arena-sweep.md). It self-heals as game-length variance
    // drifts the workers out of phase, so the fix only has to protect the opening
    // window: release workers in small batches instead of all at once. Each batch
    // joins a pool that has already desynced, so concurrent cold starts never
    // exceed RampStep — no synchronized spike at any worker level. Generalises to
    // any target N with no machine-specific core constant (why this was preferred
    // over a hold-back-to-"safe-cores" scheme). Touches only the cold start;
    // steady-state throughput/strength are already clean and unaffected.
    private const int RampInitial     = 2;    // workers live at launch
    private const int RampStep        = 2;    // workers added per ramp step
    private const int RampGamesPerStep = 12;  // games between steps (doc: ~10–20)

    // One release gate per worker; its thread parks here until the gate is Set.
    // Set only from the main thread (Start / RampUp) or on cancellation, so the
    // gate-management state below needs no locking.
    private readonly List<ManualResetEventSlim> _gates = new List<ManualResetEventSlim>();
    private int _releasedCount;   // workers whose gate has been opened so far

    // Held so RampUp can release everyone the moment the pair queue empties — a
    // run too short to reach full N (or its tail) must not strand parked workers,
    // or Finished would never go true.
    private readonly ConcurrentQueue<PairSpec> _pairs;

    public int WorkerCount { get; }
    public int Crashes  => Volatile.Read(ref _crashes);
    public bool Finished => Volatile.Read(ref _running) == 0;

    // Live per-worker status for the dashboard (index = worker id). A not-yet-
    // released worker simply shows Idle until its gate opens.
    public IReadOnlyList<WorkerStatus> Statuses { get; }


    public
    WorkerPool(int n, string[] engineNames, OpeningBook ob, string streamingAssetsPath,
        float timePerSide, float increment, bool fixedTimePerMove, bool allowOpeningBook,
        string searchLogDir,
        ConcurrentQueue<PairSpec> pairs, ConcurrentQueue<GameResult> results)
    {
        WorkerCount = n;
        _pairs      = pairs;
        var statuses = new WorkerStatus[n];

        for (int i = 0; i < n; i++)
        {
            var worker = new ArenaWorker(i, engineNames, ob, streamingAssetsPath,
                timePerSide, increment, fixedTimePerMove, allowOpeningBook, searchLogDir,
                pairs, results, _cts.Token,
                () => Interlocked.Increment(ref _crashes));

            statuses[i] = worker.Status;
            _workers.Add(worker);

            var gate = new ManualResetEventSlim(false);
            _gates.Add(gate);

            var t = new Thread(() =>
            {
                try
                {
                    // Park until released (RampUp) or cancelled (Shutdown). The
                    // token's wait handle is the second wake reason so a Shutdown
                    // during the ramp unblocks a still-gated worker immediately
                    // instead of leaving it parked forever.
                    WaitHandle.WaitAny(new[] { gate.WaitHandle, _cts.Token.WaitHandle });
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

        // Open the first batch; the rest follow via RampUp as games complete.
        ReleaseUpTo(Math.Min(RampInitial, WorkerCount));
    }


    // Drive the cold-start ramp. Call from the controller's Update() with the
    // running games-completed count. Opens the next batch each time a step
    // boundary is crossed; releases everyone early once the pair queue is empty
    // so a short run's tail never leaves workers parked. Cheap no-op once every
    // worker is live. Main-thread only.
    public void
    RampUp(int gamesCompleted)
    {
        if (_releasedCount >= WorkerCount) return;

        int target = _pairs.IsEmpty
            ? WorkerCount
            : Math.Min(WorkerCount, RampInitial + RampStep * (gamesCompleted / RampGamesPerStep));

        ReleaseUpTo(target);
    }


    private void
    ReleaseUpTo(int target)
    {
        while (_releasedCount < target)
            _gates[_releasedCount++].Set();
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

        // Threads are joined/aborted by here, so no one is still waiting on a gate.
        foreach (var g in _gates)
            g.Dispose();
    }
}
