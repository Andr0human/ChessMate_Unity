using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class MatchManager : MonoBehaviour
{
    [SerializeField] private Timer tmr;
    [SerializeField] private BoardHandler bh;

    // OpeningBook is a plain class now, not a scene component. MatchManager is
    // the single main-thread owner: it builds the book once in Awake (where
    // streamingAssetsPath is resolvable) and exposes it for DashBoard, Arena,
    // and the ChessEngine players it spawns.
    public OpeningBook OB { get; private set; }

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
    Awake()
    {
        // TT must be ready before the book builder hashes positions.
        TT.Init();
        OB = new OpeningBook(Application.streamingAssetsPath);
        if (!OB.GetOpeningBook())
            Debug.LogWarning("Opening book not found — games will run without it.");
    }


    private void
    Start()
    {
        BoardPosition = new ChessBoard(startFen);
        bh.InitializeBoard(ref BoardPosition);
        Players = new IPlayer[2];
    }


    #region MATCH_UTILS


    // Re-seat the board at the standard start position. The fallback when an
    // opening line is blank or malformed: better to play a normal game from the
    // start than to corrupt the board or kill the run on a bad book entry.
    private void
    ResetToStartPosition()
    {
        Data          = new MatchData(startFen);
        BoardPosition = new ChessBoard(startFen);
        Side2Move     = 0;

        bh.BoardReset();
        bh.Recreate(ref BoardPosition);
    }


    private IEnumerator
    PlayOpening(string opening)
    {
        // Blank / whitespace line (e.g. a stray empty line in the book file):
        // nothing to apply — just play from the start position.
        if (string.IsNullOrWhiteSpace(opening))
        {
            ResetToStartPosition();
            yield break;
        }

        if (OB.IsFen(opening))
        {
            // A malformed FEN throws in the ChessBoard parser; don't let one bad
            // line take down the whole arena coroutine — fall back to the start.
            ChessBoard parsed = null;
            try { parsed = new ChessBoard(opening); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Invalid opening FEN '{opening}' ({e.Message}); "
                                 + "starting from the initial position.");
            }

            if (parsed == null)
            {
                ResetToStartPosition();
                yield break;
            }

            Data = new MatchData(opening);
            Data.SeedHalfmoveClock(parsed.halfmove);   // honor the FEN's 50-move clock
            BoardPosition = parsed;

            bh.BoardReset();
            bh.Recreate(ref BoardPosition);

            Side2Move = BoardPosition.color ^ 1;

            yield return new WaitForSeconds(0.3f);
        }
        else
        {
            List<int> openingLine = OB.ExtractLine(opening);

            // A 0 entry means a token failed to decode (malformed line). Applying
            // it would corrupt the board via MakeMove(0); fall back to the start.
            if (openingLine.Contains(0))
            {
                Debug.LogWarning($"Invalid opening line '{opening}'; "
                                 + "starting from the initial position.");
                ResetToStartPosition();
                yield break;
            }

            Data = new MatchData(startFen);
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
        return GameUtils.IsGameOver(ref BoardPosition, Data, Side2Move,
                                    tmr.ChessClocks[Side2Move]);
    }


    public bool
    TimeLeftForSearch()
    {
        return tmr.ChessClocks[Side2Move] > 0f;
    }


    private void
    OnApplicationQuit()
    {
        if (Players[0] != null) Players[0].Stop();
        if (Players[1] != null) Players[1].Stop();

        Application.Quit();
    }

    #endregion


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
        string whiteLog = GameUtils.BuildSearchLogPath(searchLogDir, playerWhite, searchLogGameNumber);
        string blackLog = GameUtils.BuildSearchLogPath(searchLogDir, playerBlack, searchLogGameNumber);

        Players[0] = (playerWhite == "human")
                      ? new HumanPlayer()
                      : new ChessEngine(playerWhite, OB, tmr, Application.streamingAssetsPath,
                                        fixedTimePerMove, allowOpeningBook, whiteLog);

        Players[1] = (playerBlack == "human")
                      ? new HumanPlayer()
                      : new ChessEngine(playerBlack, OB, tmr, Application.streamingAssetsPath,
                                        fixedTimePerMove, allowOpeningBook, blackLog);

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
        return GameUtils.PredictionCall(Data, AdjournWinMargin);
    }
}

