// Shared duration formatting. Single source of truth for the
// seconds → hours/minutes/seconds breakdown that Arena (ETA / tournament
// total) and Timer (clock display) both need.
public static class TimeFormat
{
    // Breaks a non-negative duration into whole hours/minutes/seconds.
    // Negative inputs clamp to zero.
    public static (int hr, int mn, int sec) Split(double totalSeconds)
    {
        int sec = (int)(totalSeconds < 0.0 ? 0.0 : totalSeconds);
        int hr  = sec / 3600;
        sec %= 3600;
        int mn  = sec / 60;
        sec %= 60;
        return (hr, mn, sec);
    }

    // "{h} hr, {m} min, {s} secs" — used for the arena ETA and the
    // tournament-complete total.
    public static string Verbose(double totalSeconds)
    {
        var (hr, mn, sec) = Split(totalSeconds);
        return $"{hr} hr, {mn} min, {sec} secs";
    }
}
