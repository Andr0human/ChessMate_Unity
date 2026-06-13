using System.Collections.Generic;


public class MatchData
{
    private List< int >            moves;
    private List<float>            evals;
    private List<float>         timeLeft;
    private List<ulong> repetitionHistory;
    private int               halfmoveClock;
    private string startPosFen;

    public int BookMoveCount { get; set; } = 0;

    bool firstMove;

    public MatchData(string fen)
    {
        moves = new List<int>();
        evals = new List<float>();
        timeLeft = new List<float>();
        repetitionHistory = new List<ulong>();
        halfmoveClock = 0;
        firstMove = true;
        startPosFen = fen;
    }

    public void
    Add(int move, float eval, float remainingTime, ulong key)
    {
        moves.Add(move);
        evals.Add(eval);
        timeLeft.Add(remainingTime);

        // A pawn move or capture is irreversible: it resets both the 50-move
        // clock and the repetition history (no earlier position can recur).
        if ((((move >> 12) & 7) == 1) || (((move >> 15) & 7) != 0))
        {
            repetitionHistory.Clear();
            halfmoveClock = 0;
        }

        repetitionHistory.Add(key);
        halfmoveClock++;
    }

    public int
    LastPlayedMove()
    {
        if (firstMove)
        {
            firstMove = false;
            return 0;
        }
        return (moves.Count > 0) ? (moves[moves.Count - 1]) : (0);
    }

    // Seed the 50-move clock for a game that starts from a FEN whose halfmove
    // counter is non-zero. Without this the adjudicator restarts the count at 0
    // while the engine (sent the full FEN) sees the real clock, so the two
    // disagree on how close the 50-move draw is. Unit is plies — the same unit
    // the FEN's halfmove field and FiftyMoveRuleDraw() use.
    public void
    SeedHalfmoveClock(int plies)
    { halfmoveClock = plies; }

    public bool
    FiftyMoveRuleDraw()
    { return halfmoveClock > 100; }

    public bool
    ThreeMoveRepetitionDraw()
    {
        if (repetitionHistory.Count < 3)
            return false;
        ulong lastKey = repetitionHistory[repetitionHistory.Count - 1];
        int count = 0;

        foreach (var key in repetitionHistory)
            if (key == lastKey) count++;

        return count >= 3;
    }

