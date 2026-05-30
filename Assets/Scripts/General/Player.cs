using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private Timer       _tmr;

    private Process _engineProcess;
    private string  _engineName;
    private string  _enginePath;

    public bool    FixedMoveTime;
    public bool AllowOpeningBook;

    private readonly ConcurrentQueue<string> _engineOutput = new ConcurrentQueue<string>();

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
    ChessEngine(string engine, OpeningBook openingBook, bool fixedMoveTime=false,
        bool allowOpeningBook=true, string searchLogPath=null)
    {
        _ob  = openingBook;
        _tmr = GameObject.FindAnyObjectByType<Timer>();

        _engineName       = engine;
        FixedMoveTime    = fixedMoveTime;
        AllowOpeningBook = allowOpeningBook;

        _enginePath = Application.streamingAssetsPath + "/" + engine + ".exe";

        _engineProcess = new Process();
        _engineProcess.StartInfo = new ProcessStartInfo(_enginePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Application.streamingAssetsPath,
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


    public IEnumerator
    Play(ChessBoard position, int lastMove)
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
            yield break;
        }

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
            // time (UCI-correct split). ChessClocks[0] = white, [1] = black;
            // clocks can dip briefly negative, so clamp each to >= 1 ms.
            int wtime = Mathf.Max(1, Mathf.RoundToInt(_tmr.ChessClocks[0] * 1000f));
            int btime = Mathf.Max(1, Mathf.RoundToInt(_tmr.ChessClocks[1] * 1000f));
            int inc   = Mathf.Max(0, Mathf.RoundToInt(_tmr.IncrementTime    * 1000f));

            SendLine("go wtime " + wtime + " btime " + btime
                + " winc " + inc + " binc " + inc);
        }

        yield return new WaitUntil( ReadOutput );

        // Track our own move so the engine sees it as part of history next turn.
        if (_lastBestmoveUci != null && _engineMove > 0)
            _uciHistory.Add(_lastBestmoveUci);
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
