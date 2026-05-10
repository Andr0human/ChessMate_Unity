using System.Collections.Generic;
using TMPro;
using UnityEngine;


public class ArenaHud : MonoBehaviour
{
    [Header("Top status strip")]
    public TextMeshProUGUI RoundText;
    public TextMeshProUGUI ScoreText;
    public TextMeshProUGUI EtaText;

    [Header("Engine name labels (frame the board)")]
    public TextMeshProUGUI WhiteEngineLabel;
    public TextMeshProUGUI BlackEngineLabel;

    [Header("Side panel")]
    public TextMeshProUGUI MoveListText;
    public TextMeshProUGUI AnomalyText;

    [Header("Optional containers to toggle on InitArena")]
    public GameObject[] LiveOnlyObjects;

    private readonly List<string> anomalies = new List<string>();
    private const int MaxAnomalies = 8;


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
        if (RoundText != null)
            RoundText.text = $"Round {current} / {total}";
    }


    public void
    SetScore(string engine1, int e1Wins, int e2Wins, int draws, string engine2)
    {
        if (ScoreText != null)
            ScoreText.text = $"{engine1}  {e1Wins} - {e2Wins}  {engine2}   ({draws} draws)";
    }


    public void
    SetEta(string text)
    {
        if (EtaText != null)
            EtaText.text = "ETA  " + text;
    }


    public void
    SetEngineLabels(string white, string black)
    {
        if (WhiteEngineLabel != null) WhiteEngineLabel.text = white;
        if (BlackEngineLabel != null) BlackEngineLabel.text = black;
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
