using System.Diagnostics;

// Wall-clock flavor of IClock for arena worker threads, which have no Unity
// frame loop to decrement a Timer each Update(). Elapsed time comes from a
// Stopwatch instead of accumulated Time.deltaTime.
//
// Owned and driven by a single worker thread — Init/SwitchPlayer/Freeze and the
// engine's Remaining() reads all happen on that one thread, sequentially — so
// it needs no locking.
//
// The _side2tick encoding mirrors Timer exactly so behaviour stays in lock-step
// with the scene clock: 0/1 = that side is ticking; 2/3 = frozen (subtract 2
// for the side). The only difference from Timer is *how* time is measured:
// banked-plus-stopwatch here vs. continuous per-frame decrement there.
public class StopwatchClock : IClock
{
    public float AllotedTimePerSide { get; private set; }
    private float _incrementTime;

    // Remaining seconds per side, current as of the last bank point (Init,
    // SwitchPlayer, or ClockFreeze). The actively-ticking side's true remaining
    // is this minus the running stopwatch — see Remaining().
    private readonly float[] _banked = new float[2];

    // 0/1 = that side ticking; 2/3 = frozen (low bit = side). Starts frozen.
    private int _side2tick = 2;

    // Measures time spent by the currently-ticking side since it last started.
    private readonly Stopwatch _sw = new Stopwatch();


    public
    StopwatchClock(float perSide = 60f, float increment = 1f)
    {
        AllotedTimePerSide = perSide;
        _incrementTime     = increment;
    }


    public float
    Remaining(int side)
    {
        float r = _banked[side];
        if (side == _side2tick)   // true only when ticking (< 2) and matches side
            r -= (float)_sw.Elapsed.TotalSeconds;
        return r;
    }


    public float
    Increment()
    { return _incrementTime; }


    public void
    SetTime(float perSide, float increment)
    {
        AllotedTimePerSide = perSide;
        _incrementTime     = increment;
    }


    public void
    Init(int startingSide)
    {
        _banked[0] = _banked[1] = AllotedTimePerSide;
        _side2tick = startingSide;
        StartIfTicking();
    }


    public void
    SwitchPlayer()
    {
        // Bank the moving side's spent time, then credit its increment, then
        // hand the tick to the other side and start its stopwatch. Matches
        // Timer.SwitchPlayer(): increment is added to the side that just moved.
        BankIfTicking();
        if (_side2tick < 2)
            _banked[_side2tick] += _incrementTime;
        _side2tick ^= 1;
        StartIfTicking();
    }


    public void
    ClockFreeze()
    {
        if (_side2tick < 2)
        {
            BankIfTicking();
            _side2tick += 2;
        }
    }


    public void
    ClockUnfreeze()
    {
        if (_side2tick > 1)
        {
            _side2tick -= 2;
            StartIfTicking();
        }
    }


    // Fold the running stopwatch into the active side's banked remaining and
    // stop it. No-op when frozen / between sides.
    private void
    BankIfTicking()
    {
        if (_side2tick < 2)
        {
            _banked[_side2tick] -= (float)_sw.Elapsed.TotalSeconds;
            _sw.Reset();
        }
    }


    private void
    StartIfTicking()
    {
        if (_side2tick < 2)
            _sw.Restart();
    }
}
