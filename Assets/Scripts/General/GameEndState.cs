// How a game finished (or that it is still running). Replaces the bare
// integer "state codes" that used to flow from MatchManager.IsGameOver()
// into the Arena/EndScreen reporting. Explicit numeric values are kept
// stable so the codes remain readable in saved logs. Ongoing is 0 so a
// scene-serialized `EndState` field (Unity's default 0) reads as Ongoing.
public enum GameEndState
{
    Ongoing                    =  0,
    WhiteWinsByCheckmate       =  1,
    BlackWinsByCheckmate       =  2,
    DrawByStalemate            =  3,
    DrawByInsufficientMaterial =  4,
    DrawByRepetition           =  5,
    DrawByFiftyMoveRule        =  6,
    WhiteWinsOnTime            =  7,
    BlackWinsOnTime            =  8,
}


public static class GameEndStateExtensions
{
    // Human-readable phrase (no trailing punctuation). The single source of
    // truth for both the single-player EndScreen and the Arena result card.
    public static string
    Describe(this GameEndState state)
    {
        switch (state)
        {
            case GameEndState.WhiteWinsByCheckmate:       return "White wins by checkmate";
            case GameEndState.BlackWinsByCheckmate:       return "Black wins by checkmate";
            case GameEndState.DrawByStalemate:            return "Draw by stalemate";
            case GameEndState.DrawByInsufficientMaterial: return "Draw by insufficient material";
            case GameEndState.DrawByRepetition:           return "Draw by 3-fold repetition";
            case GameEndState.DrawByFiftyMoveRule:        return "Draw by 50-move rule";
            case GameEndState.WhiteWinsOnTime:            return "White wins on time";
            case GameEndState.BlackWinsOnTime:            return "Black wins on time";
            default:                                      return "Game over";
        }
    }
}
