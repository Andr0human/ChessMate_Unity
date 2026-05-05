using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;


public interface IPlayer
{
    IEnumerator Play(ChessBoard position, int last_move);

    (int, float) GetResults();

    void Stop() {}

    bool MoveFound();

    bool ReadOutput();

    void StopReadOutput();
}


public class ChessEngine : IPlayer
{
    private   OpeningBook ob;
    private MoveGenerator mg;
    private static Timer tmr;

    private Process    EngineProcess;
    private  string       EngineName;
    private  string       EnginePath;
    private  string  EngineInputPath;
    private  string EngineOutputPath;

    public bool    FixedMoveTime;
    public bool AllowOpeningBook;

    private   int EngineMove;
    private float EngineEval;

    private int QueryCount;


    public
    ChessEngine(string __engine, string start_fen, int game_no, bool __fixed_move_time=false,
        bool __allow_opening_book=true)
    {
        ob  = GameObject.FindObjectOfType<OpeningBook>();
        tmr = GameObject.FindObjectOfType<Timer>();

        EngineName       = __engine;
        FixedMoveTime    = __fixed_move_time;
        AllowOpeningBook = __allow_opening_book;

        EnginePath       = Application.streamingAssetsPath + "/" + __engine + ".exe";
        EngineInputPath  = Application.streamingAssetsPath + "/" + __engine +  ".in";
        EngineOutputPath = Application.streamingAssetsPath + "/" + __engine + ".out";

        QueryCount = 1;

        string EngineLogFolder = Application.streamingAssetsPath + "/arena/logs_" + EngineName;
        string EngineLogPath =
            EngineLogFolder + "/game_" + game_no.ToString() + ".log";

        // Create input and output files for commands
        if (!File.Exists( EngineInputPath)) File.Create( EngineInputPath).Dispose();
        if (!File.Exists(EngineOutputPath)) File.Create(EngineOutputPath).Dispose();
        if (!File.Exists(EngineOutputPath)) File.Create(EngineOutputPath).Dispose();

        if (!Directory.Exists(EngineLogFolder))
            Directory.CreateDirectory(EngineLogFolder);

        File.WriteAllText(EngineInputPath , "");
        File.WriteAllText(EngineOutputPath, "");

        EngineProcess = new Process();
        ProcessStartInfo startInfo = new ProcessStartInfo(EnginePath)
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "play"
                + " log "    + EngineLogPath
                + " input "  + EngineInputPath
                + " output " + EngineOutputPath
                + " position \"" + start_fen + "\"",
            WorkingDirectory = Application.streamingAssetsPath,
        };

