// Single source of truth for the per-game anomaly remark string. Pure and
// parameter-driven so it can run on a worker thread at game completion (when
// it still holds the live MatchData / ChessBoard) and stash the finished
// string into a GameResult. The sequential Arena.GameRemark() is a thin
// wrapper that pulls these values off `mm` and calls straight through here,
// so the two code paths can no longer drift apart.
public static class ArenaRemark
{
    // Decisive game that still has most of the material on the board — a
    // sharp, quick finish (start position is 7880; 4000 ≈ half).
    private const int WeightCutoff = 4000;

    // A draw only earns a flag when a side had a real material edge and still
    // let the game slip away (fortress, 50-move grind, blundered stalemate); an
    // equal-material draw is just the engines agreeing. ~one minor piece.
    private const int ImbalanceCutoff = 300;

    // How many "engines disagree on who's winning" moments make a game worth a
    // look. The per-moment bar (opposite-sign evals ≥3 pawns apart) is already
    // strict, so a couple of them signals a genuinely contentious game. Tune
    // after a real run if it fires too often / never.
    private const int DivergenceCutoff = 2;

    // result/prediction are white-relative (+1/0/-1); prediction also uses 2
    // for a dead-drawn read (see GameUtils.PredictionCall). divergentEvalCount
    // is MatchData.DivergentEvalCount(3f); positionWeight is the final
    // ChessBoard.PositionWeight(); materialImbalance is ChessBoard.MaterialImbalance().
    public static string
    Build(int result, int prediction, GameEndState state,
          int divergentEvalCount, int positionWeight, int materialImbalance)
    {
        string remark = "";

        // Prediction made and failed. PredictionCall encodes a dead-drawn
        // read as 2; map that to the draw result (0) before comparing, so a
        // correctly drawn game isn't mislabelled as a failed prediction.
        if (prediction != 0)
        {
            int predictedResult = (prediction == 2) ? 0 : prediction;
            if (result != predictedResult)
                remark += "win-loss prediction-failed | ";
        }

        // Engines disagreed on *who* was winning (opposite-sign evals ≥3 pawns
        // apart) at several points — a contentious game where their evaluations
        // clash, not just two engines scaling the same won position differently.
        if (divergentEvalCount >= DivergenceCutoff)
            remark += "eval divergence | ";

        // Quick decisive game with the board still full. Gated on checkmate so
        // a midgame draw or flag-fall with lots of material doesn't trip it.
        if (positionWeight > WeightCutoff
            && (state == GameEndState.WhiteWinsByCheckmate
                || state == GameEndState.BlackWinsByCheckmate))
            remark += "huge material | ";

        // Drawing (any mechanism: repetition, 50-move, stalemate) while a side
        // was up real material is a failed conversion worth a look. result == 0
        // covers every draw type; insufficient-material draws self-gate below
        // the cutoff.
        if (result == 0 && materialImbalance >= ImbalanceCutoff)
            remark += "unconverted edge | ";

        if (state == GameEndState.WhiteWinsOnTime || state == GameEndState.BlackWinsOnTime)
            remark += "lost on time | ";

        if (remark.Length > 0)
            remark = remark.Substring(0, remark.Length - 3);

        return remark;
    }
}
