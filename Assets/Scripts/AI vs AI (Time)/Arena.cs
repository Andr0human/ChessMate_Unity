using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;


public class Arena : MonoBehaviour
{
    [SerializeField] private MatchManager mm;

    public  int    GamesToPlay = 10;
    private int CurrentGameNum = 1;

    [HideInInspector] public float FixedTimePerGame = 30f;
    [HideInInspector] public float IncrementPerGame =  1f;

    public bool fixedTimePerMove = false;

    public TextMeshProUGUI CurrentGameNumText;
    public TextMeshProUGUI  RemainingTimeText;

    public string[] ArenaEngines;
    public string OpeningsFilePath;

    private ArenaScoreSheet ScoreSheet;
    private Stopwatch sw;


    private void
    PrintGame(int result)
    {
        string dir_games = Application.streamingAssetsPath + "/arena/Games/";
        string dir_evals = Application.streamingAssetsPath + "/arena/Evals/";
        string game_no = CurrentGameNum.ToString();

        // Create the directory if it does not exist
        if (!Directory.Exists(dir_games))
            Directory.CreateDirectory(dir_games);

        string path_pgn  = dir_games + "game" + game_no + ".pgn";
        string played_moves = mm.Data.GetMoveList(mm.mg);
        string pgn_header = ScoreSheet.GeneratePgnHeader(CurrentGameNum, result, mm.Data.StartFen());

        string pgn = pgn_header + played_moves;
        File.WriteAllText(path_pgn , pgn);
    }


    private string
    GameRemark(int result, int state, int prediction)
    {
        string remark = "";

        // Prediction made and failed
        if ((prediction != 0) && (result != prediction))
            remark += "win-loss prediction-failed | ";

        // If huge evaluation difference in more than 5 places in a game
        if (mm.Data.DifferentEvalCount(3f) > 5)
            remark += "eval-diff | ";
        
        // Game ended with huge material on board
        int weight_cutoff = 4000;
        if (mm.BoardPosition.PositionWeight() > weight_cutoff)
            remark += "huge material | ";

        if (state == 5)
            remark += "draw by 3-move repetition | ";

        if ((state == 7) || (state == 8))
            remark += "lost on time | ";

        if (remark.Length > 0)
            remark = remark.Substring(0, remark.Length - 3);

        return remark;
    }


    private int
    GetResultFromState(int state, int prediction)
    {
        // Game ended normally (one side wins)
        if (state == 1 || state == 7) return  1;
        if (state == 2 || state == 8) return -1;
        
        // Game ends in a draws
        return 0;
    }


    private void
    DisplayEstimatedTime()
    {
        double avg_game_time = sw.Elapsed.TotalSeconds / CurrentGameNum;
        double est_time = avg_game_time * (GamesToPlay - CurrentGameNum);

        int seconds = (int)est_time;
        int hours = seconds / 3600;
        int minutes = (seconds % 3600) / 60;
        int remainingSeconds = seconds % 60;

        string timeString = $"{hours} hr, {minutes} min, {remainingSeconds} secs";
        RemainingTimeText.text = timeString;
    }


    private void
    UpdateArenaElements(int s2s, int end_state, int prediction)
    {
        int end_result = GetResultFromState(end_state, prediction);

        // Update Wins, Loss, Draw
        ScoreSheet.Add(end_result, prediction, end_state);

        // Display time to complete all remaining games
        DisplayEstimatedTime();

        // Print Game pgn if found interesting
        PrintGame(end_result);

        // Print Results when new game pair starts
        if (s2s == 1)
        {
            ScoreSheet.PrintArenaResult();
        }
        ScoreSheet.PrintArenaResultLog(CurrentGameNum, end_result,
            mm.Data.MoveCount(), GameRemark(end_result, end_state, prediction));
    }


    public void
    InitArena()
    {
        GameObject.FindAnyObjectByType<OpeningBook>().GetOpeningLines(OpeningsFilePath);

        string dir_arena = Application.streamingAssetsPath + "/arena/";
        if (!Directory.Exists(dir_arena))
            Directory.CreateDirectory(dir_arena);

        ScoreSheet = new ArenaScoreSheet(ArenaEngines[0], ArenaEngines[1],
            FixedTimePerGame, IncrementPerGame, fixedTimePerMove);
        sw = new Stopwatch();

        CurrentGameNumText.gameObject.SetActive(true);
        RemainingTimeText.gameObject.SetActive(true);

        StartCoroutine( PlayArena() );
    }


    public IEnumerator
    PlayArena()
    {
        string opening_moves = "";
        int side2start = 0;
        sw.Reset();
        sw.Start();

        while (CurrentGameNum <= GamesToPlay)
        {
            // Set Current Game Number Text on Board
            CurrentGameNumText.text = "Game Number : " + CurrentGameNum.ToString();

            if (side2start == 0)
                opening_moves = FindAnyObjectByType<OpeningBook>().NextOpening();

            GameObject.FindAnyObjectByType<Timer>().SetTime(FixedTimePerGame, IncrementPerGame);

            // Play Current Match
            yield return StartCoroutine( mm.StartNewGame(
                ArenaEngines[side2start], ArenaEngines[side2start ^ 1], opening_moves,
                fixedTimePerMove, false,
                Application.streamingAssetsPath + "/arena", CurrentGameNum
            ));

            UpdateArenaElements(side2start, mm.EndState, mm.EndPrediction);

            // To next game
            CurrentGameNum++;
            side2start ^= 1;

            // Wait before starting next game
            yield return new WaitForSeconds(1.5f);
        }

        // All games ended
        CurrentGameNumText.text = "Games completed!";
        sw.Stop();
    }
}

