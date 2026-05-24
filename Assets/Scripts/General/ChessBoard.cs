using System.Collections.Generic;
using UnityEngine;

public class ChessBoard
{
    // 1 = wP , 2 = wB, 3 = wN, 4 = wR, 5 = wQ, 6 = wK, 0 = free square 
    public   int[]    board;
    public ulong[]   pieces;
    private  int[] bitIndex;

    // csep -> castling states + en-passant squares
    public int color, csep;
    public int halfmove, fullmove;

    public ulong hashvalue;

    private List<int> prevMoves;
    private List<int> prevCseps;
    private List<ulong> prevHashKeys;

    #region Load Position

    private int
    CharToPieceType(char piece)
    {
        int side = (piece < 'a') ? 8 : 0;
        piece = (char)(piece < 'a' ? piece : piece - 32);

        int res = 6;
        if (piece == 'P') res = 1;
        else if (piece == 'B') res = 2;
        else if (piece == 'N') res = 3;
        else if (piece == 'R') res = 4;
        else if (piece == 'Q') res = 5;

        return res + side;
    }

    private string
    PieceToChar(int piece)
    {
        int side = piece & 8;
        piece &= 7;

        char res = 'K';
        if (piece == 1) res = 'P';
        else if (piece == 2) res = 'B';
        else if (piece == 3) res = 'N';
        else if (piece == 4) res = 'R';
        else if (piece == 5) res = 'Q';

        res += (char)((side == 0) ? 32 : 0);
        return res.ToString();
    }

    private void
    Reset()
    {
        board  = new int[64];
        pieces = new ulong[16];
        bitIndex = new int[128];

        color = csep = 0;
        halfmove = fullmove = 0;
        hashvalue = 0;

        prevMoves = new List<int>();
        prevCseps = new List<int>();
        prevHashKeys = new List<ulong>();

        for (int sq = 0; sq < 64; sq++)
            bitIndex[((1UL << sq) * Bitboards.DeBruijn64) >> 57] = sq;
    }

    public
    ChessBoard(string fen)
    {
        LoadFen(fen);
    }

    public void
    LoadFen(string fen)
    {
        Reset();

        string[] elems = fen.Split();

        int square = 56;
        foreach (char ch in elems[0])
        {
            if (char.IsDigit(ch))
                square += (char)(ch - '0');
            else if (ch == '/')
                square -= (square & 7) != 0 ? ((square & 7) + 8) : (16);
            else
            {
                int piece = CharToPieceType(ch);
                board[square] = piece;
                pieces[piece] |= 1UL << square;
                pieces[(piece & 8) + 7] |= 1UL << square;
                square++;
            }
        }

        color = (elems[1] == "w") ? 1 : 0;
        
        foreach (char k in elems[2])
        {
            if (k == 'K') csep |= 1024;
            else if (k == 'Q') csep |= 512;
            else if (k == 'k') csep |= 256;
            else if (k == 'q') csep |= 128;
        }
        
        if (elems[3][0] == '-') csep |= 64;
        else csep |= 28 + ((2 * color - 1) * 12) + (elems[3][0] - 'a');
        
        halfmove = int.Parse(elems[4]);
        fullmove = int.Parse(elems[5]);

        hashvalue = GenerateHashKey();
    }

