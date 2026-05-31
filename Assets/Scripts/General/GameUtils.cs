// Pure, Unity-free game-outcome helpers shared by the scene MatchManager (main
// thread) and the worker-thread HeadlessMatchManager. These are the rules that
// decide how a game ends and how it's adjudicated; keeping them here is the
// single source of truth so the visible Arena scene and the distributed-arena
// workers can never score the same position differently.
//
// Every method is static and parameter-driven — no instance state, no
// UnityEngine — so it's safe to call from any thread.
public static class GameUtils
{
    // Returns how the game stands for the given side to move, or Ongoing.
    // `remainingForSideToMove` is that side's clock in seconds (Timer's
    // ChessClocks[side] or StopwatchClock.Remaining(side)).
    public static GameEndState
    IsGameOver(ref ChessBoard pos, MatchData data, int side2move,
               float remainingForSideToMove)
    {
        MoveList moveslist = MoveGenerator.GenerateMoves(ref pos);

        // checkmate / stalemate — the side to move has no legal reply
        if (moveslist.moveCount == 0)
        {
            if (moveslist.KingAttackers == 0)
                return GameEndState.DrawByStalemate;
            return (side2move == 0) ? GameEndState.BlackWinsByCheckmate
                                    : GameEndState.WhiteWinsByCheckmate;
        }

        if (InsufficientMaterial(ref pos))
            return GameEndState.DrawByInsufficientMaterial;

        if (data.ThreeMoveRepetitionDraw()) return GameEndState.DrawByRepetition;
        if (data.FiftyMoveRuleDraw())       return GameEndState.DrawByFiftyMoveRule;

        if (remainingForSideToMove < 0f)
            return (side2move == 0) ? GameEndState.BlackWinsOnTime
                                    : GameEndState.WhiteWinsOnTime;

        return GameEndState.Ongoing;
    }


    public static bool
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


    // Early-adjudication signal from the engines' evals (not a game-ender; the
    // caller stores the first non-zero value for scoring/anomaly reporting).
    // +1 both bots see white winning, -1 both see black winning, 2 dead-drawn,
    // 0 undecided.
    public static int
    PredictionCall(MatchData data, float adjournWinMargin)
    {
        var (prevEval, lastEval) = data.LastEvalPair();

        // Both bots think white is winning
        if (System.Math.Min(prevEval, lastEval) >  adjournWinMargin)
            return 1;

        // Both bots think black is winning
        if (System.Math.Max(prevEval, lastEval) < -adjournWinMargin)
            return -1;

        // Position has been drawish for the last N moves
        if (data.DrawnPositionForContinuousMoves(0.25f, 60))
            return 2;

        return 0;
    }


    public static string
    BuildSearchLogPath(string dir, string engineName, int gameNumber)
    {
        if (string.IsNullOrEmpty(dir) || engineName == "human") return null;
        return dir + "/logs_" + engineName + "~/game_" + gameNumber + ".log";
    }
}
