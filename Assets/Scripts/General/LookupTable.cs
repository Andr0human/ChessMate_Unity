
public class LookupTable
{
    public ulong[]    UpMasks = new ulong[64];
    public ulong[]  DownMasks = new ulong[64];
    public ulong[] RightMasks = new ulong[64];
    public ulong[]  LeftMasks = new ulong[64];

    public ulong[]   UpRightMasks = new ulong[64];
    public ulong[]    UpLeftMasks = new ulong[64];
    public ulong[] DownRightMasks = new ulong[64];
    public ulong[]  DownLeftMasks = new ulong[64];

    public ulong[][]        PawnMasks = new ulong[2][];
    public ulong[][] PawnCaptureMasks = new ulong[2][];

    public ulong[] KnightMasks = new ulong[64];
    public ulong[]   KingMasks = new ulong[64];


    private static bool
    InRange(int x, int y)
    { return (x >= 0) & (x < 8) & (y >= 0) & (y < 8); }


    private static void
    BuildSlidingTable(ref ulong[] table, int index, int indexInc, int incX, int incY)
    {
        for (int idx = index;; idx += indexInc)
        {
            if (idx < 0 || idx >= 64) break;
            int x = (idx & 7), y = (idx - x) >> 3;
            ulong val = 0;
            for (int i = x, j = y; InRange(i, j); i += incX, j += incY)
            {
                table[8 * j + i] = val;
                val |= 1UL << (8 * j + i);
            }
        }
    }

    private static void
    BuildPawnTable(ref ulong[] table, int dir, bool captures)
    {
        table = new ulong[64];
        for (int i = 0; i < 64; i++)
        {
            int x = i & 7, y = (i - x) >> 3, up = i + 8 * dir;
            ulong val = 0;
            if (captures)
            {
                if (x - 1 >= 0) val |= 1UL << (x - 1 + 8 * (y + dir));
                if (x + 1 < 8)  val |= 1UL << (x + 1 + 8 * (y + dir));
            }
            else if (0 <= up && up < 64)
                val |= 1UL << up;
            table[i] = val;
        }
    }

    private static void
    BuildKingTable(ref ulong[] table)
    {
        ulong Bit(int x, int y) =>
            InRange(x, y) ? (1UL << (8 * y + x)) : (0);

        for (int sq = 0; sq < 64; sq++)
        {
            int x = sq & 7;
            int y = (sq - x) >> 3;
            table[sq] = Bit(x, y + 1) | Bit(x, y - 1)
                      | Bit(x + 1, y) | Bit(x + 1, y + 1) | Bit(x + 1, y - 1)
                      | Bit(x - 1, y) | Bit(x - 1, y + 1) | Bit(x - 1, y - 1);
        }
    }

    private static void
    BuildKnightTable(ref ulong[] table)
    {
        ulong Bit(int x, int y) =>
            InRange(x, y) ? (1UL << (8 * y + x)) : (0);

        for (int sq = 0; sq < 64; sq++)
        {
            int x = sq & 7;
            int y = (sq - x) >> 3;
            table[sq] =
                Bit(x + 2, y + 1) | Bit(x + 2, y - 1) | Bit(x - 2, y + 1) | Bit(x - 2, y - 1)
              | Bit(x + 1, y + 2) | Bit(x + 1, y - 2) | Bit(x - 1, y + 2) | Bit(x - 1, y - 2);
        }
    }

    public
    LookupTable()
    {
        BuildSlidingTable(ref    UpMasks, 56,  1,  0, -1);
        BuildSlidingTable(ref  DownMasks,  7, -1,  0,  1);
        BuildSlidingTable(ref RightMasks,  7,  8, -1,  0);
        BuildSlidingTable(ref  LeftMasks,  0,  8,  1,  0);

        BuildSlidingTable(ref   UpRightMasks, 63, -8, -1, -1);
        BuildSlidingTable(ref   UpRightMasks, 56,  1, -1, -1);
        BuildSlidingTable(ref  DownLeftMasks,  0,  8,  1,  1);
        BuildSlidingTable(ref  DownLeftMasks,  7, -1,  1,  1);
        BuildSlidingTable(ref    UpLeftMasks, 56,  1,  1, -1);
        BuildSlidingTable(ref    UpLeftMasks, 56, -8,  1, -1);
        BuildSlidingTable(ref DownRightMasks,  7,  8, -1,  1);
        BuildSlidingTable(ref DownRightMasks,  7, -1, -1,  1);

        BuildPawnTable(ref PawnMasks[1],  1, false);
        BuildPawnTable(ref PawnMasks[0], -1, false);
        BuildPawnTable(ref PawnCaptureMasks[1],  1, true);
        BuildPawnTable(ref PawnCaptureMasks[0], -1, true);

        BuildKnightTable(ref KnightMasks);
        BuildKingTable(ref KingMasks);
    }

};

