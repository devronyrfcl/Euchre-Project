using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Main Menu — "Tournament Panel".
/// Handles Join / Play buttons, entry fee + prize pool display, 48h countdown,
/// and the local player's own avatar/name/rank reference.
/// </summary>
public class TournamentPanelUI : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private PlayFabManager        playFabManager;
    [SerializeField] private TournamentMatchmaking matchmaking;

    [Header("=== BUTTONS ===")]
    [SerializeField] private Button joinButton;
    [SerializeField] private Button playButton;

    [Header("=== INFO TEXTS ===")]
    [SerializeField] private TMP_Text entryFeeText;
    [SerializeField] private TMP_Text prizePoolText;
    [Tooltip("48 hour countdown remaining after joining")]
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
        if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
        if (playButton != null) playButton.onClick.AddListener(OnPlayClicked);

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
    }

    private void OnDisable()
    {
        if (joinButton != null) joinButton.onClick.RemoveListener(OnJoinClicked);
        if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);

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

    private void OnJoinClicked()
    {
        if (TournamentManager.Instance == null) return;

        if (joinButton != null) joinButton.interactable = false;

        TournamentManager.Instance.JoinTournament((success, error) =>
        {
            if (joinButton != null) joinButton.interactable = true;

            if (success)
            {
                playFabManager?.RefreshCurrencies();
                UpdateUI();
            }
            else
            {
                NotificationManager.Instance?.AddNotification(
                    "Couldn't Join Tournament", error ?? "Please try again.");
            }
        });
    }

    private void OnPlayClicked()
    {
        if (matchmaking == null)
        {
            Debug.LogWarning("[TournamentPanelUI] TournamentMatchmaking reference missing.");
            return;
        }

        matchmaking.Play();
    }

    // ─────────────────────────────────────────────────────────────
    //  UI refresh
    // ─────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        TournamentManager tm = TournamentManager.Instance;
        bool joined = tm != null && tm.IsJoined;

        if (joinButton != null) joinButton.gameObject.SetActive(!joined);
        if (playButton != null)
        {
            playButton.gameObject.SetActive(joined);
            playButton.interactable = joined && tm.CanPlay;
        }

        if (prizePoolText != null)
            prizePoolText.text = $"Prize Pool: {(joined && tm.Board != null ? tm.Board.prizePool : 0):N0} PC";

        if (myRankText != null)
            myRankText.text = joined ? $"#{ComputeMyRank(tm)}" : "#0";

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

        var sorted = new System.Collections.Generic.List<TournamentPlayerEntry>(tm.Board.players);
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
