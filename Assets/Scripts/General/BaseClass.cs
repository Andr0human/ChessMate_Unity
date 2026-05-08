using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;


public static class TT
{
    public static ulong[] HashIndex;

    static ulong x = 1237;

    static ulong xorshift64star()
    {
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        return x * 0x2545F4914F6CDD1DUL;
    }


    public static void
    Init()
    {
        if (HashIndex != null) return;

        HashIndex = new ulong[860];

        for (int i = 0; i < 860; i++)
            HashIndex[i] = xorshift64star();
    }


    public static ulong
    HashUpdate(int piece, int pos)
    {
        int color = piece >> 3;
        piece = (piece & 7) - 1;

        return HashIndex[ 85 + pos + 64 * (piece + 6 * color) ];
    }
}


public class MoveList
{
    public int KingAttackers;
    public int pColor, moveCount;
    public ulong startIndex;
    public ulong[] endIndex;

    public List<int> moves;

    public
    MoveList(int pc = 0)
    {
        KingAttackers = moveCount = 0;
        pColor = pc;
        startIndex = 0;
        endIndex = new ulong[64];
        moves = new List<int>();
    }

    public void
    Add(int idx, ulong val)
    {
        if (val != 0)
        {
            startIndex |= 1UL << (idx);
            endIndex[idx] |= val;
            moveCount++;
        }
    }

    public void
    Clear()
    {
        startIndex = 0;
        moveCount = KingAttackers = 0;
        for (int i = 0; i < 64; i++) endIndex[i] = 0;
    }

    public bool
    ValidInitSquare(int ip)
    { return (startIndex & (1UL << ip)) != 0; }

    public bool
    ValidDestSquare(int ip, int fp)
    {
        return (  startIndex & (1UL << ip)) != 0
            && (endIndex[ip] & (1UL << fp)) != 0;
    }

    public bool
    ContainsMove(int move)
    {
        int ip = move & 63, fp = (move >> 6) & 63;
        if ((startIndex & (1UL << ip)) != 0)
            if ((endIndex[ip] & (1UL << fp)) != 0) return true;
        return false;
    }


    public void
    Add(int move)
    {
        moves.Add(move);

        int ip = move & 63;
        int fp = (move >> 6) & 63;

        startIndex   |= 1UL << ip;
        endIndex[ip] |= 1UL << fp;

        moveCount++;
    }

};


public class MatchData
{
    private List< int >             moves;
    private List<float>             evals;
    private List<float>         time_left;
    private List<ulong> occured_positions;
    private string start_pos_fen;

    bool first_move;

    public MatchData(string fen)
    {
        moves = new List<int>();
        evals = new List<float>();
        time_left = new List<float>();
        occured_positions = new List<ulong>();
        first_move = true;
        start_pos_fen = fen;
    }

    public void
    Add(int move, float eval, float r_time, ulong key)
    {
        moves.Add(move);
        evals.Add(eval);
        time_left.Add(r_time);

        // If moved piece is a pawn or there is a captured piece
        if ((((move >> 12) & 7) == 1) || (((move >> 15) & 7) != 0))
            occured_positions.Clear();

        occured_positions.Add(key);
    }

    public int
    LastPlayedMove()
    {
        if (first_move)
        {
            first_move = false;
            return 0;
        }
        return (moves.Count > 0) ? (moves[moves.Count - 1]) : (0);
    }

    public bool
    FiftyMoveRuleDraw()
    { return occured_positions.Count > 100; }

    public bool
    ThreeMoveRepetitionDraw()
    {
        if (occured_positions.Count < 3)
            return false;
        ulong last_key = occured_positions[occured_positions.Count - 1];
        int count = 0;

        foreach (var key in occured_positions)
            if (key == last_key) count++;
        
        return count >= 3;
    }

