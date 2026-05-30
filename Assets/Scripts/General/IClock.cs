// Time-control contract shared by the Unity-scene Timer (frame-driven, runs on
// the main thread) and StopwatchClock (wall-clock-driven, runs on an arena
// worker thread). The match loop and the engine read/drive time through this,
// so neither has to know which flavor it is holding.
//
// Remaining() and Increment() are methods, not properties, on purpose:
//  - StopwatchClock must compute the active side's live elapsed time on read,
//    which a plain `float[] ChessClocks` array field can't do.
//  - Timer already exposes IncrementTime as a serialized public field, and a
//    field can't satisfy an interface property; a method sidesteps the name
//    clash without disturbing Timer's inspector serialization.
public interface IClock
{
    // Live remaining seconds for a side (0 = white, 1 = black). For the side
    // currently ticking this already reflects the time spent so far this turn.
    float Remaining(int side);

    // Fischer increment added to a side's clock after it completes a move.
    float Increment();

    void SetTime(float perSide, float increment);

    // Reset both clocks to the full allotment and start `startingSide` ticking.
    void Init(int startingSide);

    // Bank the moving side's spent time, add its increment, pass the tick on.
    void SwitchPlayer();

    // Pause / resume the active side's tick without changing whose turn it is.
    void ClockFreeze();
    void ClockUnfreeze();
}
