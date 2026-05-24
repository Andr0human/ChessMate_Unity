using UnityEngine;


public class MoveGenerator : MonoBehaviour
{
    private LookupTable lt = new LookupTable();

    #region UTILS

    private bool
    IsLegalMove(ChessBoard pos, int move)
    {
        int own = pos.Own();
        int emy = pos.Emy();
        int side = own >> 3;
        int kSq = pos.IndexNo(pos.King(own));

        pos.MakeMove(move);

        ulong apieces = pos.All();

        ulong lineMask = RookAttackedSquares(ref pos, kSq, apieces);
        ulong diagMask = BishopAttackedSquares(ref pos, kSq, apieces);
        ulong knightMask = KnightAttackedSquares(kSq);
        ulong pawnMask = lt.PawnCaptureMasks[side][kSq];

        bool res = (
            (lineMask & (pos.Queen(emy) | pos.Rook(emy)))
          | (diagMask & (pos.Queen(emy) | pos.Bishop(emy)))
          | (knightMask & pos.Knight(emy))
          | (pawnMask & pos.Pawn(emy)) ) == 0;

        pos.UnmakeMove();
        return res;
    }

    private bool
    InCheck(ref ChessBoard pos, int own)
    {
        int emy = own ^ 8;
        int kSq = pos.IndexNo(pos.King(own));

        ulong erq = pos.Queen(emy) | pos.Rook(emy);
        ulong ebq = pos.Queen(emy) | pos.Bishop(emy);

        return (
            (RookLegalMoves(ref pos, kSq) & erq)
          | (BishopLegalMoves(ref pos, kSq) & ebq)
          | (KnightLegalMoves(ref pos, kSq) & pos.Knight(emy))
          | (lt.PawnCaptureMasks[own / 8][kSq] & pos.Pawn(emy))
        ) != 0;
    }

    private void
    AddToMovelist(ref ChessBoard pos, ref MoveList myMoves,
        int ip, ulong endSquares, ulong pinnedSquares)
    {
        int pt = pos.board[ip] & 7;
        ulong ipBit = 1UL << ip;

        while (endSquares != 0)
        {
            int fp = pos.LsbIdx(endSquares);
            int move = MoveCodec.Encode(ref pos, ip, fp);
            endSquares &= endSquares - 1;

            if (((ipBit & pinnedSquares) != 0) && !IsLegalMove(pos, move))
                continue;

            myMoves.Add(move);

            if ((pt == 1) && ((1UL << fp) & Bitboards.Rank18) != 0)
            {
                myMoves.Add(move | 0xC0000);
                myMoves.Add(move | 0x80000);
                myMoves.Add(move | 0x40000);
            }
        }
    }