    public int
    DifferentEvalCount(float margin)
    {
        int count = 0;
        for (int i = 1; i < evals.Count; i += 2)
        {
            float eval_diff = Mathf.Abs(evals[i] - evals[i - 1]);
            float max_eval = Mathf.Max(Mathf.Abs(evals[i]), Mathf.Abs(evals[i - 1]));

            if ((eval_diff >= margin) && (max_eval <= 12f)) count++;
        }
        return count;
    }

    public (float, float)
    LastEvalPair()
    {
        int n = evals.Count;
        if (n < 2)
            return (0f, 0f);
        
        return (evals[n - 2], evals[n - 1]);
    }

    public bool
    DrawnPositionForContinuousMoves(float draw_margin, int length)
    {
        if (evals.Count < length)
            return false;
        
        int count = 0;
        foreach (float eval in evals)
        {
            count = (Mathf.Abs(eval) < draw_margin) ? (count + 1) : (0);

            if (count >= length)
                return true;

            if (count + (length - evals.Count) < length)
                return false;
        }

        return false;
    }

    public string
    GetMoveList(MoveGenerator mg)
    {
        string res = "";
        ChessBoard board = new ChessBoard(start_pos_fen);

        foreach (var move in moves)
        {
            res += mg.PrintMove(move, board) + " ";
            board.MakeMove(move);
        }

        return res;
    }

    public string
    GetMovesEval()
    {
        string res = "";

        foreach (var eval in evals)
            res += eval.ToString() + " ";

        return res;
    }

    public int
    MoveCount()
    {
        return moves.Count;
    }

    public string StartFen()
    { return start_pos_fen; }
}


class ArenaScoreSheet
{
    private string engine1;
    private string engine2;

    private float time_per_game;
    private float time_increment;
    private bool  fixed_time_per_move;

    private int prediction_attempt;
    private int prediction_success;

    private int engine1_loss_on_time;
    private int engine2_loss_on_time;

    List<int> results;


    public ArenaScoreSheet(string __engine1, string __engine2,
        float __time_per_game, float __time_increment, bool __fixed_time_per_move)
    {
        engine1 = __engine1;
        engine2 = __engine2;

        time_per_game = __time_per_game;
        time_increment = __time_increment;
        fixed_time_per_move = __fixed_time_per_move;

        prediction_attempt = prediction_success = 0;
        engine1_loss_on_time = engine2_loss_on_time = 0;
        results = new List<int>();

        string file_path = Application.streamingAssetsPath + "/arena/results_log.csv";
        File.WriteAllText(file_path, "GameNo, Result, WhitePlayer, BlackPlayer, MoveCount, Remarks\n");
    }


    public void
    Add(int result, int prediction, int state)
    {
        results.Add(result);

        if (prediction != 0)
        {
            prediction_attempt++;
            if (prediction == result)
                prediction_success++;
        }

        if (results.Count % 2 == 1)
        {
            if (state == 7)
                engine2_loss_on_time++;
            if (state == 8)
                engine1_loss_on_time++;
        }
        else
        {
            if (state == 7)
                engine1_loss_on_time++;
            if (state == 8)
                engine2_loss_on_time++;
        }
    }


    private (int, int)
    CalculateWins(int win_value)
    {
        int count1 = 0, count2 = 0;

        for (int i = 0; i < results.Count; i += 2)
            if (results[i] == win_value) count1++;
        
        for (int i = 1; i < results.Count; i += 2)
            if (results[i] == -win_value) count2++;
        
        return (count1, count2);
    }


