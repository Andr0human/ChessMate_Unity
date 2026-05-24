using UnityEngine;
using System.IO;
using System.Collections.Generic;


public class ArenaScoreSheet
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

    private List<int> results;

    public string Engine1Name => engine1;
    public string Engine2Name => engine2;
    public int    GamesPlayed => results.Count;
    public int    Engine1Wins { get { int n = 0; foreach (var r in TallyByPair( 1)) n += r; return n; } }
    public int    Engine2Wins { get { int n = 0; foreach (var r in TallyByPair(-1)) n += r; return n; } }
    public int    Draws       { get { int n = 0; foreach (var r in results) if (r == 0) n++; return n; } }

    private int[] TallyByPair(int win_value)
    {
        var (a, b) = CalculateWins(win_value);
        return new int[] { a, b };
    }


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
    Add(int result, int prediction, GameEndState state)
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
            if (state == GameEndState.WhiteWinsOnTime)
                engine2_loss_on_time++;
            if (state == GameEndState.BlackWinsOnTime)
                engine1_loss_on_time++;
        }
        else
        {
            if (state == GameEndState.WhiteWinsOnTime)
                engine1_loss_on_time++;
            if (state == GameEndState.BlackWinsOnTime)
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
