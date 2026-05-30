using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class MatchManager : MonoBehaviour
{
    [SerializeField] private Timer tmr;
    [SerializeField] private BoardHandler bh;
    [SerializeField] private OpeningBook ob;

    [SerializeField] private GameObject EndScreen;

    // Arena suppresses the shared EndScreen (result text + its Continue
    // button) so the per-game result shows in ArenaHud's result card instead,
    // freeing the left-of-board space for the eval bar.
    [HideInInspector] public bool SuppressEndScreen = false;

    // Optional per-side eval display (used by the Arena scene; left unassigned
    // in Player-vs-AI). Index 0 = first player (white at game start), 1 = second.
    [SerializeField] private TMPro.TextMeshProUGUI[] PlayerEvalTexts = new TMPro.TextMeshProUGUI[2];

    // Optional Arena HUD ref. When assigned, eval updates also drive the
    // per-side eval-pill background colour and active-side card alpha.
    [SerializeField] private ArenaHud arenaHud;

    private string[] PlayerNames = new string[2];

    [HideInInspector] public ChessBoard BoardPosition;
    [HideInInspector] public  MatchData Data;
    [HideInInspector] public  IPlayer[] Players;

    [HideInInspector] public int          Side2Move;
    [HideInInspector] public GameEndState  EndState;
    [HideInInspector] public int      EndPrediction;

    // Fired after each played move is applied to the board + Data.
    // Arena subscribes to push the live move list to its HUD; single-player
    // leaves it null.
    public System.Action OnMoveMade;

    private float AdjournWinMargin = 5.0f;

    private string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";


    private void
    Start()
    {
        TT.Init();

        BoardPosition = new ChessBoard(startFen);
        bh.InitializeBoard(ref BoardPosition);
        Players = new IPlayer[2];
    }


    #region MATCH_UTILS


    private IEnumerator
    PlayOpening(string opening)
    {
        if (ob.IsFen(opening))
        {
            Data = new MatchData(opening);
            BoardPosition = new ChessBoard(opening);

            bh.BoardReset();
            bh.Recreate(ref BoardPosition);

            Side2Move = BoardPosition.color ^ 1;

            yield return new WaitForSeconds(0.3f);
        }
        else
        {
            Data = new MatchData(startFen);
            List<int> openingLine = ob.ExtractLine(opening);
            float timeLeft = tmr.AllotedTimePerSide;

            // Play all moves of the opening line
            foreach (int move in openingLine)
            {
                ulong prevHash = BoardPosition.hashvalue;
                BoardPosition.MakeMove(move);
                Data.Add(move, 0, timeLeft, prevHash);

                bh.Recreate(ref BoardPosition);
                yield return new WaitForSeconds(0.1f);

                Side2Move ^= 1;
            }

            Data.BookMoveCount = openingLine.Count;
        }
    }


    public GameEndState
    IsGameOver()
    {
        MoveList moveslist = MoveGenerator.GenerateMoves(ref BoardPosition);

        // checkmate/stalemate check — the side to move has no legal reply.
        if (moveslist.moveCount == 0)
        {
            if (moveslist.KingAttackers == 0)
                return GameEndState.DrawByStalemate;
            return (Side2Move == 0) ? GameEndState.BlackWinsByCheckmate
                                    : GameEndState.WhiteWinsByCheckmate;
        }

        // Insufficient material check
        if (InsufficientMaterial(ref BoardPosition)) return GameEndState.DrawByInsufficientMaterial;

        // 3-fold repetition and 50-move-rule
        if (Data.ThreeMoveRepetitionDraw()) return GameEndState.DrawByRepetition;
        if (Data.FiftyMoveRuleDraw()) return GameEndState.DrawByFiftyMoveRule;

        // Check if lost on time
        if (tmr.ChessClocks[Side2Move] < 0f)
            return (Side2Move == 0) ? GameEndState.BlackWinsOnTime
                                    : GameEndState.WhiteWinsOnTime;

        return GameEndState.Ongoing;
    }


    public bool
    TimeLeftForSearch()
    {
        return tmr.ChessClocks[Side2Move] > 0f;
    }


    public bool
    InsufficientMaterial(ref ChessBoard pos)
    {
        int w = 8, b = 0;

        int wPawns   = pos.PopCount(pos.Pawn(w))  , bPawns   = pos.PopCount(pos.Pawn(b));
        int wBishops = pos.PopCount(pos.Bishop(w)), bBishops = pos.PopCount(pos.Bishop(b));
        int wKnights = pos.PopCount(pos.Knight(w)), bKnights = pos.PopCount(pos.Knight(b));
        int wRooks   = pos.PopCount(pos.Rook(w))  , bRooks   = pos.PopCount(pos.Rook(b));
        int wQueens  = pos.PopCount(pos.Queen(w)) , bQueens  = pos.PopCount(pos.Queen(b));

        int wPieces = wBishops + wKnights + wRooks + wQueens;
        int bPieces = bBishops + bKnights + bRooks + bQueens;

        if (wPawns + wPieces + bPawns + bPieces == 0) return true;
        if (wPawns > 0 || bPawns > 0) return false;

        if (wPieces == 1 && bPieces == 0)
            if (wBishops == 1 || wKnights == 1) return true;
        if (wPieces == 0 && bPieces == 1)
            if (bBishops == 1 || bKnights == 1) return true;

        if (wPieces == 1 && bPieces == 1)
            if ((wBishops == 1 || wKnights == 1) && (bBishops == 1 || bKnights == 1)) return true;

        if (wPieces + bPieces == 2)
            if (wKnights == 2 || bKnights == 2) return true;

        return false;
    }


    private void
    OnApplicationQuit()
    {
        if (Players[0] != null) Players[0].Stop();
        if (Players[1] != null) Players[1].Stop();

        Application.Quit();
    }

    #endregion


    private static string
    BuildSearchLogPath(string dir, string engineName, int gameNumber)
    {
        if (string.IsNullOrEmpty(dir) || engineName == "human") return null;
        return dir + "/logs_" + engineName + "~/game_" + gameNumber + ".log";
    }


    public IEnumerator
    StartNewGame(string playerWhite, string playerBlack, string opening,
                 bool fixedTimePerMove, bool allowOpeningBook,
                 string searchLogDir = null, int searchLogGameNumber = 0)
    {
        // Reset game data and board position
        Side2Move = 0;
        BoardPosition = new ChessBoard(startFen);
        Data = new MatchData(startFen);

        // Reset Match Parameters
        EndState = GameEndState.Ongoing;
        EndPrediction = 0;

        EndScreen.SetActive(false);

        PlayerNames[0] = playerWhite;
        PlayerNames[1] = playerBlack;
        ResetEvalDisplays();

        // Play the opening moves, if any
        if (opening.Length > 0)
            yield return StartCoroutine(PlayOpening(opening));

        // Create players
        string whiteLog = BuildSearchLogPath(searchLogDir, playerWhite, searchLogGameNumber);
        string blackLog = BuildSearchLogPath(searchLogDir, playerBlack, searchLogGameNumber);

        Players[0] = (playerWhite == "human")
                      ? new HumanPlayer()
                      : new ChessEngine(playerWhite, fixedTimePerMove, allowOpeningBook, whiteLog);

        Players[1] = (playerBlack == "human")
                      ? new HumanPlayer()
                      : new ChessEngine(playerBlack, fixedTimePerMove, allowOpeningBook, blackLog);

        yield return new WaitForSeconds(1);

        // Initialize timer and start the game
        tmr.Init(Side2Move);
        yield return StartCoroutine( PlayGame() );
    }


    private IEnumerator
    PlayGame()
    {
        while ((EndState = IsGameOver()) == GameEndState.Ongoing)
        {
            // Let player make his move
            yield return StartCoroutine( RequestMove() ) ;

            // Time runs out before player making a move
            if (TimeLeftForSearch() == false)
                break;

            // Retrieve player move
            var (move, eval) = Players[Side2Move].GetResults();

            // If no prediction made so far
            if (EndPrediction == 0)
                EndPrediction = PredictionCall();

            // Update board elements after making move
            yield return StartCoroutine( UpdateBoardElements(move, eval) );

            // Switch sides and next turn
            Side2Move ^= 1;
        }

        tmr.ClockFreeze();
        GameOverScreen(EndState);

        if (Players[0] != null) Players[0].Stop();
        if (Players[1] != null) Players[1].Stop();
    }


    public IEnumerator
    UpdateBoardElements(int move, float eval)
    {
        tmr.SwitchPlayer();
        tmr.ClockFreeze();

        BoardPosition.MakeMove(move);
        Data.Add(move, eval, tmr.ChessClocks[Side2Move ^ 1],
            BoardPosition.GenerateHashKey() );

        UpdateEvalDisplay(Side2Move, eval);

        OnMoveMade?.Invoke();

        // Board Update
        bh.BoardReset(false);
        bh.MarkPlayedMove(move);
        bh.Recreate(ref BoardPosition);

        // Give a slight delay after each board update
        yield return new WaitForSeconds(0.1f);

        tmr.ClockUnfreeze();
    }


    public IEnumerator
    RequestMove()
    {
        bool moveFound = false;
        bool timeLeftForSearch = true;

        // Start the player's move calculation
        StartCoroutine( Players[Side2Move].Play( BoardPosition, Data.LastPlayedMove() ) );

        // Wait until the move is found or no time is left
        yield return new WaitUntil(() =>
        {
            moveFound = Players[Side2Move].MoveFound();
            timeLeftForSearch = TimeLeftForSearch();
            return moveFound || !timeLeftForSearch;
        });

        if (!timeLeftForSearch) {
            EndState = (Side2Move == 0) ? GameEndState.BlackWinsOnTime
                                        : GameEndState.WhiteWinsOnTime;
            Players[Side2Move].StopReadOutput();
        }
    }


    private void
    GameOverScreen(GameEndState state)
    {
        if (SuppressEndScreen) return;

        EndScreen.GetComponent<TMPro.TextMeshProUGUI>().text = state.Describe() + "!";
        EndScreen.SetActive(true);
    }


    private void
    ResetEvalDisplays()
    {
        if (PlayerEvalTexts == null) return;
        for (int i = 0; i < PlayerEvalTexts.Length; i++)
        {
            if (PlayerEvalTexts[i] == null) continue;
            PlayerEvalTexts[i].text = "—";
        }
    }


    private void
    UpdateEvalDisplay(int side, float eval)
    {
        if (PlayerEvalTexts == null || side >= PlayerEvalTexts.Length) return;
        var tmp = PlayerEvalTexts[side];
        if (tmp == null) return;

        tmp.text = string.Format("{0:+0.00;-0.00; 0.00}", eval);

        if (arenaHud != null)
        {
            // Side2Move still holds the just-moved side here; the next player is the opposite.
            arenaHud.SetActiveSide(Side2Move ^ 1);
        }
    }


    // Rebuilds the board visual to show the position after `ply` moves of the
    // current game's MatchData have been applied. Used by Arena's post-game
    // review (move-list click-to-seek). Does NOT mutate BoardPosition or any
    // game state — purely a visual scrub.
    public void
    SeekToPly(int ply)
    {
        if (Data == null) return;

        int total = Data.MoveCount();
        ply = Mathf.Clamp(ply, 0, total);

        ChessBoard tmp = new ChessBoard(Data.StartFen());
        int lastMove = 0;
        for (int i = 0; i < ply; i++)
        {
            int m = Data.MoveAt(i);
            tmp.MakeMove(m);
            lastMove = m;
        }

        bh.BoardReset(false);
        if (lastMove != 0) bh.MarkPlayedMove(lastMove);
        bh.Recreate(ref tmp);
    }


    private int
    PredictionCall()
    {
        var (prevEval, lastEval) = Data.LastEvalPair();

        // Both bots thinks white is winning
        if (Mathf.Min(prevEval, lastEval) >  AdjournWinMargin)
            return 1;

        // Both bots thinks black is winning
        if (Mathf.Max(prevEval, lastEval) < -AdjournWinMargin)
            return -1;

        // If position is drawn for the last N moves
        if (Data.DrawnPositionForContinuousMoves(0.25f, 60))
            return 2;

        return 0;
    }
}