    public string
    PrintMove(int move, ChessBoard pos)
    {
        if (move == 0)
            return "null";
        
        string IndexToRow(int x) => ((char)(x + 49)).ToString();
        string IndexToCol(int y) => ((char)(y + 97)).ToString();
        string IndexToSquare(int x, int y) => IndexToCol(y) + IndexToRow(x);

        char[] pieceNames = {'B', 'N', 'R', 'Q'};

        int ip = move & 63;
        int fp = (move >> 6) & 63;

        int ipCol = ip & 7;
        int fpCol = fp & 7;

        int ipRow = (ip - ipCol) >> 3;
        int fpRow = (fp - fpCol) >> 3;

        int ipt = ((move >> 12) & 7);
        int fpt = ((move >> 15) & 7);

        ulong apieces = pos.All();

        pos.MakeMove(move);
        string givesCheck = InCheck(ref pos, pos.Own()) ? "+" : "";
        pos.UnmakeMove();

        string captures = "";

        if (ipt == 1)
        {
            string pawnsCaptures =
                Mathf.Abs(ipCol - fpCol) == 1
                ? (IndexToCol(ipCol) + "x") : "";

            string destSquare = IndexToSquare(fpRow, fpCol);

            if (((1UL << fp) & Bitboards.Rank18) == 0)
                return pawnsCaptures + destSquare + givesCheck;
            
            int ppt = (move >> 18) & 3;
            string promotedPiece = "=" + pieceNames[ppt];

            return pawnsCaptures + destSquare + promotedPiece + givesCheck;
        }
        if (ipt == 6)
        {
            if (Mathf.Abs(ipCol - fpCol) == 2)
                return (((1UL << fp) & Bitboards.FileG) != 0 ? "O-O" : "O-O-O") + givesCheck;

            captures = (fpt != 0) ? "x" : "";
            return "K" + captures + IndexToSquare(fpRow, fpCol) + givesCheck;
        }

        int ipts = pos.Own() + ipt;
        ulong pieces = 0;
        ulong ipos = 1UL << ip;

        if (ipt == 2)
            pieces = (BishopAttackedSquares(ref pos, fp, apieces) & pos.pieces[ipts]) ^ ipos;
        else if (ipt == 3)
            pieces = (KnightAttackedSquares(fp) & pos.pieces[ipts]) ^ ipos;
        else if (ipt == 4)
            pieces =   (RookAttackedSquares(ref pos, fp, apieces) & pos.pieces[ipts]) ^ ipos;
        else
            pieces =  (QueenAttackedSquares(ref pos, fp, apieces) & pos.pieces[ipts]) ^ ipos;

        captures = (fpt != 0) ? "x" : "";
        string endPart = captures + IndexToSquare(fpRow, fpCol) + givesCheck;

        if (pieces == 0)
            return pieceNames[ipt - 2] + endPart;

        bool row = true, col = true;

        while (pieces > 0)
        {
            int sq = pos.LsbIdx(pieces);
            pieces &= pieces - 1;

            int sqCol = sq & 7;
            int sqRow = (sq - sqCol) >> 3;

            if (sqCol == ipCol) col = false;
            if (sqRow == ipRow) row = false;
        }

        if (col) return pieceNames[ipt - 2] + IndexToCol(ipCol) + endPart;
        if (row) return pieceNames[ipt - 2] + IndexToRow(ipRow) + endPart;

        return pieceNames[ipt - 2] + IndexToSquare(ipRow, ipCol) + endPart;
    }

    #endregion


    #region Attacked Squares

    private ulong
    PawnsAttackedSquares(ref ChessBoard pos, int side)
    {
        int inc = 2 * (side / 8) - 1;
        ulong pawns = pos.Pawn(side);

        return (side == 8)
             ? ((pawns & 0x7F7F7F7F7F7F00UL) << (8 + inc)) | ((pawns & 0xFEFEFEFEFEFE00UL) << (8 - inc))
             : ((pawns & 0x7F7F7F7F7F7F00UL) >> (8 + inc)) | ((pawns & 0xFEFEFEFEFEFE00UL) >> (8 - inc));
    }

    private ulong
    KingAttackedSquares(ref ChessBoard pos, int side)
    {
        int sq = pos.IndexNo(pos.King(side));
        return lt.KingMasks[sq];
    }

    private ulong
    KnightAttackedSquares(int sq)
    {
        return lt.KnightMasks[sq];
    }

    private ulong
    RookAttackedSquares(ref ChessBoard pos, int sq, ulong apieces)
    {
        ulong ans = lt.RightMasks[sq] ^ lt.LeftMasks[sq] ^ lt.UpMasks[sq] ^ lt.DownMasks[sq];

        ans ^= lt.RightMasks[pos.LsbIdx(lt.RightMasks[sq] & apieces)];
        ans ^= lt.LeftMasks[pos.MsbIdx(lt.LeftMasks[sq] & apieces)];
        ans ^= lt.UpMasks[pos.LsbIdx(lt.UpMasks[sq] & apieces)];
        ans ^= lt.DownMasks[pos.MsbIdx(lt.DownMasks[sq] & apieces)];

        return ans;
    }

    private ulong
    BishopAttackedSquares(ref ChessBoard pos, int sq, ulong apieces)
    {
        ulong ans = lt.UpRightMasks[sq] ^ lt.UpLeftMasks[sq] ^ lt.DownRightMasks[sq] ^ lt.DownLeftMasks[sq];

        ans ^= lt.UpRightMasks[pos.LsbIdx(lt.UpRightMasks[sq] & apieces)];
        ans ^= lt.UpLeftMasks[pos.LsbIdx(lt.UpLeftMasks[sq] & apieces)];
        ans ^= lt.DownRightMasks[pos.MsbIdx(lt.DownRightMasks[sq] & apieces)];
        ans ^= lt.DownLeftMasks[pos.MsbIdx(lt.DownLeftMasks[sq] & apieces)];

        return ans;
    }

