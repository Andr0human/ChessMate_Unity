using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;


public class Arena : MonoBehaviour
{
    [SerializeField] private MatchManager mm;

    public  int    GamesToPlay = 10;
    private int CurrentGameNum = 1;

    [HideInInspector] public float FixedTimePerGame = 30f;
    [HideInInspector] public float IncrementPerGame =  1f;

    public bool fixedTimePerMove = false;

    [SerializeField] private ArenaHud Hud;

    // Time the Review button stays clickable before the arena auto-advances
    // to the next game. Click during this window to enter review mode.
    [SerializeField] private float ReviewCountdownSeconds = 1.5f;

    public string[] ArenaEngines;
    public string OpeningsFilePath;

    private ArenaScoreSheet ScoreSheet;
    private Stopwatch sw;

    private bool continueRequested = false;
    private bool reviewClicked = false;
    private int  anomalyCount = 0;


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

        if (Hud != null) Hud.SetEta(timeString);
    }


    private (int result, string remark)
    UpdateArenaElements(int s2s, int end_state, int prediction)
    {
        int end_result = GetResultFromState(end_state, prediction);
        string remark  = GameRemark(end_result, end_state, prediction);

        if (!string.IsNullOrEmpty(remark)) anomalyCount++;

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
            mm.Data.MoveCount(), remark);

        if (Hud != null)
        {
            Hud.SetScore(ScoreSheet.Engine1Name,
                         ScoreSheet.Engine1Wins, ScoreSheet.Engine2Wins,
                         ScoreSheet.Draws,
                         ScoreSheet.Engine2Name);
            Hud.SetMoveList(mm.Data.GetMoveList(mm.mg));
            Hud.AppendAnomaly(CurrentGameNum, end_result, remark);
        }

        return (end_result, remark);
    }


    private static string
    StateLabel(int state)
    {
        switch (state)
        {
            case 1: return "White wins by checkmate";
            case 2: return "Black wins by checkmate";
            case 3: return "Draw by stalemate";
            case 4: return "Draw by insufficient material";
            case 5: return "Draw by 3-fold repetition";
            case 6: return "Draw by 50-move rule";
            case 7: return "White wins on time";
            case 8: return "Black wins on time";
            default: return "Game over";
        }
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

        // Per-game result shows in the HUD result card, not the shared
        // EndScreen — keeps the left-of-board space clear for the eval bar.
        mm.SuppressEndScreen = true;

        if (Hud != null)
        {
            Hud.ShowLive(true);
            Hud.ResetAnomalies();
            Hud.SetRound(1, GamesToPlay);
            Hud.SetScore(ScoreSheet.Engine1Name, 0, 0, 0, ScoreSheet.Engine2Name);
            Hud.SetEngineLabels(ArenaEngines[0], ArenaEngines[1]);
            Hud.SetMoveList("");

            // Live update during a game: each played move pushes the move list.
            mm.OnMoveMade = () => Hud.SetMoveList(mm.Data.GetMoveList(mm.mg));

            // Post-game review wiring.
            Hud.OnContinueClicked = () => continueRequested = true;
            Hud.OnReviewClicked   = () => reviewClicked    = true;
            Hud.OnMoveLinkClicked = (ply) =>
            {
                mm.SeekToPly(ply);
                Hud.SetReviewPly(ply, mm.Data.MoveCount());
            };
            Hud.OnOpenPgnClicked = () =>
            {
                string games = Application.streamingAssetsPath + "/arena/Games/";
                if (!Directory.Exists(games)) Directory.CreateDirectory(games);
                Application.OpenURL("file://" + games);
            };
        }

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
            if (Hud != null)
            {
                Hud.SetRound(CurrentGameNum, GamesToPlay);
                Hud.SetEngineLabels(ArenaEngines[side2start], ArenaEngines[side2start ^ 1]);
            }

            if (side2start == 0)
                opening_moves = FindAnyObjectByType<OpeningBook>().NextOpening();

            GameObject.FindAnyObjectByType<Timer>().SetTime(FixedTimePerGame, IncrementPerGame);

            // Play Current Match
            yield return StartCoroutine( mm.StartNewGame(
                ArenaEngines[side2start], ArenaEngines[side2start ^ 1], opening_moves,
                fixedTimePerMove, false,
                Application.streamingAssetsPath + "/arena", CurrentGameNum
            ));

            var (_, remark) =
                UpdateArenaElements(side2start, mm.EndState, mm.EndPrediction);

            // Inter-game pause. With review UI wired: pop the per-game result
            // card with a Review button ticking down for ReviewCountdownSeconds;
            // if clicked, enter full review mode (gated on a Continue click).
            // Otherwise the card auto-dismisses — keeps unattended 1000-game
            // runs zero-friction.
            if (Hud != null && Hud.ReviewSupported)
            {
                Hud.ShowGameResult(StateLabel(mm.EndState), remark);

                reviewClicked = false;
                float remaining = ReviewCountdownSeconds;
                Hud.BeginCountdown(remaining);
                while (remaining > 0f && !reviewClicked)
                {
                    remaining -= Time.deltaTime;
                    Hud.SetCountdownLabel(Mathf.Max(0f, remaining));
                    yield return null;
                }
                Hud.EndCountdown();
                Hud.HideResultCard();

                if (reviewClicked)
                {
                    Hud.BeginReview();
                    Hud.SetReviewPly(mm.Data.MoveCount(), mm.Data.MoveCount());

                    continueRequested = false;
                    yield return new WaitUntil(() => continueRequested);

                    Hud.EndReview();
                    // Snap board back to final pos in case user scrubbed.
                    mm.SeekToPly(mm.Data.MoveCount());
                }
            }
            else
            {
                yield return new WaitForSeconds(1.5f);
            }

            // To next game
            CurrentGameNum++;
            side2start ^= 1;
        }

        // All games ended
        sw.Stop();

        if (Hud != null)
        {
            Hud.SetRound(GamesToPlay, GamesToPlay);

            int secs    = (int)sw.Elapsed.TotalSeconds;
            string time = $"{secs / 3600} hr, {(secs % 3600) / 60} min, {secs % 60} secs";

            string summary =
                "Tournament complete\n\n" +
                $"{ScoreSheet.Engine1Name}  {ScoreSheet.Engine1Wins} — " +
                $"{ScoreSheet.Engine2Wins}  {ScoreSheet.Engine2Name}\n" +
                $"{ScoreSheet.Draws} draw(s)\n" +
                $"Total time: {time}\n" +
                $"Anomalies: {anomalyCount}";

            Hud.ShowSummary(summary);
        }
    }
}

