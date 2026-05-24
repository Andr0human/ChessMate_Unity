public static class TT
{
    // HashIndex is a Zobrist key table, all keys drawn from xorshift64star().
    // Layout:
    //   [0]                       side-to-move
    //   [1 .. 65]                 en-passant square (csep & 127, value 0..64)
    //   [CastleBase .. +15]       castling rights (csep >> 7, 4 bits)
    //   [PieceBase ..]            piece-square: pos + 64 * (piece + 6 * color)
    public const int HashCount  = 860;  // total keys
    public const int CastleBase = 66;   // first castling-rights key
    public const int PieceBase  = 85;   // first piece-square key

    public static ulong[] HashIndex;

    static ulong x = 1237;

    static ulong xorshift64star()
    {
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        return x * 0x2545F4914F6CDD1DUL;
    }


    public static void
    Init()
    {
        if (HashIndex != null) return;

        HashIndex = new ulong[HashCount];

        for (int i = 0; i < HashCount; i++)
            HashIndex[i] = xorshift64star();
    }


    public static ulong
    HashUpdate(int piece, int pos)
    {
        int color = piece >> 3;
        piece = (piece & 7) - 1;

        return HashIndex[ PieceBase + pos + 64 * (piece + 6 * color) ];
    }
}
