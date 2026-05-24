using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;


public interface IPlayer
{
    IEnumerator Play(ChessBoard position, int last_move);

    (int, float) GetResults();

    void Stop() {}

    bool MoveFound();

    bool ReadOutput();

    void StopReadOutput();
}


public class ChessEngine : IPlayer
{
    private   OpeningBook ob;
    private static Timer tmr;

    private Process EngineProcess;
    private string  EngineName;
    private string  EnginePath;

    public bool    FixedMoveTime;
    public bool AllowOpeningBook;

    private readonly ConcurrentQueue<string> EngineOutput = new ConcurrentQueue<string>();

    // Stashed by Play() so ReadOutput can decode the engine's bestmove
    // back into a packed-int move before EngineMove is exposed.
    private ChessBoard CurrentPosition;

    // Anchor + replayed moves let the engine see repetition history.
    // Anchor is set on the engine's first Play() call (post-opening FEN
    // when applicable). Each subsequent turn appends the opponent's reply
    // and our own bestmove.
    private string AnchorFen;
    private readonly List<string> UciHistory = new List<string>();
    private string LastBestmoveUci;

    private   int EngineMove;
    private float EngineEval;

    // Optional per-game search-trace log. When set, every line the engine
    // emits on stdout (commands echoed, info lines, bestmove) is mirrored
    // here so Arena games are debuggable after the fact.
    private StreamWriter SearchLog;
    private readonly object SearchLogLock = new object();


    public
    ChessEngine(string __engine, bool __fixed_move_time=false,
        bool __allow_opening_book=true, string __search_log_path=null)
    {
        ob  = GameObject.FindAnyObjectByType<OpeningBook>();
        tmr = GameObject.FindAnyObjectByType<Timer>();

        EngineName       = __engine;
        FixedMoveTime    = __fixed_move_time;
        AllowOpeningBook = __allow_opening_book;

        EnginePath = Application.streamingAssetsPath + "/" + __engine + ".exe";

        EngineProcess = new Process();
        EngineProcess.StartInfo = new ProcessStartInfo(EnginePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Application.streamingAssetsPath,
        };

        if (!string.IsNullOrEmpty(__search_log_path))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(__search_log_path));
                // No AutoFlush: lines buffer in memory and hit disk in batches
                // instead of one synchronous write per `info` line. Stop()'s
                // Dispose() flushes the tail at game end, so nothing is lost.
                SearchLog = new StreamWriter(__search_log_path);
            }
            catch
            {
                SearchLog = null;
            }
        }

        EngineProcess.OutputDataReceived += (sender, args) =>
        {
            if (args.Data == null) return;
            EngineOutput.Enqueue(args.Data);
            WriteLog("< " + args.Data);
        };

        EngineProcess.Start();
        EngineProcess.BeginOutputReadLine();
        EngineProcess.StandardInput.NewLine = "\n";

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
        if (SearchLog == null) return;
        lock (SearchLogLock)
        {
            try { SearchLog.WriteLine(line); }
            catch { /* log is best-effort; never block the game */ }
        }
    }


    private void
    SendLine(string line)
    {
        if (EngineProcess == null || EngineProcess.HasExited) return;
        EngineProcess.StandardInput.WriteLine(line);
        EngineProcess.StandardInput.Flush();
        WriteLog("> " + line);
    }


    public IEnumerator
    Play(ChessBoard position, int last_move)
    {
        EngineMove = 0;
        EngineEval = 0;
        LastBestmoveUci = null;
        CurrentPosition = position;

        // First call locks in the anchor FEN. On subsequent calls, the
        // opponent's reply is appended so the engine sees full history
        // since the engine first saw the game.
        if (AnchorFen == null)
        {
            AnchorFen = position.Fen();
        }
        else if (last_move != 0)
        {
            UciHistory.Add(MoveCodec.EncodeToUci(last_move));
        }

        // string currentFen = position.Fen();
        // UnityEngine.Debug.Log(string.Format(
        //     "[{0}] turn — fen={1} last_move={2}",
        //     EngineName, currentFen,
        //     (last_move != 0) ? MoveCodec.EncodeToUci(last_move) : "-"));

        // Play Book Move if possible
        bool bookAvailable = (ob != null) && (ob.Book != null) && (ob.Book.Count > 0);
        if (AllowOpeningBook && bookAvailable && ob.PositionInOpeningBook(ref position))
        {
            int bookMove = ob.PlayBookMove(ref position);
            EngineEval = 0;
            // UnityEngine.Debug.Log("[" + EngineName + "] book move: " + MoveCodec.EncodeToUci(bookMove));
            EngineMove = bookMove;
            UciHistory.Add(MoveCodec.EncodeToUci(bookMove));
            yield break;
        }

        SendPosition(AnchorFen, UciHistory);

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
            int wtime = Mathf.Max(1, Mathf.RoundToInt(tmr.ChessClocks[0] * 1000f));
            int btime = Mathf.Max(1, Mathf.RoundToInt(tmr.ChessClocks[1] * 1000f));
            int inc   = Mathf.Max(0, Mathf.RoundToInt(tmr.IncrementTime    * 1000f));

            SendLine("go wtime " + wtime + " btime " + btime
                + " winc " + inc + " binc " + inc);
        }

        yield return new WaitUntil( ReadOutput );

        // Track our own move so the engine sees it as part of history next turn.
        if (LastBestmoveUci != null && EngineMove > 0)
            UciHistory.Add(LastBestmoveUci);
    }


    private void
    SendPosition(string fen, List<string> moves)
    {
        string cmd = "position fen " + fen;
        if (moves.Count > 0)
            cmd += " moves " + string.Join(" ", moves);

        // UnityEngine.Debug.Log(string.Format("[{0}] -> {1}", EngineName, cmd));

        SendLine(cmd);
    }


    public bool
    ReadOutput()
    {
        if ((EngineProcess == null) || (EngineMove == -1))
            return true;

        while (EngineOutput.TryDequeue(out string line))
        {
            // UnityEngine.Debug.Log("[" + EngineName + "] <- " + line);

            if (line.StartsWith("info "))
            {
                ParseInfoScore(line);
                continue;
            }

            if (!line.StartsWith("bestmove ")) continue;

            string[] parts = line.Split(' ');
            if (parts.Length < 2) continue;

            string uci = parts[1];
            // EngineEval already populated from latest "info score" line.
            LastBestmoveUci = uci;

            if (uci == "0000")
            {
                EngineMove = 0;
            }
            else
            {
                int packed = MoveCodec.DecodeFromUci(uci, ref CurrentPosition);
                EngineMove = packed;

                // UnityEngine.Debug.Log(string.Format(
                //     "[{0}] bestmove uci={1} packed=0x{2:X} from_fen={3}",
                //     EngineName, uci, packed, CurrentPosition.Fen()));
            }
            return true;
        }

        return false;
    }


    // Parses "info ... score cp N ..." or "info ... score mate N ..." and
    // stores the result in EngineEval as a white-relative pawn-unit float.
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
        int sign = (CurrentPosition.color == 1) ? 1 : -1;
        EngineEval = sign * stmEval;
    }


    public void
    Stop()
    {
        if (EngineProcess != null && !EngineProcess.HasExited)
        {
            try
            {
                SendLine("quit");
                if (!EngineProcess.WaitForExit(2000))
                    EngineProcess.Kill();
            }
            catch
            {
                try { EngineProcess.Kill(); } catch { /* already gone */ }
            }

            EngineProcess.Close();
            EngineProcess.Dispose();
        }

        EngineProcess = null;

        if (SearchLog != null)
        {
            lock (SearchLogLock)
            {
                try { SearchLog.Dispose(); } catch { /* already gone */ }
                SearchLog = null;
            }
        }
    }


    public bool
    MoveFound()
    { return EngineMove > 0; }


    public void
    StopReadOutput()
    { EngineMove = -1; }


    public (int, float)
    GetResults()
    { return (EngineMove, EngineEval); }
}


