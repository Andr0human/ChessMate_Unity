// A self-contained, Unity-free game loop that plays ONE engine-vs-engine game
// synchronously on a single thread, with no MonoBehaviour, no coroutines, no
// rendering, and no scene Timer. It is the worker-thread counterpart to the
// scene's MatchManager: where MatchManager drives the game through coroutines
// (Play + WaitUntil) and a frame-decremented Timer for the visible Player-vs-AI
// and Arena scenes, this drives it through ChessEngine.PlayBlocking and a
// wall-clock StopwatchClock so N of these can run in parallel inside the
// distributed-arena worker pool.
//
// It stands alone rather than reusing the scene MatchManager, which is a
// MonoBehaviour wired to BoardHandler / TMP / ArenaHud and can't be touched off
// the main thread. The pure game-outcome rules (end detection, insufficient
// material, adjudication) live in the shared static GameUtils so this and
// MatchManager score identically by construction; only the move loop and clock
// sequencing are written out here, minus the coroutines and rendering.
//
// Engine-vs-engine only — no HumanPlayer (that needs UserInput / the main
// thread). After PlayGame() returns, EndState / EndPrediction / Data hold the
// completed-game result for the caller to feed into scoring.
public class HeadlessMatchManager
{
    // Field, not a property: GameUtils.IsGameOver / MoveGenerator take the board
    // by `ref`, and a property can't be passed by ref.
    public ChessBoard   BoardPosition;
    public MatchData    Data          { get; private set; }
    public GameEndState EndState      { get; private set; }
    public int          EndPrediction { get; private set; }

    private int Side2Move;

    // Single clock shared by both engines, exactly as the scene shares one
    // Timer. ChessEngine reads its remaining time through the IClock it was
    // handed; here that's this StopwatchClock, ticking on the worker thread.
    private readonly StopwatchClock _clock;
    private readonly ChessEngine[]  Players = new ChessEngine[2];

    // Match configuration, fixed for this worker. The book + path are injected
    // (resolved on the main thread by the arena driver) since a worker thread
    // can't touch Application.streamingAssetsPath.
    private readonly OpeningBook _ob;
    private readonly string      _streamingAssetsPath;
    private readonly bool         _fixedTimePerMove;
    private readonly bool         _allowOpeningBook;

    private const string startFen =
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private const float AdjournWinMargin = 5.0f;


    public
    HeadlessMatchManager(OpeningBook openingBook, string streamingAssetsPath,
        float timePerSide, float increment,
        bool fixedTimePerMove = false, bool allowOpeningBook = true)
    {
        _ob                  = openingBook;
        _streamingAssetsPath = streamingAssetsPath;
        _fixedTimePerMove    = fixedTimePerMove;
        _allowOpeningBook    = allowOpeningBook;

        _clock = new StopwatchClock(timePerSide, increment);
    }


    // Plays a single game to completion and returns its end state. The same
    // instance can be reused for the next game on this worker (the clock and
    // board are reset on entry). searchLogDir/gameNumber mirror MatchManager's
    // per-game search-trace logging.
    public GameEndState
    PlayGame(string playerWhite, string playerBlack, string opening,
             string searchLogDir = null, int searchLogGameNumber = 0)
    {
        // Reset game state
        Side2Move     = 0;
        BoardPosition = new ChessBoard(startFen);
        Data          = new MatchData(startFen);
        EndState      = GameEndState.Ongoing;
        EndPrediction = 0;

        // Apply the opening line / FEN, if any
        if (!string.IsNullOrEmpty(opening))
            PlayOpening(opening);

        // Create the two engines, sharing this worker's clock + streaming path
        string whiteLog = GameUtils.BuildSearchLogPath(searchLogDir, playerWhite, searchLogGameNumber);
        string blackLog = GameUtils.BuildSearchLogPath(searchLogDir, playerBlack, searchLogGameNumber);

        Players[0] = new ChessEngine(playerWhite, _ob, _clock, _streamingAssetsPath,
                                     _fixedTimePerMove, _allowOpeningBook, whiteLog);
        Players[1] = new ChessEngine(playerBlack, _ob, _clock, _streamingAssetsPath,
                                     _fixedTimePerMove, _allowOpeningBook, blackLog);

        _clock.Init(Side2Move);

        // Always tear the engines down, even if the loop throws. Without this a
        // mid-game exception (PlayBlocking, MakeMove, ...) leaks two bot.exe
        // processes per failed game — across a long parallel run that exhausts
        // handles/RAM. The scene MatchManager doesn't need this because its
        // OnApplicationQuit sweeps the two players; a worker has no such net.
        try
        {
            RunLoop();
        }
        finally
        {
            _clock.ClockFreeze();
            Players[0]?.Stop();
            Players[1]?.Stop();
        }

        return EndState;
    }