    private ulong
    QueenAttackedSquares(ref ChessBoard pos, int sq, ulong apieces)
    {
        return RookAttackedSquares(ref pos, sq, apieces)
           | BishopAttackedSquares(ref pos, sq, apieces);
    }

    #endregion


    #region Legal Squares

    private bool
    EnpassantRecheck(int ip, ref ChessBoard pos)
    {
        int own = pos.Own();
        int emy = pos.Emy();

        int kpos = pos.IndexNo(pos.King(own));
        int eps = pos.csep & ChessBoard.EpMask;
        ulong erq = pos.Queen(emy) | pos.Rook(emy);
        ulong ebq = pos.Queen(emy) | pos.Bishop(emy);
        ulong Ap  = pos.All() ^ ((1UL << ip) | (1UL << (eps - 8 * (2 * pos.color - 1))));

        ulong res  = pos.Msb((    lt.LeftMasks[kpos] & Ap)) | pos.Lsb((lt.RightMasks[kpos] & Ap));
        ulong tmp1 = pos.Msb((lt.DownLeftMasks[kpos] & Ap)) | pos.Lsb((lt.UpRightMasks[kpos] & Ap));
        ulong tmp2 = pos.Lsb((  lt.UpLeftMasks[kpos] & Ap)) | pos.Msb((lt.DownRightMasks[kpos] & Ap));

        if ((res & erq) != 0) return false;
        // Board has to be invalid for this check
        if (((tmp1 | tmp2) & ebq) != 0) return false;

        return true;
    }

    private ulong
    PawnLegalMoves(ref ChessBoard pos, int sq)
    {
        int side = pos.color;
        int eps  = pos.csep & ChessBoard.EpMask;
        int sq2  = sq + 8 * (2 * side - 1);

        ulong freeSq = ~pos.All();
        ulong captSq = pos.All((side ^ 1) * 8);

        ulong Rank2 = (side == 1) ? 65280UL : 71776119061217280UL;
        ulong destSquares = 0;

        destSquares |= lt.PawnMasks[side][sq] & freeSq;
        destSquares |= lt.PawnCaptureMasks[side][sq] & captSq;

        // Double pawn push
        if ((((1UL << sq) & Rank2) != 0) && (((1UL << sq2) & freeSq) != 0))
            destSquares |= lt.PawnMasks[side][sq2] & freeSq;

        // EnPassant Move
        if ((eps != ChessBoard.NoEp) && (((1UL << eps) & lt.PawnCaptureMasks[side][sq]) != 0) && EnpassantRecheck(sq, ref pos))
            destSquares |= 1UL << eps;

        return destSquares;
    }

    private ulong
    KnightLegalMoves(ref ChessBoard pos, int sq)
    {
        return KnightAttackedSquares(sq) & ~pos.All(pos.Own());
    }

    private ulong
    BishopLegalMoves(ref ChessBoard pos, int sq)
    {
        ulong apieces = pos.All();
        ulong ans = BishopAttackedSquares(ref pos, sq, apieces);
        return ans & ~pos.All(pos.Own());
    }

    private ulong
    RookLegalMoves(ref ChessBoard pos, int sq)
    {
        ulong apieces = pos.All();
        ulong ans = RookAttackedSquares(ref pos, sq, apieces);
        return ans & ~pos.All(pos.Own());
    }

    private ulong
    QueenLegalMoves(ref ChessBoard pos, int sq)
    {
        return RookLegalMoves(ref pos, sq) | BishopLegalMoves(ref pos, sq);
    }

    #endregion


