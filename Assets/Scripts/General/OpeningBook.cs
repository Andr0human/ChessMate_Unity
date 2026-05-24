using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class OpeningBook : MonoBehaviour
{
    [SerializeField] private  BoardHandler bh;
    [SerializeField] private MoveGenerator mg;

    private string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public Dictionary<ulong, List<int>> Book;
    public string[] openings;
    private int openingLineNum = 0;

    private void
    Awake()
    {
        TT.Init();
        GetOpeningBook();
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
        string path = Application.streamingAssetsPath + "/Utility/" + filePath + ".opening";
        openings = File.ReadAllLines(path);
    }


    public string
    NextOpening()
    {
        int index = openingLineNum;
        openingLineNum = (openingLineNum + 1) % openings.Length;
        return openings[index];
    }


    public void
    GetOpeningBook()
    {
        Book = new Dictionary<ulong, List<int>>();

        string bookPath = Application.streamingAssetsPath + "/Utility/Opening Book.opening";
        if (!File.Exists(bookPath))
        {
            UnityEngine.Debug.LogWarning("No opening book found at " + bookPath);
            return;
        }

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
        UnityEngine.Debug.Log("Opening Book Generated!");
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
        MoveList moveList = mg.GenerateMoves(ref pos);

        foreach (int move in movesFromBook)
            if (moveList.ContainsMove(move) == false) return false;

        return true;
    }
    
    public int
    PlayBookMove(ref ChessBoard pos) 
    {
        ulong key = pos.GenerateHashKey();
        List<int> moves = Book[key];

        int randomIndex = UnityEngine.Random.Range(0, moves.Count);
        return moves[randomIndex];
    }

}

