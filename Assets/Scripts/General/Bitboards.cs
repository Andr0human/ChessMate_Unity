// Board-geometry bit constants shared across move generation, board state,
// and the UCI codec. Squares index 0..63 = a1..h8 (LSB = a1, +1 = east,
// +8 = north).
public static class Bitboards
{
    // Ranks 1 and 8 — the pawn promotion ranks (0xFF at the bottom and top).
    public const ulong Rank18 = 0xFF000000000000FFUL;

    // The g-file. A king landing here after a two-square move means kingside
    // castling (O-O); otherwise it is queenside (O-O-O).
    public const ulong FileG = 0x4040404040404040UL;

    // De Bruijn sequence for single-bit index lookup: for a one-bit mask `b`,
    // `(b * DeBruijn64) >> 57` is a perfect 0..63 hash into a precomputed
    // square-index table.
    public const ulong DeBruijn64 = 4220644425418082699UL;
}