    private ulong
    PinnedSquares(ref ChessBoard pos)
    {
        int own = pos.Own();
        int emy = pos.Emy();

        int kp = pos.IndexNo(pos.King(own));
        ulong allPieces = pos.All();
        ulong erq = pos.Queen(emy) | pos.Rook(emy);
        ulong ebq = pos.Queen(emy) | pos.Bishop(emy);

        ulong LineMask =
            lt.UpMasks[kp] | lt.DownMasks[kp] | lt.RightMasks[kp] | lt.LeftMasks[kp];

        ulong DiagMask =
            lt.UpRightMasks[kp] | lt.DownRightMasks[kp] | lt.UpLeftMasks[kp] | lt.DownLeftMasks[kp];

        if ((LineMask & erq) == 0 && (DiagMask & ebq) == 0)
            return 0;

        return GeneratePinnedSquare(pos.Lsb, ref pos, erq, lt.RightMasks)
             | GeneratePinnedSquare(pos.Msb, ref pos, erq, lt.LeftMasks)
             | GeneratePinnedSquare(pos.Lsb, ref pos, erq, lt.UpMasks)
             | GeneratePinnedSquare(pos.Msb, ref pos, erq, lt.DownMasks)
             | GeneratePinnedSquare(pos.Lsb, ref pos, ebq, lt.UpRightMasks)
             | GeneratePinnedSquare(pos.Lsb, ref pos, ebq, lt.UpLeftMasks)
             | GeneratePinnedSquare(pos.Msb, ref pos, ebq, lt.DownRightMasks)
             | GeneratePinnedSquare(pos.Msb, ref pos, ebq, lt.DownLeftMasks);
    }

    private ulong
    GeneratePinnedSquare(System.Func<ulong, ulong> pick, ref ChessBoard pos, ulong emyPiece, ulong[] table)
    {
        int own = pos.Own();
        int kpos = pos.IndexNo(pos.King(own));
        ulong myPieces = pos.All(own);
        ulong list = table[kpos] & pos.All();

        if (pos.PopCount(list) < 2) return 0;
        
        ulong piece1 = pick(list);
        ulong piece2 = pick(list ^ piece1);

        if (((piece1 & pos.All(own)) != 0) && ((piece2 & emyPiece) != 0))
            return piece1;

        return 0;
    }

    public void
    GeneratePieceMoves(ref ChessBoard pos, ref MoveList myMoves, ulong KA, ulong validSquares)
    {
        validSquares = KA * validSquares + (1 - KA) * ~(0UL);
        
        ulong pinnedSquares = PinnedSquares(ref pos);
        ulong myPieces = pos.All(pos.Own()) ^ pos.King(pos.Own());

        while (myPieces != 0)
        {
            int sq = pos.LsbIdx(myPieces);
            int pt = pos.board[sq] & 7;
            myPieces &= myPieces - 1;

            ulong destSquares = 0;

            if (pt == 1) destSquares =   PawnLegalMoves(ref pos, sq);
            if (pt == 2) destSquares = BishopLegalMoves(ref pos, sq);
            if (pt == 3) destSquares = KnightLegalMoves(ref pos, sq);
            if (pt == 4) destSquares =   RookLegalMoves(ref pos, sq);
            if (pt == 5) destSquares =  QueenLegalMoves(ref pos, sq);

            destSquares &= validSquares;

            AddToMovelist(ref pos, ref myMoves, sq, destSquares, pinnedSquares);
        }
    }

    private ulong
    GenerateAttackedSquares(ref ChessBoard pos)
    {
        ulong res = 0;
        int own = pos.Own();
        int emy = pos.Emy();
        ulong apieces = (pos.All(own) | pos.All(emy)) ^ pos.King(own);
        ulong emyPieces = pos.All(emy);

        res |= PawnsAttackedSquares(ref pos, emy) | KingAttackedSquares(ref pos, emy);

        while (emyPieces != 0)
        {
            int sq = pos.LsbIdx(emyPieces);
            int pt = pos.board[sq] & 7;
            emyPieces &= emyPieces - 1;

            if (pt == 2)
                res |= BishopAttackedSquares(ref pos, sq, apieces);
            else if (pt == 3)
                res |= KnightAttackedSquares(sq);
            else if (pt == 4)
                res |=   RookAttackedSquares(ref pos, sq, apieces);
            else if (pt == 5)
                res |=  QueenAttackedSquares(ref pos, sq, apieces);
        }
        return res;
    }


