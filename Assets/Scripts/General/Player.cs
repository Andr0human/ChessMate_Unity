using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;


public interface IPlayer
{
    IEnumerator Play(ChessBoard position, int lastMove);

    (int, float) GetResults();

    void Stop() {}

    bool MoveFound();

    bool ReadOutput();

    void StopReadOutput();
}


public class ChessEngine : IPlayer
{
    private OpeningBook _ob;

    // Injected so the engine never touches a scene component or Unity API: a
    // worker thread can't FindAnyObjectByType<Timer> or read
    // Application.streamingAssetsPath off the main thread. In scenes these are
    // the real Timer + Application.streamingAssetsPath, passed by MatchManager.
    private readonly IClock _clock;
    private readonly string _streamingAssetsPath;

    private Process _engineProcess;
    private string  _engineName;
    private string  _enginePath;

    public bool    FixedMoveTime;
    public bool AllowOpeningBook;

    private readonly ConcurrentQueue<string> _engineOutput = new ConcurrentQueue<string>();

    // Pulsed by the stdout handler on every line so PlayBlocking (the
    // synchronous, worker-thread flavor) can wait for a bestmove without
    // busy-spinning. The coroutine Play path ignores it — it frame-polls
    // ReadOutput via WaitUntil instead. AutoResetEvent latches a single
    // signal, so pulses with no waiter (coroutine path) don't accumulate.
    // Nulled by Stop() on teardown, hence not readonly; the handler null-guards.
    private AutoResetEvent _outputSignal = new AutoResetEvent(false);

    // Stashed by Play() so ReadOutput can decode the engine's bestmove
    // back into a packed-int move before _engineMove is exposed.
    private ChessBoard _currentPosition;

    // Anchor + replayed moves let the engine see repetition history.
    // Anchor is set on the engine's first Play() call (post-opening FEN
    // when applicable). Each subsequent turn appends the opponent's reply
    // and our own bestmove.
    private string _anchorFen;
    private readonly List<string> _uciHistory = new List<string>();
    private string _lastBestmoveUci;

    private   int _engineMove;
    private float _engineEval;

    // Optional per-game search-trace log. When set, every line the engine
    // emits on stdout (commands echoed, info lines, bestmove) is mirrored
    // here so Arena games are debuggable after the fact.
    private StreamWriter _searchLog;
    private readonly object _searchLogLock = new object();


    public
    ChessEngine(string engine, OpeningBook openingBook, IClock clock,
        string streamingAssetsPath, bool fixedMoveTime=false,
        bool allowOpeningBook=true, string searchLogPath=null)
    {
        _ob    = openingBook;
        _clock = clock;
        _streamingAssetsPath = streamingAssetsPath;

        _engineName       = engine;
        FixedMoveTime    = fixedMoveTime;
        AllowOpeningBook = allowOpeningBook;

        _enginePath = _streamingAssetsPath + "/" + engine + ".exe";

        _engineProcess = new Process();
        _engineProcess.StartInfo = new ProcessStartInfo(_enginePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = _streamingAssetsPath,
        };

        if (!string.IsNullOrEmpty(searchLogPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(searchLogPath));
                // No AutoFlush: lines buffer in memory and hit disk in batches
                // instead of one synchronous write per `info` line. Stop()'s
                // Dispose() flushes the tail at game end, so nothing is lost.
                _searchLog = new StreamWriter(searchLogPath);
            }
            catch
            {
                _searchLog = null;
            }
        }

        _engineProcess.OutputDataReceived += (sender, args) =>
        {
            if (args.Data == null) return;
            _engineOutput.Enqueue(args.Data);
            WriteLog("< " + args.Data);
            // A late stdout line can land during/after Stop() nulls+disposes
            // the event; null-guard + catch absorb that race.
            try { _outputSignal?.Set(); } catch (System.ObjectDisposedException) { /* stopped */ }
        };

        _engineProcess.Start();
        _engineProcess.BeginOutputReadLine();
        _engineProcess.StandardInput.NewLine = "\n";

