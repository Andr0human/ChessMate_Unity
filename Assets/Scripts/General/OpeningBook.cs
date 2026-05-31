using System.IO;
using System.Collections.Generic;

// Plain (non-MonoBehaviour) class. The Unity-side concerns it used to carry —
// streamingAssetsPath resolution and book-move randomness — are injected:
// the path comes in via the constructor, randomness uses an owned RNG. A
// main-thread owner (MatchManager) constructs it once and hands it out, so
// the book can be shared read-only across background arena workers.
public class OpeningBook
{
    private string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public Dictionary<ulong, List<int>> Book;
    public string[] openings;
    private int openingLineNum = 0;

    // Resolved on the main thread by the owner and passed in (Unity's
    // Application.streamingAssetsPath can't be read off the main thread).
    private readonly string streamingAssetsPath;

    // Owned RNG for book-move selection. Locked on use so a shared instance
    // stays safe if multiple arena workers pick book moves concurrently.
    private readonly System.Random rng = new System.Random();

    public
    OpeningBook(string streamingAssetsPath)
    {
        this.streamingAssetsPath = streamingAssetsPath;
    }

    public bool
    IsFen(string opening)
    {
        foreach (char ch in opening)
            if (ch == '/') return true;
        return false;
    }


    public List<int>
    ExtractLine(string opening)
    {
        string[] moves = opening.Split();
        List<int> encodedLine = new List<int>();
        ChessBoard board = new ChessBoard(startFen);

        foreach (string move in moves)
        {
            int eMove = MoveCodec.DecodeFromUci(move, ref board);
            encodedLine.Add(eMove);
            board.MakeMove(eMove);
        }
        return encodedLine;
    }


    public void
    GetOpeningLines(string filePath)
    {
        string path = streamingAssetsPath + "/Utility/" + filePath + ".opening";
        openings = File.ReadAllLines(path);
    }


    public string
    NextOpening()
    {
        int index = openingLineNum;
        openingLineNum = (openingLineNum + 1) % openings.Length;
        return openings[index];
    }


    // Loads the opening book from disk. Returns true on success, false if the
    // book file is missing. Either way Book is left non-null (empty on failure)
    // so callers can query it without null checks. The caller decides how to
    // react to a false return (log, show a banner, run book-less, etc.).
    public bool
    GetOpeningBook()
    {
        Book = new Dictionary<ulong, List<int>>();

        string bookPath = streamingAssetsPath + "/Utility/Opening Book.opening";
        if (!File.Exists(bookPath))
            return false;

        string[] lines = File.ReadAllLines(bookPath);

        foreach (string line in lines)
        {
            string[] moves = line.Split();
            ChessBoard position = new ChessBoard(startFen);

            foreach (string move in moves)
            {
                ulong key = position.hashvalue;
                if (!Book.ContainsKey(key))
                    Book[key] = new List<int>();

                int eMove = MoveCodec.DecodeFromUci(move, ref position);

                if (Book[key].Contains(eMove) == false)
                    Book[key].Add(eMove);
                position.MakeMove(eMove);
            }
        }
        return true;
    }

    public bool
    PositionInOpeningBook(ref ChessBoard pos)
    {
        ulong key = pos.GenerateHashKey();

        return Book.ContainsKey(key) ?
            PositionValidityCheck(key, ref pos) : false;
    }

    private bool
    PositionValidityCheck(ulong key, ref ChessBoard pos)
    {
        List<int> movesFromBook = Book[key];
        MoveList moveList = MoveGenerator.GenerateMoves(ref pos);

        foreach (int move in movesFromBook)
            if (moveList.ContainsMove(move) == false) return false;

        return true;
    }
    
    public int
    PlayBookMove(ref ChessBoard pos) 
    {
        ulong key = pos.GenerateHashKey();
        List<int> moves = Book[key];

        int randomIndex;
        lock (rng) { randomIndex = rng.Next(moves.Count); }
        return moves[randomIndex];
    }

}