public class HumanPlayer : IPlayer
{
    private UserInput ui;
    private MoveGenerator mg;

    private   int HumanMove;
    private float HumanEval;

    ChessBoard BoardPosition;


    public
    HumanPlayer()
    {
        ui = GameObject.FindAnyObjectByType<UserInput>();
        mg = GameObject.FindAnyObjectByType<MoveGenerator>();
    }


    private int
    GenerateEncodeMoveForUser()
    {
        int color = BoardPosition.color;

        int init_index = ui.InitSquare;
        int dest_index = ui.DestSquare;

        int          piece = BoardPosition.board[init_index] & 7;
        int captured_piece = BoardPosition.board[dest_index] & 7;
        int promoted_piece = ui.PromotedPiece;

        int       pos_bits = (dest_index << 6) | init_index;
        int      type_bits = (captured_piece << 15) | (piece << 12);
        int      color_bit = color << 20;
        int promotion_bits = promoted_piece << 18;

        int move = promotion_bits | color_bit | type_bits | pos_bits;
        return move;
    }


    public (int, float)
    GetResults()
    { return (HumanMove, HumanEval); }


    public IEnumerator
    Play(ChessBoard position, int last_move)
    {
        HumanMove = 0;
        HumanEval = 0;
        BoardPosition = position;

        MoveList movelist = mg.GenerateMoves(ref BoardPosition);

        ui.GetSquares(ref movelist);
        yield return new WaitUntil(() => (ui.InitSquare != -1) && (ui.DestSquare != -1));

        HumanMove = GenerateEncodeMoveForUser();
        HumanEval = 0;
    }


    public bool
    MoveFound()
    { return HumanMove > 0; }


    public void
    StopReadOutput()
    { HumanMove = -1; }


    public bool
    ReadOutput()
    { return true; }
}