    private (int, ulong)
    KingAttackers(ref ChessBoard pos, ulong attackedSquares)
    {
        int own = pos.Own();
        int emy = pos.Emy();
        int kSq = pos.IndexNo(pos.King(own));

        if ((attackedSquares != 0) && (pos.King(own) & attackedSquares) == 0)
            return (0, attackedSquares);

        ulong apieces = pos.All();
        int attackers = 0;
        ulong attackMask = 0;

        ulong pieces = RookLegalMoves(ref pos, kSq) & (pos.Queen(emy) | pos.Rook(emy));
        while (pieces != 0)
        {
            int pSq = pos.LsbIdx(pieces);
            attackMask |= (RookLegalMoves(ref pos, kSq)
                         &  RookLegalMoves(ref pos, pSq) ) | (1UL << pSq);

            attackers++;
            pieces &= pieces - 1;
        }

        pieces = BishopLegalMoves(ref pos, kSq) & (pos.Queen(emy) | pos.Bishop(emy));
        while (pieces != 0)
        {
            int pSq = pos.LsbIdx(pieces);
            attackMask |= (BishopLegalMoves(ref pos, kSq)
                         &  BishopLegalMoves(ref pos, pSq) ) | (1UL << pSq);

            attackers++;
            pieces &= pieces - 1;
        }

        pieces = KnightLegalMoves(ref pos, kSq) & pos.Knight(emy);
        while (pieces != 0)
        {
            int pSq = pos.LsbIdx(pieces);
            attackMask |= (KnightLegalMoves(ref pos, kSq)
                         &  KnightLegalMoves(ref pos, pSq) ) | (1UL << pSq);

            attackers++;
            pieces &= pieces - 1;
        }

        ulong pawnSquares = lt.PawnCaptureMasks[own / 8][kSq] & pos.Pawn(emy);
        
        if (pawnSquares != 0)
            attackers++;
        attackMask |= pawnSquares;

        return (attackers, attackMask);
    }


    private void
    GenerateKingMoves(ref ChessBoard pos, ref MoveList myMoves, ulong attackedSq)
    {
        int own = pos.Own();
        int kSq = pos.IndexNo(pos.King(own));
        ulong kBit = 1UL << kSq;

        ulong KingMask = lt.KingMasks[kSq];
        ulong apieces = pos.All();

        ulong endSquares = KingMask & (~(pos.All(own) | attackedSq));

        AddToMovelist(ref pos, ref myMoves, kSq, endSquares, 0);

        if (((pos.csep & ChessBoard.CastleMask) == 0) || ((kBit & attackedSq) != 0)) return;

        ulong coveredSquares = apieces | attackedSq;

        int shift      = 56 * (pos.color ^ 1);
        ulong lMidSq = 2UL << shift;
        ulong rSq     = 96UL << shift;
        ulong lSq     = 12UL << shift;

        int kingSide  = 256 << (2 * pos.color);
        int queenSide = 128 << (2 * pos.color);

        endSquares = 0;

        // Can castle kingSide  and no pieces are in-between
        if (((pos.csep & kingSide) != 0) && ((rSq & coveredSquares) == 0))
            endSquares |= 1UL << (6 + shift);

        // Can castle queenSide and no pieces are in-between
        if (((pos.csep & queenSide) != 0) && ((apieces & lMidSq) == 0) && ((lSq & coveredSquares) == 0))
            endSquares |= 1UL << (2 + shift);

        AddToMovelist(ref pos, ref myMoves, kSq, endSquares, 0);
    }


    public MoveList
    GenerateMoves(ref ChessBoard position)
    {
        MoveList myMoves = new MoveList(position.color);

        ulong attackedSquares = GenerateAttackedSquares(ref position);
        var (attackers, validSquares) = KingAttackers(ref position, attackedSquares);
        myMoves.KingAttackers = attackers;

        if (attackers < 2)
            GeneratePieceMoves(ref position, ref myMoves, (ulong)attackers, validSquares);
        
        GenerateKingMoves(ref position, ref myMoves, attackedSquares);
        return myMoves;
    }


    public ulong
    BulkCount(ref ChessBoard pos, int depth)
    {
        if (depth <= 0)
            return 1;

        MoveList myMoves = GenerateMoves(ref pos);

        if (depth == 1) {
            return (ulong)myMoves.moves.Count;
        }
        
        ulong answer = 0;

        foreach (var move in myMoves.moves)
        {
            pos.MakeMove(move);
            answer += BulkCount(ref pos, depth - 1);
            pos.UnmakeMove();
        }

        return answer;
    }

}

