using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Sits on each player's UI panel in the gameplay scene.
/// Holds all UI references for one player slot.
/// GameplayPlayFabManager holds 4 of these (index 0 = local, 1 = partner,
/// 2 = left opponent, 3 = right opponent).
/// </summary>
public class GameplayPlayerData : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== IDENTITY ===")]
    [SerializeField] private TMP_Text  nameText;
    [SerializeField] private TMP_Text  pcText;
    [SerializeField] private Image     avatarImage;
    [SerializeField] private Sprite    defaultAvatar;

    [Header("=== DEALER ICON ===")]
    [SerializeField] private GameObject dealerIcon;

    [Header("=== TRUMP DISPLAY ===")]
    [SerializeField] private GameObject trumpFrame;          // enabled when this player picked trump
    [SerializeField] private Image      trumpClubsIcon;
    [SerializeField] private Image      trumpDiamondsIcon;
    [SerializeField] private Image      trumpHeartsIcon;
    [SerializeField] private Image      trumpSpadesIcon;

    [Header("=== TEAM BADGE ===")]
    [SerializeField] private GameObject teamBadge;           // shown when team mode active
    [SerializeField] private TMP_Text   teamBadgeText;       // "Team" or "Partner"

    [Header("=== GO ALONE BADGE ===")]
    [SerializeField] private GameObject goAloneBadge;        // shown when this player goes alone

    [Header("=== SELECTING TRUMP ===")]
    [SerializeField] private GameObject selectingTrumpIndicator;

    [Header("=== TRICK SCORE ===")]
    [SerializeField] private TMP_Text trickScoreText;

    [Header("=== TIMER VISUAL ===")]
    [SerializeField] private Image timerVisualImage;

    private static readonly Color TIMER_FULL    = Color.white;
    private static readonly Color TIMER_WARNING = Color.yellow;
    private static readonly Color TIMER_URGENT  = Color.red;

    // ─────────────────────────────────────────────────────────────
    //  Runtime Data
    // ─────────────────────────────────────────────────────────────

    public string PlayFabId   { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public int    PokerChips  { get; private set; } = 0;
    public int    Seat        { get; private set; } = -1;
    public bool   IsBot       { get; private set; } = false;

    private Tween  _trumpPulseTween;
    private string _pendingAvatarUrl;

    // ─────────────────────────────────────────────────────────────
    //  Setup
    // ─────────────────────────────────────────────────────────────

    /// <summary>Called by GameplayPlayFabManager after PlayFab data is fetched.</summary>
    public void Setup(string playFabId, string displayName,
                      int pokerChips, int seat, bool isBot = false)
    {
        PlayFabId   = playFabId;
        DisplayName = displayName;
        PokerChips  = pokerChips;
        Seat        = seat;
        IsBot       = isBot;

        RefreshNameText();
        RefreshPCText();
        SetDealer(false);
        SetTrump(' ', false);
        SetTeamBadge(false, "");
        SetGoAlone(false);
        ResetTimerVisual();
        ResetTrickScore();
        SetSelectingTrump(false);
    }

    /// <summary>Load avatar from URL. Falls back to default if empty or failed.</summary>
    public void LoadAvatar(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            ApplyDefaultAvatar();
            return;
        }

        if (!gameObject.activeInHierarchy)
        {
            // Object is inactive — store URL and fetch when it becomes active
            _pendingAvatarUrl = url;
            return;
        }

        _pendingAvatarUrl = null;
        StartCoroutine(FetchAvatar(url));
    }

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(_pendingAvatarUrl))
        {
            StartCoroutine(FetchAvatar(_pendingAvatarUrl));
            _pendingAvatarUrl = null;
        }
    }

    /// <summary>Assigns a local sprite directly — used for bots (no network fetch).</summary>
    public void SetAvatarSprite(Sprite sprite)
    {
        _pendingAvatarUrl = null;
        if (avatarImage != null)
            avatarImage.sprite = sprite != null ? sprite : defaultAvatar;
    }

    // ─────────────────────────────────────────────────────────────
    //  PC Balance
    // ─────────────────────────────────────────────────────────────

    public void SetPokerChips(int amount)
    {
        PokerChips = amount;
        RefreshPCText();
    }

    public void DeductBet(int amount)
    {
        PokerChips = Mathf.Max(0, PokerChips - amount);
        RefreshPCText();
    }

    public void AddWinnings(int amount)
    {
        PokerChips += amount;
        RefreshPCText();
    }

    // ─────────────────────────────────────────────────────────────
    //  Dealer Icon
    // ─────────────────────────────────────────────────────────────

    public void SetDealer(bool isDealer)
    {
        if (dealerIcon != null)
            dealerIcon.SetActive(isDealer);
    }

    // ─────────────────────────────────────────────────────────────
    //  Trump Display
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when trump is selected.
    /// If this player picked trump, frame enables and correct suit icon highlights.
    /// </summary>
    public void SetTrump(char suit, bool isThisPlayerPicker)
    {
        if (trumpFrame != null)
            trumpFrame.SetActive(isThisPlayerPicker);

        // Reset all suit icons to dim
        SetSuitIconAlpha(trumpClubsIcon,    0.05f);
        SetSuitIconAlpha(trumpDiamondsIcon, 0.05f);
        SetSuitIconAlpha(trumpHeartsIcon,   0.05f);
        SetSuitIconAlpha(trumpSpadesIcon,   0.05f);

        if (!isThisPlayerPicker || suit == ' ' || suit == 'N') return;

        // Highlight selected suit
        switch (suit)
        {
            case 'C': SetSuitIconAlpha(trumpClubsIcon,    1f); break;
            case 'D': SetSuitIconAlpha(trumpDiamondsIcon, 1f); break;
            case 'H': SetSuitIconAlpha(trumpHeartsIcon,   1f); break;
            case 'S': SetSuitIconAlpha(trumpSpadesIcon,   1f); break;
        }
    }

    /// <summary>Clears trump display (between hands).</summary>
    public void ClearTrump()
    {
        SetTrump(' ', false);
    }

    public void SetSelectingTrump(bool selecting)
    {
        if (selectingTrumpIndicator == null) return;

        if (selecting)
        {
            selectingTrumpIndicator.SetActive(true);
            _trumpPulseTween?.Kill();
            selectingTrumpIndicator.transform.localScale = Vector3.one;
            _trumpPulseTween = selectingTrumpIndicator.transform
                .DOScale(1.1f, 0.4f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }
        else
        {
            _trumpPulseTween?.Kill();
            _trumpPulseTween = null;
            if (selectingTrumpIndicator != null)
            {
                selectingTrumpIndicator.transform.localScale = Vector3.one;
                selectingTrumpIndicator.SetActive(false);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Team / Go Alone Badges
    // ─────────────────────────────────────────────────────────────

    public void SetTeamBadge(bool visible, string label)
    {
        if (teamBadge != null)     teamBadge.SetActive(visible);
        if (teamBadgeText != null) teamBadgeText.text = label;
    }

    public void SetGoAlone(bool visible)
    {
        if (goAloneBadge != null)
            goAloneBadge.SetActive(visible);
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal Helpers
    // ─────────────────────────────────────────────────────────────

    private void RefreshNameText()
    {
        if (nameText != null)
            nameText.text = string.IsNullOrEmpty(DisplayName) ? "---" : DisplayName;
    }

    private void RefreshPCText()
    {
        if (pcText != null)
            pcText.text = PokerChips.ToString("N0") + " PC";
    }

    private void ApplyDefaultAvatar()
    {
        if (avatarImage != null && defaultAvatar != null)
            avatarImage.sprite = defaultAvatar;
    }

    private void SetSuitIconAlpha(Image icon, float alpha)
    {
        if (icon == null) return;
        Color c = icon.color;
        c.a        = alpha;
        icon.color = c;
    }

    private IEnumerator FetchAvatar(string url)
    {
        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Texture2D tex = DownloadHandlerTexture.GetContent(www);
            Sprite sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));

            if (avatarImage != null)
                avatarImage.sprite = sprite;
        }
        else
        {
            ApplyDefaultAvatar();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Trick Score
    // ─────────────────────────────────────────────────────────────

    public void SetTrickScore(int tricks)
    {
        if (trickScoreText != null)
            trickScoreText.text = tricks.ToString();
    }

    public void ResetTrickScore()
    {
        if (trickScoreText != null)
            trickScoreText.text = "0";
    }

    /// <summary>
    /// Called every second by TurnTimerController.
    /// remaining: current seconds left (30 down to 0).
    /// duration: total turn duration (30).
    /// </summary>
    public void UpdateTimerVisual(int remaining, int duration)
    {
        if (timerVisualImage == null) return;

        timerVisualImage.gameObject.SetActive(true);

        // Fill amount — 1 at full, 0 at zero
        float fill = (float)remaining / duration;
        timerVisualImage.fillAmount = fill;

        // Color lerp: white → yellow → red
        if (remaining <= 5)
            timerVisualImage.color = TIMER_URGENT;
        else if (remaining <= 10)
            timerVisualImage.color = Color.Lerp(TIMER_URGENT, TIMER_WARNING,
                (remaining - 5f) / 5f);
        else
            timerVisualImage.color = Color.Lerp(TIMER_WARNING, TIMER_FULL,
                (remaining - 10f) / (duration - 10f));
    }

    /// <summary>Reset and hide timer visual.</summary>
    public void ResetTimerVisual()
    {
        if (timerVisualImage == null) return;

        timerVisualImage.fillAmount = 1f;
        timerVisualImage.color      = TIMER_FULL;
        timerVisualImage.gameObject.SetActive(false);
    }
}
