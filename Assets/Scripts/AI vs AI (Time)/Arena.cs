using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;


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

    // Ply currently shown while in review mode. Driven by move-list clicks
    // and Left/Right arrow keyboard navigation.
    private int  reviewPly = 0;


    // Clamps to [0, total], snaps the board to that ply, and updates the HUD
    // (grey overlay / LIVE pill). Shared by move-list clicks and arrow keys.
    private void
    SeekReview(int ply)
    {
        int total = mm.Data.MoveCount();
        reviewPly = Mathf.Clamp(ply, 0, total);
        mm.SeekToPly(reviewPly);
        if (Hud != null)
        {
            Hud.SetReviewPly(reviewPly, total);
            Hud.SetCurrentMove(reviewPly);
        }
    }


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
    GameRemark(int result, GameEndState state, int prediction)
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

        if (state == GameEndState.DrawByRepetition)
            remark += "draw by 3-move repetition | ";

        if ((state == GameEndState.WhiteWinsOnTime) || (state == GameEndState.BlackWinsOnTime))
            remark += "lost on time | ";

        if (remark.Length > 0)
            remark = remark.Substring(0, remark.Length - 3);

        return remark;
    }


    private int
    GetResultFromState(GameEndState state)
    {
        // Game ended normally (one side wins)
        if (state == GameEndState.WhiteWinsByCheckmate || state == GameEndState.WhiteWinsOnTime) return  1;
        if (state == GameEndState.BlackWinsByCheckmate || state == GameEndState.BlackWinsOnTime) return -1;

        // Game ends in a draw
        return 0;
    }


    private void
    DisplayEstimatedTime()
    {
        double avg_game_time = sw.Elapsed.TotalSeconds / CurrentGameNum;
        double est_time = avg_game_time * (GamesToPlay - CurrentGameNum);

        if (Hud != null) Hud.SetEta(TimeFormat.Verbose(est_time));
    }


    private (int result, string remark)
    UpdateArenaElements(int s2s, GameEndState end_state, int prediction)
    {
        int end_result = GetResultFromState(end_state);
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
            Hud.OnMoveLinkClicked = (ply) => SeekReview(ply);
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
                Hud.ShowGameResult(mm.EndState.Describe(), remark);

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
                    int totalPlies = mm.Data.MoveCount();
                    Hud.BeginReview();
                    // Start review on the final position with the last move
                    // highlighted in the move list.
                    SeekReview(totalPlies);

                    // Wait for Continue, polling Left/Right arrows each frame
                    // to step the reviewed position one ply at a time.
                    continueRequested = false;
                    while (!continueRequested)
                    {
                        var kb = Keyboard.current;
                        if (kb != null)
                        {
                            if (kb.leftArrowKey.wasPressedThisFrame)
                                SeekReview(reviewPly - 1);
                            else if (kb.rightArrowKey.wasPressedThisFrame)
                                SeekReview(reviewPly + 1);
                        }
                        yield return null;
                    }

                    Hud.EndReview();
                    // Snap board back to final pos in case user scrubbed.
                    mm.SeekToPly(totalPlies);
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

            string time = TimeFormat.Verbose(sw.Elapsed.TotalSeconds);

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