    // Counts full moves where the two engines disagreed on *who* is winning:
    // their (white-relative) evals straddle zero AND differ by at least `margin`
    // pawns. A same-sign gap like +15 vs +12 is just two engines scaling a won
    // position differently and is ignored; an opposite-sign gap like +2 vs -1 is
    // a genuine "who's better?" disagreement worth a look. This also shrugs off a
    // constant per-engine eval offset, which a raw magnitude diff would not.
    // Mate-score plies (stored as ±100) are excluded by the 12-pawn cap.
    public int
    DivergentEvalCount(float margin)
    {
        int count = 0;
        for (int i = 1; i < evals.Count; i += 2)
        {
            float a = evals[i - 1];
            float b = evals[i];
            float maxEval = System.Math.Max(System.Math.Abs(a), System.Math.Abs(b));

            if (a * b < 0f && System.Math.Abs(a - b) >= margin && maxEval <= 12f)
                count++;
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
    DrawnPositionForContinuousMoves(float drawMargin, int length)
    {
        if (evals.Count < length)
            return false;

        int count = 0;
        foreach (float eval in evals)
        {
            count = (System.Math.Abs(eval) < drawMargin) ? (count + 1) : (0);

            if (count >= length)
                return true;

            if (count + (length - evals.Count) < length)
                return false;
        }

        return false;
    }

    public string
    GetMoveList()
    {
        var sb = new System.Text.StringBuilder();
        ChessBoard board = new ChessBoard(startPosFen);

        // Detect starting side + fullmove number from FEN so the move-pair
        // layout works even from a non-standard start position.
        string[] fenParts = startPosFen.Split(' ');
        bool whiteFirst = fenParts.Length < 2 || fenParts[1] == "w";
        int startFullMove = 1;
        if (fenParts.Length >= 6) int.TryParse(fenParts[5], out startFullMove);
        if (startFullMove < 1) startFullMove = 1;

        int lastIdx = moves.Count - 1;
        bool inBook = false;

        for (int i = 0; i < moves.Count; i++)
        {
            int move = moves[i];
            bool isBook = i < BookMoveCount;
            bool isLast = i == lastIdx;

            bool isWhitePly  = whiteFirst ? (i % 2 == 0) : (i % 2 == 1);
            int  fullMoveNo  = whiteFirst ? (startFullMove + i / 2)
                                          : (startFullMove + (i + 1) / 2);
            bool needsPrefix = isWhitePly || i == 0;

            if (needsPrefix)
            {
                // Move-number prefix should never inherit the book color.
                if (inBook) { sb.Append("</color>"); inBook = false; }
                if (i > 0) sb.Append('\n');
                // Right-pad number to width 3 so the white-move column is
                // aligned even when game crosses move 10/100.
                sb.Append("<mspace=0.55em>")
                  .Append(fullMoveNo.ToString().PadLeft(3))
                  .Append(isWhitePly ? "." : "...")
                  .Append("</mspace> ");
            }
            else
            {
                // Tab to a fixed pixel column so black moves line up across rows.
                sb.Append("<pos=55%>");
            }

            if (isBook && !inBook) { sb.Append("<color=#C9A227>"); inBook = true; }
            else if (!isBook && inBook) { sb.Append("</color>"); inBook = false; }

            // Link id = number of moves applied to reach the position AFTER this move.
            // Click handler passes this directly into MatchManager.SeekToPly().
            string token = "<link=\"" + (i + 1) + "\">" + MoveGenerator.PrintMove(move, board) + "</link>";

            if (isLast)
            {
                // Last-played move is always bold-white, even inside a book span.
                // Briefly close + reopen the book color so the white shows through.
                bool reopenBook = inBook;
                if (reopenBook) sb.Append("</color>");
                sb.Append("<b><color=#FFFFFF>").Append(token).Append("</color></b>");
                if (reopenBook) sb.Append("<color=#C9A227>");
            }
            else
            {
                sb.Append(token);
            }

            sb.Append(' ');
            board.MakeMove(move);
        }
        if (inBook) sb.Append("</color>");

        return sb.ToString();
    }


    // Plain standard-PGN movetext for the .pgn files on disk. GetMoveList()
    // above is for the on-screen list and embeds TMP rich-text tags
    // (<link>/<mspace>/<pos>/<color>) that make the file unparseable by any
    // chess GUI; this is the clean counterpart. Pass the game's result token
    // ("1-0"/"0-1"/"1/2-1/2"/"*") so the movetext is terminated as PGN requires.
    public string
    GetPgnMoveText(string resultToken = "*")
    {
        var sb = new System.Text.StringBuilder();
        ChessBoard board = new ChessBoard(startPosFen);

        string[] fenParts = startPosFen.Split(' ');
        bool whiteFirst = fenParts.Length < 2 || fenParts[1] == "w";
        int startFullMove = 1;
        if (fenParts.Length >= 6) int.TryParse(fenParts[5], out startFullMove);
        if (startFullMove < 1) startFullMove = 1;

        int lineLen = 0;
        void Emit(string token)
        {
            // Wrap near 80 columns like a standard PGN export.
            if (lineLen > 0 && lineLen + token.Length + 1 > 80)
            {
                sb.Append('\n');
                lineLen = 0;
            }
            else if (lineLen > 0)
            {
                sb.Append(' ');
                lineLen++;
            }
            sb.Append(token);
            lineLen += token.Length;
        }

        for (int i = 0; i < moves.Count; i++)
        {
            int move = moves[i];
            bool isWhitePly = whiteFirst ? (i % 2 == 0) : (i % 2 == 1);
            int  fullMoveNo = whiteFirst ? (startFullMove + i / 2)
                                         : (startFullMove + (i + 1) / 2);

            if (isWhitePly)      Emit(fullMoveNo + ".");
            else if (i == 0)     Emit(fullMoveNo + "...");   // black-to-move start

            Emit(MoveGenerator.PrintMove(move, board));
            board.MakeMove(move);
        }

        Emit(resultToken);
        return sb.ToString();
    }

    public int
    MoveAt(int index)
    {
        return moves[index];
    }

    public int
    MoveCount()
    {
        return moves.Count;
    }

    public string StartFen()
    { return startPosFen; }
}
