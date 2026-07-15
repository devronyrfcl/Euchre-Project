using System;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEngine;

/// <summary>
/// Daily reward system — 24-hour cooldown, grants PC via PlayFab.
/// Place on any persistent GameObject in the Main Menu scene.
///
/// Wire the Claim button's onClick → DailyRewardManager.ClaimReward()
/// </summary>
public class DailyRewardManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────────

    [Header("=== REWARD ===")]
    [SerializeField] private int rewardPC = 500;

    [Header("=== UI ===")]
    [Tooltip("Shows reward amount when available (e.g. '+500 PC'), or a 'come back' message when not")]
    [SerializeField] private TMP_Text  rewardText;

    [Tooltip("Countdown until the next reward becomes available")]
    [SerializeField] private TMP_Text  timerText;

    [Tooltip("Claim button / panel — enabled only when reward is available")]
    [SerializeField] private GameObject claimObject;

    // ─────────────────────────────────────────────────────────────
    //  Private
    // ─────────────────────────────────────────────────────────────

    private const string SAVE_KEY        = "DailyRewardLastClaim";
    private const double COOLDOWN_HOURS  = 24.0;

    private DateTime _lastClaimUtc = DateTime.MinValue;
    private bool     _available;
    private float    _ticker;

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        Load();
        Refresh();
    }

    private void Update()
    {
        // Refresh every second (for the countdown)
        _ticker += Time.deltaTime;
        if (_ticker < 1f) return;
        _ticker = 0f;
        Refresh();
    }

    // ─────────────────────────────────────────────────────────────
    //  Core logic
    // ─────────────────────────────────────────────────────────────

    private void Refresh()
    {
        TimeSpan elapsed = DateTime.UtcNow - _lastClaimUtc;
        _available = elapsed.TotalHours >= COOLDOWN_HOURS;

        // Enable / disable claim button area
        if (claimObject != null)
            claimObject.SetActive(_available);

        if (_available)
        {
            if (rewardText != null)
                rewardText.text = $"+{rewardPC:N0} PC";

            if (timerText != null)
                timerText.text = "Ready to claim!";
        }
        else
        {
            if (rewardText != null)
                rewardText.text = "Daily reward not available";

            TimeSpan remaining = TimeSpan.FromHours(COOLDOWN_HOURS) - elapsed;
            if (timerText != null)
                timerText.text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Claim — wire to claim button onClick
    // ─────────────────────────────────────────────────────────────

    public void ClaimReward()
    {
        if (!_available) return;

        // Save claim time immediately to prevent double-claim
        _lastClaimUtc = DateTime.UtcNow;
        Save();
        _available = false;
        Refresh();

        // Grant PC via PlayFab
        PlayFabClientAPI.AddUserVirtualCurrency(
            new AddUserVirtualCurrencyRequest
            {
                VirtualCurrency = "PC",
                Amount          = rewardPC
            },
            result =>
            {
                Debug.Log($"[DailyReward] Claimed {rewardPC} PC. New balance: {result.Balance}");
            },
            error =>
            {
                Debug.LogWarning($"[DailyReward] Claim failed: {error.GenerateErrorReport()}");

                // Roll back the save so the player can retry
                _lastClaimUtc = DateTime.MinValue;
                Save();
                _available = true;
                Refresh();
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Save / Load
    // ─────────────────────────────────────────────────────────────

    private void Save()
    {
        PlayerPrefs.SetString(SAVE_KEY, _lastClaimUtc.Ticks.ToString());
        PlayerPrefs.Save();
    }

    private void Load()
    {
        string saved = PlayerPrefs.GetString(SAVE_KEY, "");
        if (!string.IsNullOrEmpty(saved) && long.TryParse(saved, out long ticks))
            _lastClaimUtc = new DateTime(ticks, DateTimeKind.Utc);
        else
            _lastClaimUtc = DateTime.MinValue;
    }
}
