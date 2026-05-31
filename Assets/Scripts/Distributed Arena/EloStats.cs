using System;

// Converts a win/draw/loss tally into an Elo difference plus a 95% confidence
// interval, for the live dashboard's score readout.
//
// The interval is the score-based normal approximation that accounts for draws
// (the standard chess "Elo ± error" bar): treat each game's score as a random
// variable worth 1 / 0.5 / 0, take the mean and its standard error over n
// games, put a 1.96·SE band on the mean score, then map the score bounds
// through the logistic Elo formula. This generalises the plain win/loss Wilson
// interval the spec sketched to a draw-inclusive score; for a draw-free run it
// reduces to the same normal-approx band. The Elo bounds are asymmetric because
// the score→Elo map is nonlinear, so Low/High are reported, not a single ±.
public static class EloStats
{
    public readonly struct Result
    {
        public readonly double Score;   // (W + 0.5D)/n, in [0,1]
        public readonly double Elo;     // central estimate
        public readonly double EloLow;  // 95% lower bound
        public readonly double EloHigh; // 95% upper bound
        public readonly int    Games;

        public Result(double score, double elo, double lo, double hi, int games)
        { Score = score; Elo = elo; EloLow = lo; EloHigh = hi; Games = games; }
    }

    // Clamp scores away from 0/1 so the logit stays finite even at extreme
    // (or tiny-sample) tallies.
    private const double Eps = 1e-4;
    private const double Z   = 1.95996398454; // 95% two-sided normal quantile

    // Elo difference implied by an expected score in (0,1).
    public static double
    EloDiff(double score)
    {
        score = Clamp(score, Eps, 1.0 - Eps);
        return -400.0 * Math.Log10(1.0 / score - 1.0);
    }

    public static Result
    FromCounts(int wins, int draws, int losses)
    {
        int n = wins + draws + losses;
        if (n == 0)
            return new Result(0.0, 0.0, 0.0, 0.0, 0);

        double pw = (double)wins   / n;
        double pd = (double)draws  / n;
        double pl = (double)losses / n;

        double score = pw + 0.5 * pd;

        // Variance of a single game's score about the mean, then SE of the mean.
        double var = pw * Sq(1.0 - score)
                   + pd * Sq(0.5 - score)
                   + pl * Sq(0.0 - score);
        double se  = Math.Sqrt(var / n);

        double lo = Clamp(score - Z * se, Eps, 1.0 - Eps);
        double hi = Clamp(score + Z * se, Eps, 1.0 - Eps);

        return new Result(score, EloDiff(score), EloDiff(lo), EloDiff(hi), n);
    }

    private static double Sq(double x)               => x * x;
    private static double Clamp(double v, double a, double b) => v < a ? a : (v > b ? b : v);
}
