using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shown to all players before the match starts.
/// Both players in a pair must vote Yes for team mode to activate.
/// Any No vote → free-for-all mode for that match.
/// Bots auto-vote Yes.
/// </summary>
public class TeamVoteUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== PANEL ===")]
    [SerializeField] private GameObject    rootPanel;
    [SerializeField] private RectTransform panelRect;

    [Header("=== TEXT ===")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text waitingText;

    [Header("=== BUTTONS ===")]
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;

    [Header("=== VOTE STATUS ICONS ===")]
    [Tooltip("4 icons showing each player's vote status (0=local,1=partner,2,3)")]
    [SerializeField] private Image[] voteStatusIcons = new Image[4];
    [SerializeField] private Sprite  votePendingSprite;
    [SerializeField] private Sprite  voteYesSprite;
    [SerializeField] private Sprite  voteNoSprite;

    // ─────────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    private Action _onYes;
    private Action _onNo;
    private bool   _voted = false;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        yesButton?.onClick.AddListener(OnYesClicked);
        noButton?.onClick.AddListener(OnNoClicked);
        rootPanel?.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Show / Hide
    // ─────────────────────────────────────────────────────────────

    public void Show(Action onYes, Action onNo)
    {
        _onYes  = onYes;
        _onNo   = onNo;
        _voted  = false;

        titleText.text  = "Play as Team?";
        bodyText.text   = "Vote YES to play with a partner.\n" +
                          "All players must agree for team mode.\n" +
                          "Any NO vote = free-for-all!";
        waitingText.text = "";

        SetButtonsInteractable(true);
        ResetVoteIcons();

        rootPanel?.SetActive(true);

        if (panelRect != null)
        {
            panelRect.localScale = Vector3.zero;
            panelRect.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack);
        }
    }

    public void Hide()
    {
        if (panelRect != null)
        {
            panelRect.DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack)
                .OnComplete(() => rootPanel?.SetActive(false));
        }
        else
        {
            rootPanel?.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Vote Icon Update (called externally per vote received)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Update vote icon for a panel index (0-3).
    /// Called by GameplayNetworkManager as votes come in.
    /// </summary>
    public void SetVoteIcon(int panelIndex, bool voted, bool yes)
    {
        if (panelIndex < 0 || panelIndex >= voteStatusIcons.Length) return;
        Image icon = voteStatusIcons[panelIndex];
        if (icon == null) return;

        if (!voted)
            icon.sprite = votePendingSprite;
        else
            icon.sprite = yes ? voteYesSprite : voteNoSprite;
    }

    // ─────────────────────────────────────────────────────────────
    //  Button Callbacks
    // ─────────────────────────────────────────────────────────────

    private void OnYesClicked()
    {
        if (_voted) return;
        _voted = true;

        SetButtonsInteractable(false);
        waitingText.text = "Voted YES — waiting for others...";

        SetVoteIcon(0, voted: true, yes: true);
        _onYes?.Invoke();
    }

    private void OnNoClicked()
    {
        if (_voted) return;
        _voted = true;

        SetButtonsInteractable(false);
        waitingText.text = "Voted NO — waiting for others...";

        SetVoteIcon(0, voted: true, yes: false);
        _onNo?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private void SetButtonsInteractable(bool interactable)
    {
        if (yesButton != null) yesButton.interactable = interactable;
        if (noButton  != null) noButton.interactable  = interactable;
    }

    private void ResetVoteIcons()
    {
        foreach (Image icon in voteStatusIcons)
            if (icon != null) icon.sprite = votePendingSprite;
    }
}
