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
        List<int> encoded_line = new List<int>();
        ChessBoard board = new ChessBoard(startFen);

        foreach (string move in moves)
        {
            int e_move = MoveCodec.DecodeFromUci(move, ref board);
            encoded_line.Add(e_move);
            board.MakeMove(e_move);
        }
        return encoded_line;
    }


    public void
    GetOpeningLines(string file_path)
    {
        string path = Application.streamingAssetsPath + "/Utility/" + file_path + ".opening";
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

        string book_path = Application.streamingAssetsPath + "/Utility/Opening Book.opening";
        if (!File.Exists(book_path))
        {
            UnityEngine.Debug.LogWarning("No opening book found at " + book_path);
            return;
        }

        string[] lines = File.ReadAllLines(book_path);

        foreach (string line in lines)
        {
            string[] moves = line.Split();
            ChessBoard position = new ChessBoard(startFen);

            foreach (string move in moves)
            {
                ulong key = position.hashvalue;
                if (!Book.ContainsKey(key))
                    Book[key] = new List<int>();

                int e_move = MoveCodec.DecodeFromUci(move, ref position);

                if (Book[key].Contains(e_move) == false)
                    Book[key].Add(e_move);
                position.MakeMove(e_move);
            }
        }
        UnityEngine.Debug.Log("Opening Book Generated!");
    }

    public bool
    PositionInOpeningBook(ref ChessBoard __pos)
    {
        ulong key = __pos.GenerateHashKey();

        return Book.ContainsKey(key) ?
            PositionValidityCheck(key, ref __pos) : false;
    }

    private bool
    PositionValidityCheck(ulong key, ref ChessBoard __pos)
    {
        List<int> moves_from_book = Book[key];
        MoveList move_list = mg.GenerateMoves(ref __pos);

        foreach (int move in moves_from_book)
            if (move_list.ContainsMove(move) == false) return false;

        return true;
    }
    
    public int
    PlayBookMove(ref ChessBoard __pos) 
    {
        ulong key = __pos.GenerateHashKey();
        List<int> moves = Book[key];

        int random_index = UnityEngine.Random.Range(0, moves.Count);
        return moves[random_index];
    }

}

