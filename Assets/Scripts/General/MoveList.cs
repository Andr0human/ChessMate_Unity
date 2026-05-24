using System.Collections.Generic;


public class MoveList
{
    public int KingAttackers;
    public int pColor, moveCount;
    public ulong startIndex;
    public ulong[] endIndex;

    public List<int> moves;

    public
    MoveList(int pc = 0)
    {
        KingAttackers = moveCount = 0;
        pColor = pc;
        startIndex = 0;
        endIndex = new ulong[64];
        moves = new List<int>();
    }

    public void
    Clear()
    {
        startIndex = 0;
        moveCount = KingAttackers = 0;
        for (int i = 0; i < 64; i++) endIndex[i] = 0;
    }

    public bool
    ValidInitSquare(int ip)
    { return (startIndex & (1UL << ip)) != 0; }

    public bool
    ValidDestSquare(int ip, int fp)
    {
        return (  startIndex & (1UL << ip)) != 0
            && (endIndex[ip] & (1UL << fp)) != 0;
    }

    public bool
    ContainsMove(int move)
    {
        int ip = move & 63, fp = (move >> 6) & 63;
        if ((startIndex & (1UL << ip)) != 0)
            if ((endIndex[ip] & (1UL << fp)) != 0) return true;
        return false;
    }


    public void
    Add(int move)
    {
        moves.Add(move);

        int ip = move & 63;
        int fp = (move >> 6) & 63;

        startIndex   |= 1UL << ip;
        endIndex[ip] |= 1UL << fp;

        moveCount++;
    }

};