        // UCI handshake. We don't strictly require uciok/readyok before
        // playing — the engine processes commands in order — but draining
        // the early lines keeps the queue clean.
        SendLine("uci");
        SendLine("isready");
        SendLine("ucinewgame");
    }


    private void
    WriteLog(string line)
    {
        if (_searchLog == null) return;
        lock (_searchLogLock)
        {
            try { _searchLog.WriteLine(line); }
            catch { /* log is best-effort; never block the game */ }
        }
    }


    private void
    SendLine(string line)
    {
        if (_engineProcess == null || _engineProcess.HasExited) return;
        _engineProcess.StandardInput.WriteLine(line);
        _engineProcess.StandardInput.Flush();
        WriteLog("> " + line);
    }


    // Per-turn setup shared by Play (coroutine) and PlayBlocking (synchronous).
    // Resets per-turn state, maintains the anchor FEN + replayed-move history,
    // and short-circuits with a book move when one is available. Returns true
    // if a book move was taken — the caller then skips the engine search.
    private bool
    PrepareTurn(ChessBoard position, int lastMove)
    {
        _engineMove = 0;
        _engineEval = 0;
        _lastBestmoveUci = null;
        _currentPosition = position;

        // First call locks in the anchor FEN. On subsequent calls, the
        // opponent's reply is appended so the engine sees full history
        // since the engine first saw the game.
        if (_anchorFen == null)
        {
            _anchorFen = position.Fen();
        }
        else if (lastMove != 0)
        {
            _uciHistory.Add(MoveCodec.EncodeToUci(lastMove));
        }

        // Play Book Move if possible
        bool bookAvailable = (_ob != null) && (_ob.Book != null) && (_ob.Book.Count > 0);
        if (AllowOpeningBook && bookAvailable && _ob.PositionInOpeningBook(ref position))
        {
            int bookMove = _ob.PlayBookMove(ref position);
            _engineEval = 0;
            _engineMove = bookMove;
            _uciHistory.Add(MoveCodec.EncodeToUci(bookMove));
            return true;
        }

        return false;
    }


    // Sends the current position + a `go` command. Shared by both Play flavors.
    private void
    SendGo()
    {
        SendPosition(_anchorFen, _uciHistory);

        if (FixedMoveTime)
        {
            // Flat per-move budget (Arena's fixedTimePerMove). The engine's
            // movetime path is unchanged.
            SendLine("go movetime 500");
        }
        else
        {
            // Forward the raw clock and let the engine decide its own search
            // time (UCI-correct split). Remaining(0) = white, (1) = black;
            // clocks can dip briefly negative, so clamp each to >= 1 ms.
            int wtime = System.Math.Max(1, (int)System.Math.Round(_clock.Remaining(0) * 1000f));
            int btime = System.Math.Max(1, (int)System.Math.Round(_clock.Remaining(1) * 1000f));
            int inc   = System.Math.Max(0, (int)System.Math.Round(_clock.Increment()  * 1000f));

            SendLine("go wtime " + wtime + " btime " + btime
                + " winc " + inc + " binc " + inc);
        }
    }


    // Tracks our own move so the engine sees it as part of history next turn.
    private void
    RecordOwnMove()
    {
        if (_lastBestmoveUci != null && _engineMove > 0)
            _uciHistory.Add(_lastBestmoveUci);
    }


    public IEnumerator
    Play(ChessBoard position, int lastMove)
    {
        if (PrepareTurn(position, lastMove))
            yield break;

        SendGo();

        yield return new WaitUntil( ReadOutput );

        RecordOwnMove();
    }


    // Synchronous twin of Play for background arena workers, which run off the
    // Unity main thread and so can't use coroutines / WaitUntil. Sends the
    // position + go, then blocks the calling thread until the engine emits a
    // bestmove, and returns (move, eval) directly instead of stashing them for
    // a later GetResults() call. Same engine conversation as Play — only the
    // waiting strategy differs (block vs. frame-poll).
    public (int, float)
    PlayBlocking(ChessBoard position, int lastMove)
    {
        if (PrepareTurn(position, lastMove))
            return (_engineMove, _engineEval);

        SendGo();

        // Block until the engine answers. The stdout handler pulses
        // _outputSignal per line; ReadOutput() drains the queue and returns
        // true once it sees a bestmove. Two escape hatches keep a worker from
        // hanging forever, both checked on the 1s wake-up:
        //   - the engine process exited (died mid-search, no more lines coming);
        //   - the side-to-move's clock ran out while the engine is alive but
        //     wedged (no bestmove arriving). The coroutine Play path is saved
        //     here by its WaitUntil clock check; the blocking path has no frame
        //     loop, so it must check the (still-ticking) clock itself.
        // On either break _engineMove stays 0 → caller scores it a time loss.
        // ChessBoard.color: 1 = white to move → clock side 0, 0 = black → side 1.
        int side = _currentPosition.color ^ 1;
        while (!ReadOutput())
        {
            if (_outputSignal.WaitOne(1000)) continue;
            if (_engineProcess != null && _engineProcess.HasExited) break;
            if (_clock.Remaining(side) <= 0f) break;
        }

        RecordOwnMove();
        return (_engineMove, _engineEval);
    }


    private void
    SendPosition(string fen, List<string> moves)
    {
        string cmd = "position fen " + fen;
        if (moves.Count > 0)
            cmd += " moves " + string.Join(" ", moves);

        SendLine(cmd);
    }


    public bool
    ReadOutput()
    {
        if ((_engineProcess == null) || (_engineMove == -1))
            return true;

        while (_engineOutput.TryDequeue(out string line))
        {
            if (line.StartsWith("info "))
            {
                ParseInfoScore(line);
                continue;
            }

            if (!line.StartsWith("bestmove ")) continue;

            string[] parts = line.Split(' ');
            if (parts.Length < 2) continue;

            string uci = parts[1];
            // _engineEval already populated from latest "info score" line.
            _lastBestmoveUci = uci;

            if (uci == "0000")
            {
                _engineMove = 0;
            }
            else
            {
                int packed = MoveCodec.DecodeFromUci(uci, ref _currentPosition);
                _engineMove = packed;
            }
            return true;
        }

        return false;
    }


    // Parses "info ... score cp N ..." or "info ... score mate N ..." and
    // stores the result in _engineEval as a white-relative pawn-unit float.
    // Elsa emits cp from the side-to-move POV, so we flip when STM is black
    // to match the white-relative convention used by MatchData / PredictionCall.
    private void
    ParseInfoScore(string line)
    {
        int idx = line.IndexOf(" score ");
        if (idx < 0) return;

        string[] tk = line.Substring(idx + 1).Split(' ');
        if (tk.Length < 3) return;

        if (!int.TryParse(tk[2], out int v)) return;

        float stmEval;
        if (tk[1] == "mate")
            stmEval = (v >= 0 ? 100f : -100f);
        else if (tk[1] == "cp")
            stmEval = v / 100f;
        else
            return;

        // ChessBoard.color: 1 = white to move, 0 = black to move.
        int sign = (_currentPosition.color == 1) ? 1 : -1;
        _engineEval = sign * stmEval;
    }


    public void
    Stop()
    {
        if (_engineProcess != null && !_engineProcess.HasExited)
        {
            try
            {
                SendLine("quit");
                if (!_engineProcess.WaitForExit(2000))
                    _engineProcess.Kill();
            }
            catch
            {
                try { _engineProcess.Kill(); } catch { /* already gone */ }
            }

            _engineProcess.Close();
            _engineProcess.Dispose();
        }

        _engineProcess = null;

        // Tear the signal down exactly once. Stop() can run more than once
        // (game-end, then MatchManager.OnApplicationQuit), and a buffered
        // stdout line can still fire the handler mid-teardown. Null it first so
        // a second Stop() is a no-op and the handler's null-guard short-circuits;
        // Set() before Dispose() wakes any thread parked in PlayBlocking.
        AutoResetEvent sig = _outputSignal;
        _outputSignal = null;
        if (sig != null)
        {
            try { sig.Set(); sig.Dispose(); } catch { /* already gone */ }
        }

        if (_searchLog != null)
        {
            lock (_searchLogLock)
            {
                try { _searchLog.Dispose(); } catch { /* already gone */ }
                _searchLog = null;
            }
        }
    }


    public bool
    MoveFound()
    { return _engineMove > 0; }


    public void
    StopReadOutput()
    { _engineMove = -1; }


    public (int, float)
    GetResults()
    { return (_engineMove, _engineEval); }
}


public class HumanPlayer : IPlayer
{
    private UserInput _ui;

    private   int _humanMove;
    private float _humanEval;

    ChessBoard _boardPosition;


    public
    HumanPlayer()
    {
        _ui = GameObject.FindAnyObjectByType<UserInput>();
    }


    public (int, float)
    GetResults()
    { return (_humanMove, _humanEval); }


    public IEnumerator
    Play(ChessBoard position, int lastMove)
    {
        _humanMove = 0;
        _humanEval = 0;
        _boardPosition = position;

        MoveList movelist = MoveGenerator.GenerateMoves(ref _boardPosition);

        _ui.GetSquares(ref movelist);
        yield return new WaitUntil(() => (_ui.InitSquare != -1) && (_ui.DestSquare != -1));

        _humanMove = MoveCodec.Encode(
            ref _boardPosition, _ui.InitSquare, _ui.DestSquare, _ui.PromotedPiece);
        _humanEval = 0;
    }


    public bool
    MoveFound()
    { return _humanMove > 0; }


    public void
    StopReadOutput()
    { _humanMove = -1; }


    public bool
    ReadOutput()
    { return true; }
}
