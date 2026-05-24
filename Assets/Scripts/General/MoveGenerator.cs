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
        int k_sq = pos.IndexNo(pos.King(own));

        pos.MakeMove(move);

        ulong apieces = pos.All();

        ulong line_mask = RookAttackedSquares(ref pos, k_sq, apieces);
        ulong diag_mask = BishopAttackedSquares(ref pos, k_sq, apieces);
        ulong knight_mask = KnightAttackedSquares(k_sq);
        ulong pawn_mask = lt.PawnCaptureMasks[side][k_sq];

        bool res = (
            (line_mask & (pos.Queen(emy) | pos.Rook(emy)))
          | (diag_mask & (pos.Queen(emy) | pos.Bishop(emy)))
          | (knight_mask & pos.Knight(emy))
          | (pawn_mask & pos.Pawn(emy)) ) == 0;

        pos.UnmakeMove();
        return res;
    }

    private bool
    InCheck(ref ChessBoard pos, int own)
    {
        int emy = own ^ 8;
        int k_sq = pos.IndexNo(pos.King(own));

        ulong erq = pos.Queen(emy) | pos.Rook(emy);
        ulong ebq = pos.Queen(emy) | pos.Bishop(emy);

        return (
            (RookLegalMoves(ref pos, k_sq) & erq)
          | (BishopLegalMoves(ref pos, k_sq) & ebq)
          | (KnightLegalMoves(ref pos, k_sq) & pos.Knight(emy))
          | (lt.PawnCaptureMasks[own / 8][k_sq] & pos.Pawn(emy))
        ) != 0;
    }

    private void
    AddToMovelist(ref ChessBoard pos, ref MoveList my_moves,
        int ip, ulong endSquares, ulong pinned_squares)
    {
        ulong Rank18 = 18374686479671623935UL;
        int pt = pos.board[ip] & 7;
        ulong ip_bit = 1UL << ip;

        while (endSquares != 0)
        {
            int fp = pos.LsbIdx(endSquares);
            int move = MoveCodec.Encode(ref pos, ip, fp);
            endSquares &= endSquares - 1;

            if (((ip_bit & pinned_squares) != 0) && !IsLegalMove(pos, move))
                continue;

            my_moves.Add(move);

            if ((pt == 1) && ((1UL << fp) & Rank18) != 0)
            {
                my_moves.Add(move | 0xC0000);
                my_moves.Add(move | 0x80000);
                my_moves.Add(move | 0x40000);
            }
        }
    }

    public string
    PrintMove(int move, ChessBoard pos)
    {
        if (move == 0)
            return "null";
        
        string IndexToRow(int __x) => ((char)(__x + 49)).ToString();
        string IndexToCol(int __y) => ((char)(__y + 97)).ToString();
        string IndexToSquare(int __x, int __y) => IndexToCol(__y) + IndexToRow(__x);

        char[] piece_names = {'B', 'N', 'R', 'Q'};

        int ip = move & 63;
        int fp = (move >> 6) & 63;

        int ip_col = ip & 7;
        int fp_col = fp & 7;

        int ip_row = (ip - ip_col) >> 3;
        int fp_row = (fp - fp_col) >> 3;

        int ipt = ((move >> 12) & 7);
        int fpt = ((move >> 15) & 7);

        ulong apieces = pos.All();

        pos.MakeMove(move);
        string gives_check = InCheck(ref pos, pos.Own()) ? "+" : "";
        pos.UnmakeMove();

        string captures = "";

        if (ipt == 1)
        {
            ulong Rank18 = 18374686479671623935UL;
            string pawns_captures =
                Mathf.Abs(ip_col - fp_col) == 1
                ? (IndexToCol(ip_col) + "x") : "";

            string dest_square = IndexToSquare(fp_row, fp_col);

            if (((1UL << fp) & Rank18) == 0)
                return pawns_captures + dest_square + gives_check;
            
            int ppt = (move >> 18) & 3;
            string promoted_piece = "=" + piece_names[ppt];

            return pawns_captures + dest_square + promoted_piece + gives_check;
        }
        if (ipt == 6)
        {
            ulong FileG = 4629771061636907072UL;

            if (Mathf.Abs(ip_col - fp_col) == 2)
                return (((1UL << fp) & FileG) != 0 ? "O-O" : "O-O-O") + gives_check;

            captures = (fpt != 0) ? "x" : "";
            return "K" + captures + IndexToSquare(fp_row, fp_col) + gives_check;
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
        string end_part = captures + IndexToSquare(fp_row, fp_col) + gives_check;

        if (pieces == 0)
            return piece_names[ipt - 2] + end_part;

        bool row = true, col = true;

        while (pieces > 0)
        {
            int __pos = pos.LsbIdx(pieces);
            pieces &= pieces - 1;

            int __pos_col = __pos & 7;
            int __pos_row = (__pos - __pos_col) >> 3;

            if (__pos_col == ip_col) col = false;
            if (__pos_row == ip_row) row = false;
        }

        if (col) return piece_names[ipt - 2] + IndexToCol(ip_col) + end_part;
        if (row) return piece_names[ipt - 2] + IndexToRow(ip_row) + end_part;

        return piece_names[ipt - 2] + IndexToSquare(ip_row, ip_col) + end_part;
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
        int eps = pos.csep & 127;
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
        int eps  = pos.csep & 127;
        int sq2  = sq + 8 * (2 * side - 1);

        ulong free_sq = ~pos.All();
        ulong capt_sq = pos.All((side ^ 1) * 8);

        ulong Rank2 = (side == 1) ? 65280UL : 71776119061217280UL;
        ulong dest_squares = 0;

        dest_squares |= lt.PawnMasks[side][sq] & free_sq;
        dest_squares |= lt.PawnCaptureMasks[side][sq] & capt_sq;

        // Double pawn push
        if ((((1UL << sq) & Rank2) != 0) && (((1UL << sq2) & free_sq) != 0))
            dest_squares |= lt.PawnMasks[side][sq2] & free_sq;

        // EnPassant Move
        if ((eps != 64) && (((1UL << eps) & lt.PawnCaptureMasks[side][sq]) != 0) && EnpassantRecheck(sq, ref pos))
            dest_squares |= 1UL << eps;

        return dest_squares;
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
        ulong _Ap = pos.All();
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
    GeneratePinnedSquare(System.Func<ulong, ulong> __f, ref ChessBoard pos, ulong emyPiece, ulong[] table)
    {
        int own = pos.Own();
        int kpos = pos.IndexNo(pos.King(own));
        ulong my_pieces = pos.All(own);
        ulong list = table[kpos] & pos.All();

        if (pos.PopCount(list) < 2) return 0;
        
        ulong piece1 = __f(list);
        ulong piece2 = __f(list ^ piece1);

        if (((piece1 & pos.All(own)) != 0) && ((piece2 & emyPiece) != 0))
            return piece1;

        return 0;
    }

    public void
    GeneratePieceMoves(ref ChessBoard pos, ref MoveList my_moves, ulong KA, ulong valid_squares)
    {
        valid_squares = KA * valid_squares + (1 - KA) * ~(0UL);
        
        ulong pinned_squares = PinnedSquares(ref pos);
        ulong my_pieces = pos.All(pos.Own()) ^ pos.King(pos.Own());

        while (my_pieces != 0)
        {
            int sq = pos.LsbIdx(my_pieces);
            int pt = pos.board[sq] & 7;
            my_pieces &= my_pieces - 1;

            ulong dest_squares = 0;

            if (pt == 1) dest_squares =   PawnLegalMoves(ref pos, sq);
            if (pt == 2) dest_squares = BishopLegalMoves(ref pos, sq);
            if (pt == 3) dest_squares = KnightLegalMoves(ref pos, sq);
            if (pt == 4) dest_squares =   RookLegalMoves(ref pos, sq);
            if (pt == 5) dest_squares =  QueenLegalMoves(ref pos, sq);

            dest_squares &= valid_squares;

            AddToMovelist(ref pos, ref my_moves, sq, dest_squares, pinned_squares);
        }
    }

    private ulong
    GenerateAttackedSquares(ref ChessBoard pos)
    {
        ulong res = 0;
        int own = pos.Own();
        int emy = pos.Emy();
        ulong apieces = (pos.All(own) | pos.All(emy)) ^ pos.King(own);
        ulong emy_pieces = pos.All(emy);

        res |= PawnsAttackedSquares(ref pos, emy) | KingAttackedSquares(ref pos, emy);

        while (emy_pieces != 0)
        {
            int sq = pos.LsbIdx(emy_pieces);
            int pt = pos.board[sq] & 7;
            emy_pieces &= emy_pieces - 1;

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
    KingAttackers(ref ChessBoard pos, ulong attacked_squares)
    {
        int own = pos.Own();
        int emy = pos.Emy();
        int k_sq = pos.IndexNo(pos.King(own));

        if ((attacked_squares != 0) && (pos.King(own) & attacked_squares) == 0)
            return (0, attacked_squares);

        ulong apieces = pos.All();
        int attackers = 0;
        ulong attack_mask = 0;

        ulong pieces = RookLegalMoves(ref pos, k_sq) & (pos.Queen(emy) | pos.Rook(emy));
        while (pieces != 0)
        {
            int p_sq = pos.LsbIdx(pieces);
            attack_mask |= (RookLegalMoves(ref pos, k_sq)
                         &  RookLegalMoves(ref pos, p_sq) ) | (1UL << p_sq);

            attackers++;
            pieces &= pieces - 1;
        }

        pieces = BishopLegalMoves(ref pos, k_sq) & (pos.Queen(emy) | pos.Bishop(emy));
        while (pieces != 0)
        {
            int p_sq = pos.LsbIdx(pieces);
            attack_mask |= (BishopLegalMoves(ref pos, k_sq)
                         &  BishopLegalMoves(ref pos, p_sq) ) | (1UL << p_sq);

            attackers++;
            pieces &= pieces - 1;
        }

        pieces = KnightLegalMoves(ref pos, k_sq) & pos.Knight(emy);
        while (pieces != 0)
        {
            int p_sq = pos.LsbIdx(pieces);
            attack_mask |= (KnightLegalMoves(ref pos, k_sq)
                         &  KnightLegalMoves(ref pos, p_sq) ) | (1UL << p_sq);

            attackers++;
            pieces &= pieces - 1;
        }

        ulong pawn_squares = lt.PawnCaptureMasks[own / 8][k_sq] & pos.Pawn(emy);
        
        if (pawn_squares != 0)
            attackers++;
        attack_mask |= pawn_squares;

        return (attackers, attack_mask);
    }


    private void
    GenerateKingMoves(ref ChessBoard pos, ref MoveList my_moves, ulong attacked_sq)
    {
        int own = pos.Own();
        int k_sq = pos.IndexNo(pos.King(own));
        ulong k_bit = 1UL << k_sq;

        ulong KingMask = lt.KingMasks[k_sq];
        ulong apieces = pos.All();

        ulong end_squares = KingMask & (~(pos.All(own) | attacked_sq));

        AddToMovelist(ref pos, ref my_moves, k_sq, end_squares, 0);

        if (((pos.csep & 1920) == 0) || ((k_bit & attacked_sq) != 0)) return;

        ulong covered_squares = apieces | attacked_sq;

        int shift      = 56 * (pos.color ^ 1);
        ulong l_mid_sq = 2UL << shift;
        ulong r_sq     = 96UL << shift;
        ulong l_sq     = 12UL << shift;

        int king_side  = 256 << (2 * pos.color);
        int queen_side = 128 << (2 * pos.color);

        end_squares = 0;

        // Can castle king_side  and no pieces are in-between
        if (((pos.csep & king_side) != 0) && ((r_sq & covered_squares) == 0))
            end_squares |= 1UL << (6 + shift);

        // Can castle queen_side and no pieces are in-between
        if (((pos.csep & queen_side) != 0) && ((apieces & l_mid_sq) == 0) && ((l_sq & covered_squares) == 0))
            end_squares |= 1UL << (2 + shift);

        AddToMovelist(ref pos, ref my_moves, k_sq, end_squares, 0);
    }


    public MoveList
    GenerateMoves(ref ChessBoard position)
    {
        MoveList my_moves = new MoveList(position.color);

        ulong attacked_squares = GenerateAttackedSquares(ref position);
        var (attackers, valid_squares) = KingAttackers(ref position, attacked_squares);
        my_moves.KingAttackers = attackers;

        if (attackers < 2)
            GeneratePieceMoves(ref position, ref my_moves, (ulong)attackers, valid_squares);
        
        GenerateKingMoves(ref position, ref my_moves, attacked_squares);
        return my_moves;
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

