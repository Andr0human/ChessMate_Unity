using UnityEngine;
using System.Collections.Generic;


public class MatchData
{
    private List< int >            moves;
    private List<float>            evals;
    private List<float>         timeLeft;
    private List<ulong> occurredPositions;
    private string startPosFen;

    public int BookMoveCount { get; set; } = 0;

    bool firstMove;

    public MatchData(string fen)
    {
        moves = new List<int>();
        evals = new List<float>();
        timeLeft = new List<float>();
        occurredPositions = new List<ulong>();
        firstMove = true;
        startPosFen = fen;
    }

    public void
    Add(int move, float eval, float remainingTime, ulong key)
    {
        moves.Add(move);
        evals.Add(eval);
        timeLeft.Add(remainingTime);

        // If moved piece is a pawn or there is a captured piece
        if ((((move >> 12) & 7) == 1) || (((move >> 15) & 7) != 0))
            occurredPositions.Clear();

        occurredPositions.Add(key);
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

    public bool
    FiftyMoveRuleDraw()
    { return occurredPositions.Count > 100; }

    public bool
    ThreeMoveRepetitionDraw()
    {
        if (occurredPositions.Count < 3)
            return false;
        ulong lastKey = occurredPositions[occurredPositions.Count - 1];
        int count = 0;

        foreach (var key in occurredPositions)
            if (key == lastKey) count++;

        return count >= 3;
    }

    public int
    DifferentEvalCount(float margin)
    {
        int count = 0;
        for (int i = 1; i < evals.Count; i += 2)
        {
            float evalDiff = Mathf.Abs(evals[i] - evals[i - 1]);
            float maxEval = Mathf.Max(Mathf.Abs(evals[i]), Mathf.Abs(evals[i - 1]));

            if ((evalDiff >= margin) && (maxEval <= 12f)) count++;
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
            count = (Mathf.Abs(eval) < drawMargin) ? (count + 1) : (0);

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
            string token = "<link=\"" + (i + 1) + "\">" + mg.PrintMove(move, board) + "</link>";

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
