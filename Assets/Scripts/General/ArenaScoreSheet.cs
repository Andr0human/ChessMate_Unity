using UnityEngine;
using System.IO;
using System.Collections.Generic;


public class ArenaScoreSheet
{
    private string engine1;
    private string engine2;

    private float timePerGame;
    private float timeIncrement;
    private bool  fixedTimePerMove;

    private int predictionAttempt;
    private int predictionSuccess;

    private int engine1LossOnTime;
    private int engine2LossOnTime;

    private List<int> results;

    public string Engine1Name => engine1;
    public string Engine2Name => engine2;
    public int    GamesPlayed => results.Count;
    public int    Engine1Wins { get { int n = 0; foreach (var r in TallyByPair( 1)) n += r; return n; } }
    public int    Engine2Wins { get { int n = 0; foreach (var r in TallyByPair(-1)) n += r; return n; } }
    public int    Draws       { get { int n = 0; foreach (var r in results) if (r == 0) n++; return n; } }

    private int[] TallyByPair(int winValue)
    {
        var (a, b) = CalculateWins(winValue);
        return new int[] { a, b };
    }


    public ArenaScoreSheet(string name1, string name2,
        float timePerGame, float timeIncrement, bool fixedTimePerMove)
    {
        engine1 = name1;
        engine2 = name2;

        this.timePerGame = timePerGame;
        this.timeIncrement = timeIncrement;
        this.fixedTimePerMove = fixedTimePerMove;

        predictionAttempt = predictionSuccess = 0;
        engine1LossOnTime = engine2LossOnTime = 0;
        results = new List<int>();

        string filePath = Application.streamingAssetsPath + "/arena/results_log.csv";
        File.WriteAllText(filePath, "GameNo, Result, WhitePlayer, BlackPlayer, MoveCount, Remarks\n");
    }


    public void
    Add(int result, int prediction, GameEndState state)
    {
        results.Add(result);

        if (prediction != 0)
        {
            predictionAttempt++;
            if (prediction == result)
                predictionSuccess++;
        }

        if (results.Count % 2 == 1)
        {
            if (state == GameEndState.WhiteWinsOnTime)
                engine2LossOnTime++;
            if (state == GameEndState.BlackWinsOnTime)
                engine1LossOnTime++;
        }
        else
        {
            if (state == GameEndState.WhiteWinsOnTime)
                engine1LossOnTime++;
            if (state == GameEndState.BlackWinsOnTime)
                engine2LossOnTime++;
        }
    }


    private (int, int)
    CalculateWins(int winValue)
    {
        int count1 = 0, count2 = 0;

        for (int i = 0; i < results.Count; i += 2)
            if (results[i] == winValue) count1++;

        for (int i = 1; i < results.Count; i += 2)
            if (results[i] == -winValue) count2++;

        return (count1, count2);
    }


    public void
    PrintArenaResult()
    {
        var ( e1WinsW,  e1WinsB) = CalculateWins(1);
        var ( e2WinsB,  e2WinsW) = CalculateWins(-1);
        var (e1DrawsW, e1DrawsB) = CalculateWins(0);

        int  e1WinsT =  e1WinsW +  e1WinsB;
        int  e2WinsT =  e2WinsW +  e2WinsB;
        int e1DrawsT = e1DrawsW + e1DrawsB;

        string filePath = Application.streamingAssetsPath + "/arena/results.txt";

        string timeControl = fixedTimePerMove
            ? $"Fixed {timePerGame:0.##}s per move"
            : $"{timePerGame:0.##}s + {timeIncrement:0.##}s increment";

        int nameWidth = System.Math.Max(engine1.Length, engine2.Length);

        string sep = new string('=', 50);

        File.WriteAllText(filePath,
            sep + "\n"
            + "                ARENA RESULTS\n"
            + sep + "\n"
            + engine1 + "  vs  " + engine2 + "\n\n"
            + "Time Control : " + timeControl + "\n"
            + "Games Played : " + results.Count.ToString() + "\n\n"
            + "             Wins   Draws   Losses\n"
            + string.Format("   White : {0,4}    {1,4}    {2,4}\n",  e1WinsW, e1DrawsW, e2WinsB)
            + string.Format("   Black : {0,4}    {1,4}    {2,4}\n",  e1WinsB, e1DrawsB, e2WinsW)
            + string.Format("   Total : {0,4}    {1,4}    {2,4}\n\n", e1WinsT, e1DrawsT, e2WinsT)
            + "Prediction Accuracy : " + predictionSuccess.ToString() + "/" + predictionAttempt.ToString() + "\n\n"
            + "Losses on Time\n"
            + "   " + engine1.PadRight(nameWidth) + " : " + engine1LossOnTime.ToString() + "\n"
            + "   " + engine2.PadRight(nameWidth) + " : " + engine2LossOnTime.ToString() + "\n"
            + sep + "\n"
        );
    }


    public string
    GeneratePgnHeader(int gameNo, int result, string fen)
    {
        string white = gameNo % 2 == 1 ? engine1 : engine2;
        string black = gameNo % 2 == 0 ? engine1 : engine2;

        string eventName = engine1 + " vs " + engine2 + " Unit Testing";

        System.DateTime theTime = System.DateTime.Now;
        string dateString = theTime.Year + "." + theTime.Month + "." + theTime.Day;

        string resultString = "";

        if (result == 1)
            resultString = "1-0";
        else if (result == -1)
            resultString = "0-1";
        else
            resultString = "1/2-1/2";

        return
            "[Event \""  + eventName + "\"]\n"
          + "[Site \"?\"]\n"
          + "[Date \""   + dateString + "\"]\n"
          + "[Round \""  + gameNo.ToString()  + "\"]\n"
          + "[White \""  + white  + "\"]\n"
          + "[Black \""  + black  + "\"]\n"
          + "[Result \"" + resultString + "\"]\n"
          + "[FEN \""    + fen + "\"]\n\n";
    }


    public void
    PrintArenaResultLog(int gameNo, int result, int moveCount, string remark)
    {
        string filePath = Application.streamingAssetsPath + "/arena/results_log.csv";

        string csvLine = "";
        csvLine = gameNo.ToString() + ", ";

        if (result == 1)
            csvLine += "1-0, ";
        else if (result == -1)
            csvLine += "0-1, ";
        else
            csvLine += "1/2-1/2, ";

        string white = gameNo % 2 == 1 ? engine1 : engine2;
        string black = gameNo % 2 == 0 ? engine1 : engine2;

        csvLine += white + ", " + black + ", ";

        int m = (moveCount + 1) / 2;
        csvLine += m.ToString() + ", " + remark + "\n";

        File.AppendAllText(filePath, csvLine);
    }
}