    public void
    PrintArenaResult()
    {
        var ( e1_wins_w,  e1_wins_b) = CalculateWins(1);
        var ( e2_wins_b,  e2_wins_w) = CalculateWins(-1);
        var (e1_draws_w, e1_draws_b) = CalculateWins(0);

        int  e1_wins_t =  e1_wins_w +  e1_wins_b;
        int  e2_wins_t =  e2_wins_w +  e2_wins_b;
        int e1_draws_t = e1_draws_w + e1_draws_b;

        string file_path = Application.streamingAssetsPath + "/arena/results.txt";

        string time_control = fixed_time_per_move
            ? $"Fixed {time_per_game:0.##}s per move"
            : $"{time_per_game:0.##}s + {time_increment:0.##}s increment";

        int name_width = System.Math.Max(engine1.Length, engine2.Length);

        string sep = new string('=', 50);

        File.WriteAllText(file_path,
            sep + "\n"
            + "                ARENA RESULTS\n"
            + sep + "\n"
            + engine1 + "  vs  " + engine2 + "\n\n"
            + "Time Control : " + time_control + "\n"
            + "Games Played : " + results.Count.ToString() + "\n\n"
            + "             Wins   Draws   Losses\n"
            + string.Format("   White : {0,4}    {1,4}    {2,4}\n",  e1_wins_w, e1_draws_w, e2_wins_b)
            + string.Format("   Black : {0,4}    {1,4}    {2,4}\n",  e1_wins_b, e1_draws_b, e2_wins_w)
            + string.Format("   Total : {0,4}    {1,4}    {2,4}\n\n", e1_wins_t, e1_draws_t, e2_wins_t)
            + "Prediction Accuracy : " + prediction_success.ToString() + "/" + prediction_attempt.ToString() + "\n\n"
            + "Losses on Time\n"
            + "   " + engine1.PadRight(name_width) + " : " + engine1_loss_on_time.ToString() + "\n"
            + "   " + engine2.PadRight(name_width) + " : " + engine2_loss_on_time.ToString() + "\n"
            + sep + "\n"
        );
    }


    public string
    GeneratePgnHeader(int game_no, int result, string fen)
    {
        string white = game_no % 2 == 1 ? engine1 : engine2;
        string black = game_no % 2 == 0 ? engine1 : engine2;

        string event_name = engine1 + " vs " + engine2 + " Unit Testing";

        System.DateTime theTime = System.DateTime.Now;
        string date_string = theTime.Year + "." + theTime.Month + "." + theTime.Day;

        string result_string = "";

        if (result == 1)
            result_string = "1-0";
        else if (result == -1)
            result_string = "0-1";
        else
            result_string = "1/2-1/2";

        return
            "[Event \""  + event_name + "\"]\n"
          + "[Site \"?\"]\n"
          + "[Date \""   + date_string + "\"]\n"
          + "[Round \""  + game_no.ToString()  + "\"]\n"
          + "[White \""  + white  + "\"]\n"
          + "[Black \""  + black  + "\"]\n"
          + "[Result \"" + result_string + "\"]\n"
          + "[FEN \""    + fen + "\"]\n\n";
    }


    public string
    GenerateCsvLine(int game_no, int result, int movecount, string remark)
    {
        string csv_line = "";
        csv_line = game_no.ToString() + ", ";

        if (result == 1)
            csv_line += "1-0, ";
        else if (result == -1)
            csv_line += "0-1, ";
        else
            csv_line += "1/2-1/2, ";

        string white = game_no % 2 == 1 ? engine1 : engine2;
        string black = game_no % 2 == 0 ? engine1 : engine2;

        csv_line += white + ", " + black + ", ";

        int m = (movecount + 1) / 2;
        csv_line += m.ToString() + ", " + remark;

        return csv_line;
    }


    public void
    PrintArenaResultLog(int game_no, int result, int movecount, string remark)
    {
        string file_path = Application.streamingAssetsPath + "/arena/results_log.csv";

        string csv_line = "";
        csv_line = game_no.ToString() + ", ";

        if (result == 1)
            csv_line += "1-0, ";
        else if (result == -1)
            csv_line += "0-1, ";
        else
            csv_line += "1/2-1/2, ";

        string white = game_no % 2 == 1 ? engine1 : engine2;
        string black = game_no % 2 == 0 ? engine1 : engine2;

        csv_line += white + ", " + black + ", ";

        int m = (movecount + 1) / 2;
        csv_line += m.ToString() + ", " + remark + "\n";

        File.AppendAllText(file_path, csv_line);
    }
}