    public string
    Fen()
    {
        string generatedFen = "";
        int zeros = 0;

        for (int square = 56; square >= 0; square++)
        {
            if (board[square] == 0)
                zeros++;
            else
            {
                if (zeros != 0) generatedFen += zeros.ToString();
                
                zeros = 0;
                generatedFen += PieceToChar(board[square]);
            }

            if ((square & 7) == 7)
            {
                if (zeros != 0) generatedFen += zeros.ToString();
                
                zeros = 0;
                generatedFen += '/';
                square -= 16;
            }
        }

        generatedFen = generatedFen.Substring(0, generatedFen.Length - 1) + ' ';
        generatedFen += (color == 1) ? "w " : "b ";

        if ((csep & 1024) != 0) generatedFen += "K";
        if ((csep &  512) != 0) generatedFen += "Q";
        if ((csep &  256) != 0) generatedFen += "k";
        if ((csep &  128) != 0) generatedFen += "q";
        if ((csep & 1920) == 0) generatedFen += "-";

        generatedFen += ' ';

        if ((csep & 64) != 0)
            generatedFen += "- ";
        else
        {
            generatedFen += (char)((csep & 7) + 'a');
            generatedFen += (color == 1) ? "6 " : "3 ";
        }

        generatedFen += halfmove.ToString() + " " + fullmove.ToString();
        return generatedFen;
    }

    public ulong
    GenerateHashKey()
    {
        ulong key = 0;

        if (color == 0)
            key ^= TT.HashIndex[0];

        if ((csep & 127) != 64)
            key ^= TT.HashIndex[(csep & 127) + 1];

        key ^= TT.HashIndex[(csep >> 7) + TT.CastleBase];

        for (int sq = 0; sq < 64; sq++)
        {
            if (board[sq] == 0)
                continue;
            key ^= TT.HashUpdate(board[sq], sq);
        }

        return key;
    }

    #endregion


    #region Utils

    public ulong
    Lsb(ulong x)
    {
        return x & ~(x - 1);
    }

    public ulong
    Msb(ulong x)
    {
        ulong tmp = x;
        while (x != 0) {
            tmp = x;
            x &= x - 1;
        }
        return tmp;
    }

    public int
    LsbIdx(ulong x)
    {
        return (x != 0) ? IndexNo(Lsb(x)) : 63;
    }

    public int
    MsbIdx(ulong x)
    {
        return (x != 0) ? IndexNo(Msb(x)) : 0;
    }

    public int
    PopCount(ulong x)
    {
        int ones = 0;
        while (x != 0) {
            x &= x - 1;
            ones++;
        }
        return ones;
    }

    public int
    IndexNo(ulong x)
    {
        return bitIndex[(x * Bitboards.DeBruijn64) >> 57];
    }

    public int
    PositionWeight()
    {
        int weight = 0;
        weight += 100 * PopCount(pieces[1] | pieces[ 9]);
        weight += 320 * PopCount(pieces[2] | pieces[10]);
        weight += 300 * PopCount(pieces[3] | pieces[11]);
        weight += 500 * PopCount(pieces[4] | pieces[12]);
        weight += 900 * PopCount(pieces[5] | pieces[13]);
        return weight;
    }

    #endregion


    #region Make/Unmake-move

