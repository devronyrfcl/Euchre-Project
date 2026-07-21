using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Main Menu — "Tournament Panel".
///
/// Two top-level states:
///  - Not joined  → show the Tournament List (browse/join/create rooms)
///  - Joined      → show the dashboard (Left Side Panels, Total Coin, Scroll View,
///                   Title Text) with Play/Leave, entry fee, prize pool, countdown,
///                   and the local player's own avatar/name/rank.
///
/// Joining a room is free (handled by TournamentListUI / room cards). The entry
/// fee is charged the first time Play is pressed. Leave is only available before
/// paying.
/// </summary>
public class TournamentPanelUI : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private PlayFabManager        playFabManager;
    [SerializeField] private TournamentMatchmaking matchmaking;

    [Header("=== TOP-LEVEL STATE ===")]
    [Tooltip("Shown when the player hasn't joined a tournament room yet")]
    [SerializeField] private GameObject tournamentListPanel;
    [Tooltip("These 4 are the joined-room dashboard — hidden while browsing the Tournament List")]
    [SerializeField] private GameObject leftSidePanels;
    [SerializeField] private GameObject totalCoinObj;
    [SerializeField] private GameObject scrollViewObj;
    [SerializeField] private GameObject titleTextObj;

    [Header("=== BUTTONS ===")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button leaveButton;

    [Header("=== INFO TEXTS ===")]
    [SerializeField] private TMP_Text entryFeeText;
    [SerializeField] private TMP_Text prizePoolText;
    [Tooltip("48 hour countdown — starts when the first player joins the room")]
    [SerializeField] private TMP_Text countdownText;

    [Header("=== MY STATUS ===")]
    [SerializeField] private Image    myAvatarImage;
    [SerializeField] private Sprite   defaultAvatar;
    [SerializeField] private TMP_Text myNameText;
    [Tooltip("Shows #0 if not joined, otherwise #<rank>")]
    [SerializeField] private TMP_Text myRankText;

    private float _ticker;

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (playButton  != null) playButton.onClick.AddListener(OnPlayClicked);
        if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveClicked);

        if (TournamentManager.Instance != null)
            TournamentManager.Instance.OnStatusUpdated += UpdateUI;

        // My name / avatar don't depend on tournament status
        if (playFabManager != null)
        {
            if (myNameText != null) myNameText.text = playFabManager.CachedDisplayName;
            StartCoroutine(FetchAvatar(playFabManager.CachedAvatarUrl));
        }

        if (entryFeeText != null)
            entryFeeText.text = $"Entry Fee: {TournamentManager.ENTRY_FEE:N0} PC";

        TournamentManager.Instance?.RefreshMyStatus(_ => UpdateUI());
        UpdateUI();

        // Did a room I was in expire and auto-pay-out while I was away?
        TournamentManager.Instance?.CheckPendingReward((hasReward, reward) =>
        {
            if (!hasReward || reward == null) return;

            NotificationManager.Instance?.AddNotification(
                "Tournament Ended!",
                $"\"{reward.roomName}\" finished — you placed #{reward.rank} and earned {reward.reward:N0} PC!");

            playFabManager?.RefreshCurrencies();
            TournamentManager.Instance?.ClearPendingReward();
        });
    }

    private void OnDisable()
    {
        if (playButton  != null) playButton.onClick.RemoveListener(OnPlayClicked);
        if (leaveButton != null) leaveButton.onClick.RemoveListener(OnLeaveClicked);

        if (TournamentManager.Instance != null)
            TournamentManager.Instance.OnStatusUpdated -= UpdateUI;
    }

    private void Update()
    {
        _ticker += Time.deltaTime;
        if (_ticker < 1f) return;
        _ticker = 0f;

        UpdateCountdownText();
    }

    // ─────────────────────────────────────────────────────────────
    //  Button handlers
    // ─────────────────────────────────────────────────────────────

    private void OnPlayClicked()
    {
        if (matchmaking == null)
        {
            Debug.LogWarning("[TournamentPanelUI] TournamentMatchmaking reference missing.");
            return;
        }

        TournamentManager tm = TournamentManager.Instance;
        if (tm == null) return;

        if (tm.HasPaid)
        {
            matchmaking.Play();
            return;
        }

        // First Play press — charge the entry fee before matchmaking starts.
        if (playButton != null) playButton.interactable = false;

        tm.PayEntryFee((success, error) =>
        {
            if (playButton != null) playButton.interactable = true;

            if (!success)
            {
                NotificationManager.Instance?.AddNotification("Payment Failed", error ?? "Please try again.");
                return;
            }

            playFabManager?.RefreshCurrencies();
            UpdateUI(); // Leave button hides now that the entry fee is paid
            matchmaking.Play();
        });
    }

    private void OnLeaveClicked()
    {
        if (TournamentManager.Instance == null) return;

        if (leaveButton != null) leaveButton.interactable = false;

        TournamentManager.Instance.LeaveRoom((success, error) =>
        {
            if (leaveButton != null) leaveButton.interactable = true;

            if (!success)
            {
                NotificationManager.Instance?.AddNotification("Couldn't Leave", error ?? "Please try again.");
                return;
            }

            UpdateUI(); // switches back to the Tournament List
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  UI refresh
    // ─────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        TournamentManager tm = TournamentManager.Instance;
        bool joined = tm != null && tm.IsJoined;

        // Top-level state switch
        tournamentListPanel?.SetActive(!joined);
        leftSidePanels?.SetActive(joined);
        totalCoinObj?.SetActive(joined);
        scrollViewObj?.SetActive(joined);
        titleTextObj?.SetActive(joined);

        if (!joined)
        {
            UpdateCountdownText();
            if (myRankText != null) myRankText.text = "#0";
            return;
        }

        if (playButton != null)
        {
            playButton.gameObject.SetActive(true);
            playButton.interactable = tm.CanPlay;
        }

        if (leaveButton != null)
            leaveButton.gameObject.SetActive(!tm.HasPaid);

        if (prizePoolText != null)
            prizePoolText.text = $"Prize Pool: {(tm.Board != null ? tm.Board.prizePool : 0):N0} PC";

        if (myRankText != null)
            myRankText.text = $"#{ComputeMyRank(tm)}";

        UpdateCountdownText();
    }

    private void UpdateCountdownText()
    {
        if (countdownText == null) return;

        TournamentManager tm = TournamentManager.Instance;
        if (tm == null || !tm.IsJoined)
        {
            countdownText.text = "Not Joined";
            return;
        }

        if (tm.IsExpired)
        {
            countdownText.text = "Time Expired";
            return;
        }

        System.TimeSpan remaining = tm.TimeRemaining;
        countdownText.text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private int ComputeMyRank(TournamentManager tm)
    {
        if (tm.Board?.players == null || tm.MyEntry == null) return 0;

        var sorted = new List<TournamentPlayerEntry>(tm.Board.players);
        sorted.Sort((a, b) => b.score.CompareTo(a.score));

        for (int i = 0; i < sorted.Count; i++)
            if (sorted[i].playFabId == tm.MyEntry.playFabId)
                return i + 1;

        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  Avatar loading
    // ─────────────────────────────────────────────────────────────

    private IEnumerator FetchAvatar(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            ApplyDefaultAvatar();
            yield break;
        }

        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Texture2D tex = DownloadHandlerTexture.GetContent(www);
            Sprite sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));

            if (myAvatarImage != null)
                myAvatarImage.sprite = sprite;
        }
        else
        {
            ApplyDefaultAvatar();
        }
    }

    private void ApplyDefaultAvatar()
    {
        if (myAvatarImage != null && defaultAvatar != null)
            myAvatarImage.sprite = defaultAvatar;
    }
}
