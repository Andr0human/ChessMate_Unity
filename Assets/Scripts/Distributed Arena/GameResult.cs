// A finished game, packaged by a worker thread for the main thread to tally,
// log, and display. Everything here is captured at game completion BEFORE the
// worker reuses its HeadlessMatchManager — the live ChessBoard / MatchData get
// overwritten on the next PlayGame(), so the PGN body, ply count, and the
// anomaly remark must all be snapshotted into this object, not referenced.
//
// Crucially it carries `WhiteEngineIndex` explicitly. The sequential
// ArenaScoreSheet infers "engine 1 played white" from `results.Count % 2`,
// which only holds when games finish in dispatch order. Under parallel,
// out-of-order completion that inference is wrong, so attribution travels with
// the result instead.
public class GameResult
{
    // Tournament-engine index (0 or 1) that played each colour this game.
    public int WhiteEngineIndex;
    public int BlackEngineIndex => WhiteEngineIndex ^ 1;

    // White-relative outcome, same convention as Arena.GetResultFromState:
    // +1 white win, 0 draw, -1 black win.
    public int          Result;
    public GameEndState State;
    public int          Prediction;   // EndPrediction (+1/-1/2/0), for accuracy tracking

    public int PairId;
    public int GameInPair;            // 0 = engine A white, 1 = engine B white
    public int WorkerId;

    // Stable, gap-free, order-independent game number for PGN/CSV naming.
    public int GameNumber => PairId * 2 + GameInPair;

    public int    Plies;              // Data.MoveCount()
    public string StartFen;           // Data.StartFen()
    public string MoveListBody;       // Data.GetPgnMoveText() — clean SAN for .pgn
    public string Remark;             // anomaly tags (see ArenaRemark), "" if clean

    // True when the game was decided by a flag, not on the board — the engine
    // whose clock expired is BlackEngineIndex for WhiteWinsOnTime and
    // WhiteEngineIndex for BlackWinsOnTime.
    public bool FlaggedOnTime =>
        State == GameEndState.WhiteWinsOnTime || State == GameEndState.BlackWinsOnTime;
}
