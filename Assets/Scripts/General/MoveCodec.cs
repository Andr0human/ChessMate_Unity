using System.Text;

// Bidirectional codec between Unity's packed-int move encoding and UCI
// long-algebraic strings (e.g. "e2e4", "e7e8q").
//
// Packed-move bit layout (matches MoveGenerator.cs / HumanPlayer.cs):
//   bits  0-5  : init square  (0..63)
//   bits  6-11 : final square (0..63)
//   bits 12-14 : moving piece type (1=P, 2=B, 3=N, 4=R, 5=Q, 6=K)
//   bits 15-17 : captured piece type (0 if none)
//   bits 18-19 : promotion piece (0=B, 1=N, 2=R, 3=Q)
//   bit  20    : moving color (1 = white, 0 = black)
public static class MoveCodec
{
    private const ulong Rank18 = 0xFF000000000000FFUL;

    // Packs a move from board square indices, reading moving/captured piece
    // type and color from the current position. promo is the promotion piece
    // (0=B, 1=N, 2=R, 3=Q); leave at 0 for non-promotions. Single source of
    // truth for the packed-int layout — every encoder routes through here.
    public static int Encode(ref ChessBoard pos, int ip, int fp, int promo = 0)
    {
        int ipt = pos.board[ip] & 7;
        int fpt = pos.board[fp] & 7;

        int posBits   = (fp << 6) | ip;
        int typeBits  = (fpt << 15) | (ipt << 12);
        int colorBit  = pos.color << 20;
        int promoBits = promo << 18;

        return promoBits | colorBit | typeBits | posBits;
    }

    public static string EncodeToUci(int packedMove)
    {
        if (packedMove == 0) return "0000";

        int ip = packedMove & 63;
        int fp = (packedMove >> 6) & 63;

        int ipCol = ip & 7;
        int ipRow = ip >> 3;
        int fpCol = fp & 7;
        int fpRow = fp >> 3;

        var sb = new StringBuilder(5);
        sb.Append((char)('a' + ipCol));
        sb.Append((char)('1' + ipRow));
        sb.Append((char)('a' + fpCol));
        sb.Append((char)('1' + fpRow));

        int pt = (packedMove >> 12) & 7;
        if (pt == 1 && ((1UL << fp) & Rank18) != 0)
        {
            int ppt = (packedMove >> 18) & 3;
            // 0=B, 1=N, 2=R, 3=Q
            char[] promoChars = { 'b', 'n', 'r', 'q' };
            sb.Append(promoChars[ppt]);
        }

        return sb.ToString();
    }

    // Converts a UCI string to a Unity packed-int move using the current
    // board position to determine piece type, captured piece, and color.
    public static int DecodeFromUci(string uci, ref ChessBoard pos)
    {
        if (string.IsNullOrEmpty(uci) || uci.Length < 4) return 0;

        int ip = (uci[0] - 'a') + (uci[1] - '1') * 8;
        int fp = (uci[2] - 'a') + (uci[3] - '1') * 8;

        int promo = 0;
        if (uci.Length >= 5)
        {
            switch (uci[4])
            {
                case 'b': promo = 0; break;
                case 'n': promo = 1; break;
                case 'r': promo = 2; break;
                case 'q': promo = 3; break;
                default:  promo = 3; break;
            }
        }

        return Encode(ref pos, ip, fp, promo);
    }
}
