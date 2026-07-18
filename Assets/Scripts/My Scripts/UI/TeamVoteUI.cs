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
    [SerializeField] private float   pendingSpinDuration = 1f;

    // ─────────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    private Action _onYes;
    private Action _onNo;
    private bool   _voted = false;
    private Tween[] _iconSpinTweens;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        yesButton?.onClick.AddListener(OnYesClicked);
        noButton?.onClick.AddListener(OnNoClicked);
        rootPanel?.SetActive(false);

        _iconSpinTweens = new Tween[voteStatusIcons.Length];
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
        for (int i = 0; i < voteStatusIcons.Length; i++)
            StopIconSpin(i);

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

    private void OnDestroy()
    {
        if (_iconSpinTweens == null) return;
        foreach (Tween tween in _iconSpinTweens)
            tween?.Kill();
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
        {
            icon.sprite = votePendingSprite;
            StartIconSpin(panelIndex);
        }
        else
        {
            icon.sprite = yes ? voteYesSprite : voteNoSprite;
            StopIconSpin(panelIndex);
        }
    }

    private void StartIconSpin(int panelIndex)
    {
        Image icon = voteStatusIcons[panelIndex];
        if (icon == null) return;

        if (_iconSpinTweens[panelIndex] != null && _iconSpinTweens[panelIndex].IsActive()) return;

        icon.rectTransform.localRotation = Quaternion.identity;
        _iconSpinTweens[panelIndex] = icon.rectTransform
            .DORotate(new Vector3(0f, 0f, -360f), pendingSpinDuration, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Restart);
    }

    private void StopIconSpin(int panelIndex)
    {
        Image icon = voteStatusIcons[panelIndex];

        _iconSpinTweens[panelIndex]?.Kill();
        _iconSpinTweens[panelIndex] = null;

        if (icon != null)
            icon.rectTransform.localRotation = Quaternion.identity;
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
        for (int i = 0; i < voteStatusIcons.Length; i++)
        {
            if (voteStatusIcons[i] == null) continue;
            voteStatusIcons[i].sprite = votePendingSprite;
            StartIconSpin(i);
        }
    }
}
