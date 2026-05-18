using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

    [Header("Post-game review")]
    // Camera that renders the canvas (null for ScreenSpace-Overlay).
    public Camera UICamera;
    // Shown for ReviewCountdownSeconds after a game ends. Click → enter review.
    // Label is driven by SetCountdownLabel each frame.
    public Button ReviewButton;
    public TMP_Text ReviewButtonLabel;
    // Shown once review is entered. OnClick → OnContinueClicked → next game.
    public Button ContinueButton;
    // Optional visual cue overlaid on the board area when scrubbing
    // (ply < total). Toggled by SetReviewPly.
    public GameObject BoardGreyOverlay;
    // Optional "● LIVE" pill shown when viewing the final position.
    public GameObject LivePill;

    // Fired when the user clicks a move in MoveListText. Argument is the
    // number of moves applied (i.e. SeekToPly-compatible).
    public System.Action<int> OnMoveLinkClicked;
    public System.Action      OnContinueClicked;
    public System.Action      OnReviewClicked;

    [Header("Optional containers to toggle on InitArena")]
    public GameObject[] LiveOnlyObjects;

    [Header("Active-side card alpha")]
    [Range(0f, 1f)] public float ActiveCardAlpha = 1.00f;
    [Range(0f, 1f)] public float IdleCardAlpha   = 0.65f;

    private readonly List<string> anomalies = new List<string>();
    private const int MaxAnomalies = 8;

    private int activeSide = -1;
    private Coroutine pulseRoutine;

    private bool reviewMode = false;

    // Arena uses this to decide whether to run the countdown+review flow or
    // fall back to a fixed timed wait. Both buttons must be wired up.
    public bool ReviewSupported => ContinueButton != null && ReviewButton != null;


    private void
    Awake()
    {
        if (ContinueButton != null)
            ContinueButton.onClick.AddListener(() => OnContinueClicked?.Invoke());
        if (ReviewButton != null)
            ReviewButton.onClick.AddListener(() => OnReviewClicked?.Invoke());

        if (BoardGreyOverlay != null) BoardGreyOverlay.SetActive(false);
        if (LivePill         != null) LivePill.SetActive(false);
        if (ContinueButton   != null) ContinueButton.gameObject.SetActive(false);
        if (ReviewButton     != null) ReviewButton.gameObject.SetActive(false);

        // Route pointer clicks on the move list through EventSystem so this
        // works under both legacy Input Manager and the new Input System.
        if (MoveListText != null)
        {
            var fwd = MoveListText.gameObject.GetComponent<MoveListClickForwarder>();
            if (fwd == null) fwd = MoveListText.gameObject.AddComponent<MoveListClickForwarder>();
            fwd.Hud = this;

            // IPointerClickHandler needs the graphic to be raycastable.
            MoveListText.raycastTarget = true;
        }
    }


    // Called by MoveListClickForwarder when the user clicks the move-list TMP.
    internal void
    HandleMoveListClick(PointerEventData ev)
    {
        if (!reviewMode || MoveListText == null) return;

        int linkIndex = TMP_TextUtilities.FindIntersectingLink(
            MoveListText, ev.position, ev.pressEventCamera);
        if (linkIndex < 0) return;

        var linkInfo = MoveListText.textInfo.linkInfo[linkIndex];
        if (int.TryParse(linkInfo.GetLinkID(), out int ply))
            OnMoveLinkClicked?.Invoke(ply);
    }


    public void
    BeginCountdown(float seconds)
    {
        if (ReviewButton != null) ReviewButton.gameObject.SetActive(true);
        SetCountdownLabel(seconds);
    }


    public void
    SetCountdownLabel(float remaining)
    {
        if (ReviewButtonLabel == null) return;
        ReviewButtonLabel.text = $"Review ({remaining:0.0}s)";
    }


    public void
    EndCountdown()
    {
        if (ReviewButton != null) ReviewButton.gameObject.SetActive(false);
    }


    public void
    BeginReview()
    {
        reviewMode = true;
        if (ContinueButton != null) ContinueButton.gameObject.SetActive(true);
        if (LivePill       != null) LivePill.SetActive(true);
        // Idle both sides so the clock pulse stops during review.
        SetActiveSide(-1);
    }


    public void
    EndReview()
    {
        reviewMode = false;
        if (ContinueButton   != null) ContinueButton.gameObject.SetActive(false);
        if (LivePill         != null) LivePill.SetActive(false);
        if (BoardGreyOverlay != null) BoardGreyOverlay.SetActive(false);
    }


    // Drives the grey overlay + LIVE pill while scrubbing. ply == totalPlies
    // means "on the live final position".
    public void
    SetReviewPly(int ply, int totalPlies)
    {
        bool atLive = (ply >= totalPlies);
        if (BoardGreyOverlay != null) BoardGreyOverlay.SetActive(!atLive);
        if (LivePill         != null) LivePill.SetActive(atLive);
    }


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


    private string moveListBase = "";
    private int    hoveredLinkId = -1;


    public void
    SetMoveList(string moves)
    {
        moveListBase = moves ?? "";
        RefreshMoveListText();
    }


    // Called by MoveListClickForwarder on pointer move/exit. ev == null means
    // the pointer left the text — clear hover.
    internal void
    HandleMoveListHover(PointerEventData ev)
    {
        int newId = -1;
        if (ev != null && MoveListText != null)
        {
            int li = TMP_TextUtilities.FindIntersectingLink(
                MoveListText, ev.position, UICamera);
            if (li >= 0)
            {
                var info = MoveListText.textInfo.linkInfo[li];
                int.TryParse(info.GetLinkID(), out newId);
            }
        }
        if (newId != hoveredLinkId)
        {
            hoveredLinkId = newId;
            RefreshMoveListText();
        }
    }


    private void
    RefreshMoveListText()
    {
        if (MoveListText == null) return;
        MoveListText.text = ApplyHoverUnderline(moveListBase, hoveredLinkId);
    }


    // Wraps the hovered link's inner text in <u>…</u>. Underline chosen over
    // <mark> because TMP <mark> rendering is finicky across versions.
    private static string
    ApplyHoverUnderline(string baseText, int linkId)
    {
        if (linkId < 0 || string.IsNullOrEmpty(baseText)) return baseText;

        string startTag = "<link=\"" + linkId + "\">";
        int start = baseText.IndexOf(startTag);
        if (start < 0) return baseText;

        int innerStart = start + startTag.Length;
        int end = baseText.IndexOf("</link>", innerStart);
        if (end < 0) return baseText;

        return baseText.Substring(0, innerStart)
             + "<u>" + baseText.Substring(innerStart, end - innerStart) + "</u>"
             + baseText.Substring(end);
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


// Auto-attached to the MoveListText GameObject. Forwards EventSystem pointer
// clicks back to ArenaHud — works under both legacy and new Input System.
public class MoveListClickForwarder : MonoBehaviour,
    IPointerClickHandler, IPointerMoveHandler, IPointerExitHandler
{
    [HideInInspector] public ArenaHud Hud;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Hud != null) Hud.HandleMoveListClick(eventData);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (Hud != null) Hud.HandleMoveListHover(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (Hud != null) Hud.HandleMoveListHover(null);
    }
}
