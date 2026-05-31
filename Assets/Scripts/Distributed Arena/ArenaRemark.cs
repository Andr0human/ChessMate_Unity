// Builds the per-game anomaly remark string — the same tags the sequential
// Arena.GameRemark() produces, but pure and parameter-driven so a worker thread
// can compute it at game completion (when it still holds the live MatchData /
// ChessBoard) and stash the finished string into a GameResult.
//
// Mirrors Arena.GameRemark exactly; kept separate rather than shared with Arena
// only because Arena's copy reads its `mm` fields directly. If these rules ever
// change, change both (or fold Arena onto this).
public static class ArenaRemark
{
    private const int WeightCutoff = 4000;

    // result/prediction are white-relative (+1/0/-1); differentEvalCount is
    // MatchData.DifferentEvalCount(3f); positionWeight is the final
    // ChessBoard.PositionWeight().
    public static string
    Build(int result, int prediction, GameEndState state,
          int differentEvalCount, int positionWeight)
    {
        string remark = "";

        // Prediction made and failed
        if ((prediction != 0) && (result != prediction))
            remark += "win-loss prediction-failed | ";

        // Huge evaluation swing in more than 5 places in a game
        if (differentEvalCount > 5)
            remark += "eval-diff | ";

        // Game ended with huge material still on the board
        if (positionWeight > WeightCutoff)
            remark += "huge material | ";

        if (state == GameEndState.DrawByRepetition)
            remark += "draw by 3-move repetition | ";

        if (state == GameEndState.WhiteWinsOnTime || state == GameEndState.BlackWinsOnTime)
            remark += "lost on time | ";

        if (remark.Length > 0)
            remark = remark.Substring(0, remark.Length - 3);

        return remark;
    }
}