    public void
    MakeMove(int move)
    {
        StoreCurrentStateValues(move);

        int ip = move & 63;
        int fp = (move >> 6) & 63;

        ulong ipos = 1UL << ip;
        ulong fpos = 1UL << fp;

        int ep = csep & 127;
        int own = Own();
        int emy = Emy();

        // Piece at init and dest squares.
        int ipt = board[ip];
        int fpt = board[fp];

        board[ip] = 0;
        board[fp] = ipt;

        if (ep != 64)
            hashvalue ^= TT.HashIndex[ep + 1];
        
        csep = (csep & 1920) ^ 64;

        // If rook captured
        MakeMoveCornerRook(ipt & 7, ip);

        // If rook moved
        MakeMoveCornerRook(fpt & 7, fp);

        // Check special pawn moves
        if ((ipt & 7) == 1)
        {
            // Double pawn push
            if (Mathf.Abs(ip - fp) == 16)
            {
                csep = (csep & 1920) | ((ip + fp) / 2);

                pieces[own + 1] ^= ipos ^ fpos;
                pieces[own + 7] ^= ipos ^ fpos;

                // Add current enpassant-state to hashvalue
                hashvalue ^= TT.HashIndex[1 + (csep & 127)] ^ TT.HashIndex[0];
                hashvalue ^= TT.HashUpdate(own + 1, ip) ^ TT.HashUpdate(own + 1, fp);
                
                color ^= 1;
                return;
            }

            // En-passant move
            if (fp == ep)
            {
                int capPawnFp = ep - 8 * (2 * color - 1);

                // Remove opp. pawn from the board
                pieces[emy + 1] ^= 1UL << capPawnFp;
                pieces[emy + 7] ^= 1UL << capPawnFp;
                board[capPawnFp] = 0;

                // Update own pawn in pieces-table
                pieces[own + 1] ^= ipos ^ fpos;
                pieces[own + 7] ^= ipos ^ fpos;

                hashvalue ^= TT.HashUpdate(emy + 1, capPawnFp) ^ TT.HashIndex[0];
                hashvalue ^= TT.HashUpdate(own + 1, ip) ^ TT.HashUpdate(own + 1, ep);

                color ^= 1;
                return;
            }

            // Promotion
            if ((fpos & Bitboards.Rank18) != 0)
            {
                int newPt = ((move >> 18) & 3) + 2;

                pieces[own + 1] ^= ipos;
                pieces[own + newPt] ^= fpos;
                pieces[own + 7] ^= ipos ^ fpos;
                board[fp] = own + newPt;

                if (fpt > 0)
                {
                    pieces[fpt] ^= fpos;
                    pieces[emy + 7] ^= fpos;

                    hashvalue ^= TT.HashUpdate(fpt, fp);
                }

                hashvalue ^= TT.HashIndex[0]
                           ^ TT.HashUpdate(own + 1, ip)
                           ^ TT.HashUpdate(own + newPt, fp);

                color ^= 1;
                return;
            }
        }

        // Check king moves
        if ((ipt & 7) == 6)
        {
            int oldCsep = csep;
            int filter = 2047 ^ (384 << (color * 2));
            csep &= filter;

            hashvalue ^= TT.HashIndex[(oldCsep >> 7) + TT.CastleBase];
            hashvalue ^= TT.HashIndex[(csep >> 7) + TT.CastleBase];

            // Castling
            if (Mathf.Abs(fp - ip) == 2) {
                MakeMoveCastling(ip, fp, 1);
                return;
            }
        }

        if (fpt > 0)
        {
            pieces[fpt] ^= fpos;
            pieces[emy + 7] ^= fpos;

            hashvalue ^= TT.HashUpdate(fpt, fp);
        }

        pieces[ipt] ^= ipos ^ fpos;
        pieces[own + 7] ^= ipos ^ fpos;

        hashvalue ^= TT.HashIndex[0] ^ TT.HashUpdate(ipt, ip) ^ TT.HashUpdate(ipt, fp);
        color ^= 1;
    }

    public void
    UnmakeMove()
    {
        int move = RetrievePrevStateValues();
        if (move == 0)
            return;

        color ^= 1;

        int ip = move & 63;
        int fp = (move >> 6) & 63;
        int ep = csep & 127;

        ulong ipos = 1UL << ip;
        ulong fpos = 1UL << fp;

        int own = Own();
        int emy = Emy();

        int ipt = ((move >> 12) & 7) ^ own;
        int fpt = ((move >> 15) & 7) ^ emy;

        if (fpt == 8) fpt = 0;
        
        board[ip] = ipt;
        board[fp] = fpt;

        if (fpt > 0)
        {
            pieces[fpt] ^= fpos;
            pieces[emy + 7] ^= fpos;
        }

        pieces[ipt] ^= ipos ^ fpos;
        pieces[own + 7] ^= ipos ^ fpos;

        if ((ipt & 7) == 1)
        {
            if (fp == ep)
            {
                int pawnFp = ep - 8 * (2 * color - 1);
                pieces[emy + 1] ^= 1UL << pawnFp;
                pieces[emy + 7] ^= 1UL << pawnFp;
                board[pawnFp] = emy + 1;
            }
            else if ((fpos & Bitboards.Rank18) != 0)
            {
                ipt = (((move >> 18) & 3) + 2) + own;
                pieces[own + 1] ^= fpos;
                pieces[ipt] ^= fpos;
            }
        }

        if ((ipt & 7) == 6 && (Mathf.Abs(ip - fp) == 2)) {
            MakeMoveCastling(ip, fp, 0);
            return;
        }
    }

