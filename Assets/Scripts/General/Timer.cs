using UnityEngine;
using TMPro;

public class Timer : MonoBehaviour
{
    public float AllotedTimePerSide = 60f;
    public float IncrementTime = 1f;

    public float[] ChessClocks;

    [SerializeField] private TextMeshProUGUI[] TimeInText;
    [SerializeField] private bool ColorByTimeLeft = true;

    private int Side2Tick = 2;


    private void
    Start()
    {
        ChessClocks = new float[2];
    }

    public void
    Init(int StartingSide)
    {
        Side2Tick = StartingSide;
        ChessClocks[0] = ChessClocks[1] = AllotedTimePerSide;

        ClockUnfreeze();

        if (AllotedTimePerSide != 0)
        {
            TimeInText[0].enabled = true;
            TimeInText[1].enabled = true;
        }
    }

    private void
    Update()
    {
        if (Side2Tick >= 2)
            return;

        ChessClocks[Side2Tick] -= 1f * Time.deltaTime;

        TextColorChange(ref TimeInText[0], ChessClocks[0]);
        TextColorChange(ref TimeInText[1], ChessClocks[1]);

        TimeInText[0].text = RemainingTime(ChessClocks[0]);
        TimeInText[1].text = RemainingTime(ChessClocks[1]);
    }

    public void
    TextColorChange(ref TextMeshProUGUI t, float timeLeft)
    {
        if (!ColorByTimeLeft) return;

        float a = t.color.a;
        if (timeLeft < 15f)
            t.color = new Color(0.844f, 0.086f, 0.0267f, a);
        else if (timeLeft < 45f)
            t.color = new Color(1f, 0.901f, 0.156f, a);
        else
            t.color = new Color(0.1297f, 0.5f, 0.16841f, a);
    }

    public void
    SwitchPlayer()
    {
        if (Side2Tick < 2)
            ChessClocks[Side2Tick] += IncrementTime;
        Side2Tick ^= 1;
    }

    public void
    ClockFreeze()
    {
        if (Side2Tick < 2)
            Side2Tick += 2;
    }

    public void
    ClockUnfreeze()
    {
        if (Side2Tick > 1)
            Side2Tick -= 2;
    }

    public string
    RemainingTime(double timeLeft)
    {
        if (timeLeft < 0.0) timeLeft = 0.0;

        if (timeLeft < 15.0)
            return string.Format("0:{0:00.0}", timeLeft);

        var (hr, mn, sec) = TimeFormat.Split(timeLeft);

        if (hr > 0)
            return string.Format("{0}:{1:00}:{2:00}", hr, mn, sec);
        return string.Format("{0}:{1:00}", mn, sec);
    }

    public void
    SetTime(float perSide, float increment)
    {
        AllotedTimePerSide = perSide;
        IncrementTime = increment;
    }
}