        EngineProcess = Process.Start(startInfo);
    }


    private static (float, float)
    GetAvailableTime(ref ChessBoard __pos)
    {
        int __side = __pos.color ^ 1;
        return (tmr.ChessClocks[__side], tmr.IncrementTime);
    }


    private static float
    DecideTimeForSearch(ref ChessBoard __pos)
    {
        var (time_left, increment) = GetAvailableTime(ref __pos);

        float max_moves = 32;
        float max_weight = 7880f;
        float current_weight = __pos.PositionWeight();

        float moves_to_go = max_moves -
            (((max_weight - current_weight) / 400f) * 1.3f);

        float search_time = ((time_left + increment) / moves_to_go) + (0.6f * increment);
        
        search_time = Mathf.Min(search_time, 0.62f * time_left);
        return search_time;
    }


    public IEnumerator
    Play(ChessBoard position, int last_move)
    {
        EngineMove = 0;
        EngineEval = 0;

        // Play Book Move if possible
        bool bookAvailable = (ob != null) && (ob.Book != null) && (ob.Book.Count > 0);
        if (AllowOpeningBook && bookAvailable && ob.PositionInOpeningBook(ref position))
        {
            EngineMove = ob.PlayBookMove(ref position);
            EngineEval = 0;
            yield break;
        }

        float search_time = FixedMoveTime ? 0.5f : DecideTimeForSearch(ref position);

        WriteInput(search_time, tmr.ChessClocks[position.color ^ 1], last_move);
        yield return new WaitUntil( ReadOutput );
        QueryCount++;
    }


    private void
    WriteInput(float alloted_time, float time_left, int last_move)
    {
        using (FileStream inputFileStream = new FileStream(EngineInputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (StreamWriter inputFileWriter = new StreamWriter(inputFileStream))
        {
            int INPUT_SIZE = 50;
            string commandline =
                QueryCount.ToString() + " time " + alloted_time.ToString("0.###");

            if (last_move != 0)
                commandline += " moves " + last_move.ToString();

            commandline += " go";
            commandline += new string(' ', INPUT_SIZE - commandline.Length);

            inputFileWriter.WriteLine(commandline);
        }
    }


    public bool
    ReadOutput()
    {
        int OUTPUT_SIZE = 30;
        if ((EngineProcess == null) || (EngineMove == -1))
            return true;

        using (FileStream outputFileStream = new FileStream(EngineOutputPath, FileMode.Open, FileAccess.Read, FileShare.Write))
        using (StreamReader outputFileReader = new StreamReader(outputFileStream))
        {
            string line = outputFileReader.ReadLine();
            if (line == null) return false;
            UnityEngine.Debug.Log(line);
            if (line.Length != OUTPUT_SIZE) return false;

            string[] values = line.Split();

            int returnQuery = int.Parse(values[2]);
            if (returnQuery != QueryCount)
                return false;

            EngineMove = int.Parse(values[0]);
            EngineEval = float.Parse(values[1]);

            return true;
        }
    }


    public void
    Stop()
    {
        if (EngineProcess != null && !EngineProcess.HasExited)
        {
            EngineProcess.CloseMainWindow();
            EngineProcess.WaitForExit();

            EngineProcess.Close();
            EngineProcess.Dispose();
        }

        // Set to null after stopping and disposing to prevent further access.
        EngineProcess = null; 

        // Delete both input and output files along with their meta files
        string  input_meta_file =  EngineInputPath + ".meta";
        string output_meta_file = EngineOutputPath + ".meta";

        if (File.Exists(EngineInputPath)) File.Delete(EngineInputPath);
        if (File.Exists(input_meta_file)) File.Delete(input_meta_file);

        if (File.Exists(EngineOutputPath)) File.Delete(EngineOutputPath);
        if (File.Exists(output_meta_file)) File.Delete(output_meta_file);
    }


    public bool
    MoveFound()
    { return EngineMove > 0; }


    public void
    StopReadOutput()
    { EngineMove = -1; }


    public (int, float)
    GetResults()
    { return (EngineMove, EngineEval); }
}


public class HumanPlayer : IPlayer
{
    private UserInput ui;
    private MoveGenerator mg;

    private   int HumanMove;
    private float HumanEval;

    ChessBoard BoardPosition;


    public
    HumanPlayer()
    {
        ui = GameObject.FindObjectOfType<UserInput>();
        mg = GameObject.FindObjectOfType<MoveGenerator>();
    }


    private int
    GenerateEncodeMoveForUser()
    {
        int color = BoardPosition.color;

        int init_index = ui.InitSquare;
        int dest_index = ui.DestSquare;

        int          piece = BoardPosition.board[init_index] & 7;
        int captured_piece = BoardPosition.board[dest_index] & 7;
        int promoted_piece = ui.PromotedPiece;

        int       pos_bits = (dest_index << 6) | init_index;
        int      type_bits = (captured_piece << 15) | (piece << 12);
        int      color_bit = color << 20;
        int promotion_bits = promoted_piece << 18;

        int move = promotion_bits | color_bit | type_bits | pos_bits;
        return move;
    }


    public (int, float)
    GetResults()
    { return (HumanMove, HumanEval); }


    public IEnumerator
    Play(ChessBoard position, int last_move)
    {
        HumanMove = 0;
        HumanEval = 0;
        BoardPosition = position;

        MoveList movelist = mg.GenerateMoves(ref BoardPosition);

        ui.GetSquares(ref movelist);
        yield return new WaitUntil(() => (ui.InitSquare != -1) && (ui.DestSquare != -1));

        HumanMove = GenerateEncodeMoveForUser();
        HumanEval = 0;
    }


    public bool
    MoveFound()
    { return HumanMove > 0; }


    public void
    StopReadOutput()
    { HumanMove = -1; }


    public bool
    ReadOutput()
    { return true; }
}

