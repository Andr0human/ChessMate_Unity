using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class ArenaHud : MonoBehaviour
{
    [Header("Top status strip")]
    public TextMeshProUGUI RoundCurrentText;
    public TextMeshProUGUI RoundTotalText;
    public TextMeshProUGUI Engine1NameText;
    public TextMeshProUGUI Engine2NameText;
    public TextMeshProUGUI ScoreCenterText;
    public TextMeshProUGUI DrawsText;
    public TextMeshProUGUI EtaValueText;

    [Header("Layout roots to force-rebuild after text changes")]
    public RectTransform TopStripRoot;

    [Header("Engine cards")]
    public TextMeshProUGUI WhiteEngineLabel;
    public TextMeshProUGUI BlackEngineLabel;
    // Index 0 = white-side card, 1 = black-side card. Alpha-toggled by SetActiveSide.
    public CanvasGroup[] EngineCardGroups = new CanvasGroup[2];
    // Index 0 = white clock TMP, 1 = black clock TMP. Pulsed for the active side.
    public TextMeshProUGUI[] ClockTexts = new TextMeshProUGUI[2];

    [Header("Active-side name styling")]
    public Color ActiveNameColor = new Color(1.000f, 1.000f, 1.000f, 1f);
    public Color IdleNameColor   = new Color(0.627f, 0.627f, 0.627f, 1f); // #A0A0A0

    [Header("Active clock pulse")]
    public float PulseMinAlpha = 0.35f;
    public float PulseSpeed    = 2.0f; // cycles per second

    [Header("Side panel")]
    public TextMeshProUGUI MoveListText;
    public TextMeshProUGUI AnomalyText;

    [Header("Optional containers to toggle on InitArena")]
    public GameObject[] LiveOnlyObjects;

    [Header("Active-side card alpha")]
    [Range(0f, 1f)] public float ActiveCardAlpha = 1.00f;
    [Range(0f, 1f)] public float IdleCardAlpha   = 0.65f;

    private readonly List<string> anomalies = new List<string>();
    private const int MaxAnomalies = 8;

    private int activeSide = -1;
    private Coroutine pulseRoutine;


    public void
    ShowLive(bool live)
    {
        if (LiveOnlyObjects == null) return;
        foreach (var go in LiveOnlyObjects)
            if (go != null) go.SetActive(live);
    }


    public void
    SetRound(int current, int total)
    {
        if (RoundCurrentText != null) RoundCurrentText.text = current.ToString();
        if (RoundTotalText   != null) RoundTotalText.text   = total.ToString();

        RebuildTopStrip();
    }


    public void
    SetScore(string engine1, int e1Wins, int e2Wins, int draws, string engine2)
    {
        if (Engine1NameText != null) Engine1NameText.text = engine1;
        if (Engine2NameText != null) Engine2NameText.text = engine2;
        if (ScoreCenterText != null) ScoreCenterText.text = $"{e1Wins} — {e2Wins}";
        if (DrawsText       != null) DrawsText.text       = draws == 1 ? "(1 draw)" : $"({draws} draws)";

        RebuildTopStrip();
    }


    public void
    SetEta(string text)
    {
        if (EtaValueText != null) EtaValueText.text = text;

        RebuildTopStrip();
    }


    private void
    RebuildTopStrip()
    {
        if (TopStripRoot != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(TopStripRoot);
    }


    public void
    SetEngineLabels(string white, string black)
    {
        if (WhiteEngineLabel != null) WhiteEngineLabel.text = white;
        if (BlackEngineLabel != null) BlackEngineLabel.text = black;
    }


    public void
    SetActiveSide(int side)
    {
        activeSide = side;

        if (EngineCardGroups != null)
        {
            for (int i = 0; i < EngineCardGroups.Length; i++)
            {
                if (EngineCardGroups[i] == null) continue;
                EngineCardGroups[i].alpha = (i == side) ? ActiveCardAlpha : IdleCardAlpha;
            }
        }

        // Name styling: active = bold + bright, idle = normal + dim.
        StyleName(WhiteEngineLabel, side == 0);
        StyleName(BlackEngineLabel, side == 1);

        // Reset idle clock alpha to fully opaque; the pulse coroutine drives the active one.
        for (int i = 0; i < ClockTexts.Length; i++)
        {
            if (ClockTexts[i] == null) continue;
            if (i != side)
            {
                var c = ClockTexts[i].color;
                ClockTexts[i].color = new Color(c.r, c.g, c.b, 1f);
            }
        }

        if (pulseRoutine != null) StopCoroutine(pulseRoutine);
        if (isActiveAndEnabled) pulseRoutine = StartCoroutine(PulseActiveClock());
    }


    private void
    StyleName(TextMeshProUGUI tmp, bool active)
    {
        if (tmp == null) return;
        tmp.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
        tmp.color     = active ? ActiveNameColor : IdleNameColor;
    }


    private IEnumerator
    PulseActiveClock()
    {
        while (true)
        {
            if (activeSide < 0 || activeSide >= ClockTexts.Length || ClockTexts[activeSide] == null)
                yield break;

            // Sine wave between PulseMinAlpha and 1.0
            float t = (Mathf.Sin(Time.time * PulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
            float a = Mathf.Lerp(PulseMinAlpha, 1f, t);

            var clk = ClockTexts[activeSide];
            var c = clk.color;
            clk.color = new Color(c.r, c.g, c.b, a);

            yield return null;
        }
    }


    private void
    OnDisable()
    {
        if (pulseRoutine != null)
        {
            StopCoroutine(pulseRoutine);
            pulseRoutine = null;
        }
    }


    public void
    SetMoveList(string moves)
    {
        if (MoveListText != null)
            MoveListText.text = moves;
    }


    public void
    AppendAnomaly(int gameNo, int result, string remark)
    {
        if (AnomalyText == null) return;
        if (string.IsNullOrEmpty(remark)) return;

        string r = result ==  1 ? "1-0"
                 : result == -1 ? "0-1"
                 : "1/2";

        anomalies.Add($"g{gameNo} {r}  {remark}");
        while (anomalies.Count > MaxAnomalies)
            anomalies.RemoveAt(0);

        AnomalyText.text = string.Join("\n", anomalies);
    }


    public void
    ResetAnomalies()
    {
        anomalies.Clear();
        if (AnomalyText != null) AnomalyText.text = "";
    }
}
