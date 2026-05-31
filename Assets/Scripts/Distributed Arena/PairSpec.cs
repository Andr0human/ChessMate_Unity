// One unit of arena work: a single opening line played in BOTH colour
// orientations (game 0 = engine A white, game 1 = engine B white). Dispatching
// the pair whole makes colour fairness structural — there is no separate
// colour-swap bookkeeping the way the sequential Arena tracks `side2start`.
//
// Built up front on the main thread (openings pre-assigned round-robin, so
// workers never touch OpeningBook.NextOpening()'s racy shared counter) and
// pulled from a ConcurrentQueue<PairSpec> by the worker threads. Immutable, so
// it's safe to hand the same instance to whichever worker dequeues it.
public readonly struct PairSpec
{
    public readonly int    PairId;       // 0-based; stable game numbers derive from it
    public readonly string OpeningLine;  // book line / FEN; "" = play from the start position

    public
    PairSpec(int pairId, string openingLine)
    {
        PairId      = pairId;
        OpeningLine = openingLine ?? "";
    }
}
