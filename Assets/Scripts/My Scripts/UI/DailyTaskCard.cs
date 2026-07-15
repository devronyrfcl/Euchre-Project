using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sits on the Daily Task card prefab.
/// Call Setup() once after spawning, then Refresh() when progress changes.
/// </summary>
public class DailyTaskCard : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== TASK INFO ===")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text rewardGPText;
    [SerializeField] private TMP_Text rewardPCText;
    [SerializeField] private TMP_Text statusCountText;      // "1 / 3"
    [SerializeField] private Image    progressFillImage;    // Image type: Filled

    [Header("=== CLAIM ===")]
    [SerializeField] private Button   claimButton;
    [SerializeField] private TMP_Text claimedLabel;         // "Claimed" — shown after claiming

    // ─────────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    private string        _taskId;
    private Action<string> _onClaim;

    // ─────────────────────────────────────────────────────────────
    //  Init
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        claimButton?.onClick.AddListener(OnClaimClicked);
    }

    public void Setup(DailyTaskState task, Action<string> onClaim)
    {
        _taskId  = task.id;
        _onClaim = onClaim;

        if (titleText    != null) titleText.text    = task.title;
        if (rewardGPText != null) rewardGPText.text = $"+{task.rewardGP} GP";
        if (rewardPCText != null) rewardPCText.text = $"+{task.rewardPC} PC";

        // Set fill immediately on spawn (no tween on first show)
        if (progressFillImage != null)
        {
            float fill = task.targetCount > 0
                ? (float)task.currentCount / task.targetCount
                : 0f;
            progressFillImage.fillAmount = fill;
        }

        Refresh(task);
    }

    // ─────────────────────────────────────────────────────────────
    //  Refresh (called when progress updates)
    // ─────────────────────────────────────────────────────────────

    public void Refresh(DailyTaskState task)
    {
        int  current = task.currentCount;
        int  target  = task.targetCount;
        bool done    = task.IsComplete;
        bool claimed = task.isClaimed;

        if (statusCountText != null)
            statusCountText.text = $"{current} / {target}";

        if (progressFillImage != null)
        {
            float fill = target > 0 ? (float)current / target : 0f;
            progressFillImage.DOFillAmount(fill, 0.35f).SetEase(Ease.OutCubic);
        }

        if (claimButton != null)
            claimButton.interactable = done && !claimed;

        if (claimedLabel != null)
            claimedLabel.text = claimed ? "Claimed" : "Claim";
    }

    // ─────────────────────────────────────────────────────────────
    //  Claim
    // ─────────────────────────────────────────────────────────────

    private void OnClaimClicked()
    {
        if (claimButton  != null) claimButton.interactable = false;
        if (claimedLabel != null) claimedLabel.text = "Claimed";
        _onClaim?.Invoke(_taskId);
    }
}