    private void
    RunLoop()
    {
        while ((EndState = GameUtils.IsGameOver(ref BoardPosition, Data, Side2Move,
                                                _clock.Remaining(Side2Move)))
               == GameEndState.Ongoing)
        {
            // Block until this side's engine answers; the clock ticks for it
            // throughout (started by Init / the previous turn's ClockUnfreeze).
            var (move, eval) = Players[Side2Move].PlayBlocking(
                BoardPosition, Data.LastPlayedMove());

            // Out of time, or the engine failed to produce a legal move
            // (null move / process died). Either way the side to move loses;
            // the move is discarded, mirroring MatchManager's pre-apply check.
            if (_clock.Remaining(Side2Move) <= 0f || move <= 0)
            {
                EndState = (Side2Move == 0) ? GameEndState.BlackWinsOnTime
                                            : GameEndState.WhiteWinsOnTime;
                break;
            }

            if (EndPrediction == 0)
                EndPrediction = GameUtils.PredictionCall(Data, AdjournWinMargin);

            ApplyMove(move, eval);

            Side2Move ^= 1;
        }
    }


    // The non-visual core of MatchManager.UpdateBoardElements: advance the
    // clock, apply the move, and record it. Side2Move still holds the side that
    // just moved here (flipped by the caller afterward), so the recorded
    // remaining time uses `Side2Move ^ 1` exactly as the scene does.
    private void
    ApplyMove(int move, float eval)
    {
        _clock.SwitchPlayer();
        _clock.ClockFreeze();

        BoardPosition.MakeMove(move);
        Data.Add(move, eval, _clock.Remaining(Side2Move ^ 1),
                 BoardPosition.GenerateHashKey());

        _clock.ClockUnfreeze();
    }


    // Forcibly stop the current game's engines from ANOTHER thread — the worker
    // pool's shutdown path, when the app quits mid-run and a worker is blocked
    // in PlayBlocking on a game that could still have ~80s to run (the fat
    // tail). ChessEngine.Stop() is idempotent and defensive against concurrent
    // stdout callbacks, so calling it cross-thread while the owning worker is
    // mid-search is safe: the process dies, PlayBlocking unblocks on the
    // process-exit escape, and the worker scores it a loss / exits. No-op
    // between games (the previous PlayGame's finally already Stop()ed both, and
    // a second Stop() is harmless). Without this, hard app-exit would orphan the
    // in-flight bot.exe children (Windows doesn't kill them with the parent).
    public void
    AbortEngines()
    {
        Players[0]?.Stop();
        Players[1]?.Stop();
    }


    // Re-seat the board at the standard start position. The fallback when an
    // opening line is blank or malformed: better to play a normal game from the
    // start than to corrupt the board or crash the worker on a bad book entry.
    private void
    ResetToStartPosition()
    {
        Data          = new MatchData(startFen);
        BoardPosition = new ChessBoard(startFen);
        Side2Move     = 0;
    }


    private void
    PlayOpening(string opening)
    {
        // Blank / whitespace line (e.g. a stray empty line in the book file):
        // nothing to apply — just play from the start position.
        if (string.IsNullOrWhiteSpace(opening))
        {
            ResetToStartPosition();
            return;
        }

        if (_ob.IsFen(opening))
        {
            // A malformed FEN throws in the ChessBoard parser. A worker thread
            // would catch this as a "crash" loss with no hint it was a bad book
            // line; instead detect it and fall back to the start position.
            ChessBoard parsed = null;
            try { parsed = new ChessBoard(opening); }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"Invalid opening FEN '{opening}' ({e.Message}); "
                                             + "starting from the initial position.");
            }

            if (parsed == null)
            {
                ResetToStartPosition();
                return;
            }

            Data          = new MatchData(opening);
            Data.SeedHalfmoveClock(parsed.halfmove);   // honor the FEN's 50-move clock
            BoardPosition = parsed;
            Side2Move     = BoardPosition.color ^ 1;
        }
        else
        {
            System.Collections.Generic.List<int> openingLine = _ob.ExtractLine(opening);

            // A 0 entry means a token failed to decode (malformed line). Applying
            // it would corrupt the board via MakeMove(0); fall back to the start.
            if (openingLine.Contains(0))
            {
                UnityEngine.Debug.LogWarning($"Invalid opening line '{opening}'; "
                                             + "starting from the initial position.");
                ResetToStartPosition();
                return;
            }

            Data = new MatchData(startFen);
            float timeLeft = _clock.AllotedTimePerSide;

            foreach (int move in openingLine)
            {
                ulong prevHash = BoardPosition.hashvalue;
                BoardPosition.MakeMove(move);
                Data.Add(move, 0, timeLeft, prevHash);

                Side2Move ^= 1;
            }

            Data.BookMoveCount = openingLine.Count;
        }
    }
}