    private void
    MakeMoveCastling(int ip, int fp, int MakeMoveCall)
    {
        int own = Own();

        ulong rooksIndexes =
            (fp > ip ? 160UL : 9UL) << (56 * (color ^ 1));

        pieces[own + 4] ^= rooksIndexes;
        pieces[own + 7] ^= rooksIndexes;

        int flask = (MakeMoveCall ^ 1) * (own + 4);

        if (fp > ip)
        {
            board[ip + 3] = flask;
            board[ip + 1] = (own + 4) ^ flask;
        }
        else
        {
            board[ip - 4] = flask;
            board[ip - 1] = (own + 4) ^ flask;
        }

        if (MakeMoveCall == 1)
        {
            pieces[own + 6] ^= (1UL << ip) ^ (1UL << fp);
            pieces[own + 7] ^= (1UL << ip) ^ (1UL << fp);
            color ^= 1;

            int p1 = LsbIdx(rooksIndexes);
            int p2 = MsbIdx(rooksIndexes);
            hashvalue ^= TT.HashUpdate(own + 6, ip) ^ TT.HashUpdate(own + 6, fp);
            hashvalue ^= TT.HashUpdate(own + 4, p1) ^ TT.HashUpdate(own + 4, p2);
            hashvalue ^= TT.HashIndex[0];
        }
    }

    private void
    MakeMoveCornerRook(int piece, int square)
    {
        ulong CORNER_SQUARES = 0x8100000000000081UL;
        ulong piecePos = 1UL << square;

        if (((piecePos & CORNER_SQUARES) != 0) && (piece == 4))
        {
            int oldCsep = csep;
            int y = (square + 1) >> 3;
            int z  = y + (y < 7 ? 9 : 0);
            csep &= 2047 ^ (1 << z);

            hashvalue ^= TT.HashIndex[(oldCsep >> 7) + TT.CastleBase];
            hashvalue ^= TT.HashIndex[(csep >> 7) + TT.CastleBase];
        }
    }

    private void
    StoreCurrentStateValues(int move)
    {
        prevMoves.Add(move);
        prevCseps.Add(csep);
        prevHashKeys.Add(hashvalue);
    }

    private int
    RetrievePrevStateValues()
    {
        int n = prevMoves.Count;
        if (n == 0)
            return 0;
        
        int lastMove = prevMoves[n - 1];
        csep = prevCseps[n - 1];
        hashvalue = prevHashKeys[n - 1];

        prevMoves.RemoveAt(n - 1);
        prevCseps.RemoveAt(n - 1);
        prevHashKeys.RemoveAt(n - 1);

        return lastMove;
    }

    #endregion


    #region Move Generation

    public ulong
    Pawn(int side)
    { return pieces[side + 1]; }

    public ulong
    Bishop(int side)
    { return pieces[side + 2]; }

    public ulong
    Knight(int side)
    { return pieces[side + 3]; }

    public ulong
    Rook(int side)
    { return pieces[side + 4]; }

    public ulong
    Queen(int side)
    { return pieces[side + 5]; }

    public ulong
    King(int side)
    { return pieces[side + 6]; }

    public ulong
    All(int side)
    { return pieces[side + 7]; }

    public ulong
    All()
    { return pieces[7] | pieces[15]; }

    public int
    Own()
    { return color << 3; }

    public int
    Emy()
    { return Own() ^ 8; }
    
    #endregion

}

