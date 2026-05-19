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
    // Index 0 = white-side card, 1 = black-side card. Neutralised to alpha 1
    // by ApplyCardTheme — the chess.com-style theme no longer dims cards.
    public CanvasGroup[] EngineCardGroups = new CanvasGroup[2];
    // Index 0 = white clock TMP, 1 = black clock TMP. Pulsed for the active side.
    public TextMeshProUGUI[] ClockTexts = new TextMeshProUGUI[2];

    [Header("Engine card theme — chess.com-style")]
    // Background Images of the white-slot (bottom) and black-slot (top) cards.
    // Cards are positionally fixed; only their text contents swap per game.
    public Image WhiteCardBg;
    public Image BlackCardBg;
    public Color WhiteCardColor = new Color(0.925f, 0.925f, 0.925f, 1f); // #ECECEC
    public Color BlackCardColor = new Color(0.122f, 0.122f, 0.122f, 1f); // #1F1F1F

    [Header("Engine card text colors")]
    // Engine names carry a fixed colour — dark on the white card, light on
    // the black card. No active/idle restyle; the clock pulse is the only
    // side-to-move cue.
    public Color WhiteCardTextColor = new Color(0.102f, 0.102f, 0.102f, 1f); // #1A1A1A
    public Color BlackCardTextColor = new Color(1.000f, 1.000f, 1.000f, 1f);

    [Header("Active clock pulse")]
    // Both clocks sit in their own dark sub-box, so they stay light text
    // regardless of which card carries them.
    public Color ClockTextColor = new Color(1f, 1f, 1f, 1f);
    public float PulseMinAlpha  = 0.35f;
    public float PulseSpeed     = 2.0f; // cycles per second

    [Header("Clock icon (spins on the side to move)")]
    // Index 0 = white card icon, 1 = black card icon. Optional — a small
    // clock sprite placed before the clock text. Only the active side spins.
    public Image[] ClockIcons = new Image[2];
    public float ClockIconSpinSpeed = 90f; // degrees/sec, clockwise

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

    [Header("Result card (per-game + tournament-end)")]
    // Shared card container — no dim backdrop, sits in a fixed slot. Hosts
    // either GameResultGroup or SummaryGroup depending on which Show* runs.
    public GameObject ResultCardRoot;
    // Per-game layout: title + remark + the Review button/countdown.
    public GameObject GameResultGroup;
    // Tournament-end layout: final summary + Open PGNs button.
    public GameObject SummaryGroup;
    public TextMeshProUGUI ResultTitleText;
    public TextMeshProUGUI ResultRemarkText;
    public TextMeshProUGUI SummaryText;
    public Button OpenPgnButton;

    // Fired when the user clicks a move in MoveListText. Argument is the
    // number of moves applied (i.e. SeekToPly-compatible).
    public System.Action<int> OnMoveLinkClicked;
    public System.Action      OnContinueClicked;
    public System.Action      OnReviewClicked;
    public System.Action      OnOpenPgnClicked;

    [Header("Optional containers to toggle on InitArena")]
    public GameObject[] LiveOnlyObjects;

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
        if (OpenPgnButton != null)
            OpenPgnButton.onClick.AddListener(() => OnOpenPgnClicked?.Invoke());

        if (BoardGreyOverlay != null) BoardGreyOverlay.SetActive(false);
        if (LivePill         != null) LivePill.SetActive(false);
        if (ContinueButton   != null) ContinueButton.gameObject.SetActive(false);
        if (ReviewButton     != null) ReviewButton.gameObject.SetActive(false);
        if (ResultCardRoot   != null) ResultCardRoot.SetActive(false);

        ApplyCardTheme();

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


    // Per-game result card. title = "Game N · 1-0 — White wins …",
    // remark = the GameRemark string (empty for an unremarkable game).
    public void
    ShowGameResult(string title, string remark)
    {
        if (ResultCardRoot   != null) ResultCardRoot.SetActive(true);
        if (GameResultGroup  != null) GameResultGroup.SetActive(true);
        if (SummaryGroup     != null) SummaryGroup.SetActive(false);
        if (ResultTitleText  != null) ResultTitleText.text  = title;
        if (ResultRemarkText != null)
        {
            ResultRemarkText.text = remark ?? "";
            ResultRemarkText.gameObject.SetActive(!string.IsNullOrEmpty(remark));
        }
    }


    // Tournament-end summary — same card, swapped layout group.
    public void
    ShowSummary(string summary)
    {
        if (ResultCardRoot  != null) ResultCardRoot.SetActive(true);
        if (GameResultGroup != null) GameResultGroup.SetActive(false);
        if (SummaryGroup    != null) SummaryGroup.SetActive(true);
        if (SummaryText     != null) SummaryText.text = summary;
    }


    public void
    HideResultCard()
    {
        if (ResultCardRoot != null) ResultCardRoot.SetActive(false);
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


    // Paints the static card theme: chess.com-style white/dark backgrounds,
    // accent borders (hidden until SetActiveSide), neutral card alpha, and
    // per-card clock base colours.
    private void
    ApplyCardTheme()
    {
        if (WhiteCardBg != null) WhiteCardBg.color = WhiteCardColor;
        if (BlackCardBg != null) BlackCardBg.color = BlackCardColor;

        if (WhiteCardBorder != null)
        {
            WhiteCardBorder.color   = AccentColor;
            WhiteCardBorder.enabled = false;
        }
        if (BlackCardBorder != null)
        {
            BlackCardBorder.color   = AccentColor;
            BlackCardBorder.enabled = false;
        }

        // The theme drops card dimming — clear any stale idle alpha.
        if (EngineCardGroups != null)
            foreach (var g in EngineCardGroups)
                if (g != null) g.alpha = 1f;

        // Engine names: fixed colour per card, no active/idle restyle.
        if (WhiteEngineLabel != null) WhiteEngineLabel.color = WhiteCardTextColor;
        if (BlackEngineLabel != null) BlackEngineLabel.color = BlackCardTextColor;

        // Clock base RGB; the pulse coroutine only drives alpha. Both clocks
        // are light text — each sits in its own dark sub-box.
        SetClockBaseColor(0, ClockTextColor);
        SetClockBaseColor(1, ClockTextColor);
    }


    private void
    SetClockBaseColor(int idx, Color rgb)
    {
        if (ClockTexts == null || idx >= ClockTexts.Length || ClockTexts[idx] == null) return;
        float a = ClockTexts[idx].color.a;
        ClockTexts[idx].color = new Color(rgb.r, rgb.g, rgb.b, a);
    }


    public void
    SetActiveSide(int side)
    {
        activeSide = side;

        // chess.com-style: cards keep full colour; the accent border marks
        // the side to move (side < 0 during review hides both).
        if (WhiteCardBorder != null) WhiteCardBorder.enabled = (side == 0);
        if (BlackCardBorder != null) BlackCardBorder.enabled = (side == 1);

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

        // Reset idle clock icons to upright; the active one spins in PulseActiveClock.
        if (ClockIcons != null)
        {
            for (int i = 0; i < ClockIcons.Length; i++)
            {
                if (ClockIcons[i] != null && i != side)
                    ClockIcons[i].rectTransform.localRotation = Quaternion.identity;
            }
        }

        if (pulseRoutine != null) StopCoroutine(pulseRoutine);
        if (isActiveAndEnabled) pulseRoutine = StartCoroutine(PulseActiveClock());
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

            // Spin the active side's clock icon clockwise (negative z).
            if (ClockIcons != null && activeSide < ClockIcons.Length
                && ClockIcons[activeSide] != null)
            {
                ClockIcons[activeSide].rectTransform.Rotate(
                    0f, 0f, -ClockIconSpinSpeed * Time.deltaTime);
            }

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
