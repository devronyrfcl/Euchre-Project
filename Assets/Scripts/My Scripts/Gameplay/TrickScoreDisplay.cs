using TMPro;
using UnityEngine;

/// <summary>
/// Shows live trick counts for both teams / all players during a hand.
/// GameplayNetworkManager calls UpdateTrickScore() after each trick.
/// </summary>
public class TrickScoreDisplay : MonoBehaviour
{
    public static TrickScoreDisplay Instance { get; private set; }

    [Header("=== TRICK COUNT TEXTS ===")]
    [Tooltip("Shows 'Team 1 Tricks: X' or 'Your Tricks: X' in team/freeforall mode")]
    [SerializeField] private TMP_Text team1TrickText;
    [SerializeField] private TMP_Text team2TrickText;

    [Header("=== HAND SCORE TEXTS (cumulative match points) ===")]
    [SerializeField] private TMP_Text team1ScoreText;
    [SerializeField] private TMP_Text team2ScoreText;

    [Header("=== LABELS (optional — to rename based on team mode) ===")]
    [SerializeField] private TMP_Text team1Label;
    [SerializeField] private TMP_Text team2Label;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        ResetDisplay();
    }

    // ─── Called by GameplayNetworkManager ────────────────────────

    /// <summary>
    /// Call after every trick win.
    /// isTeamMode: true = team labels, false = individual labels.
    /// </summary>
    public void UpdateTrickScore(int team1Tricks, int team2Tricks,
                                  int team1Score,  int team2Score,
                                  bool isTeamMode)
    {
        // Trick counts (resets each hand)
        if (team1TrickText != null)
            team1TrickText.text = team1Tricks.ToString();

        if (team2TrickText != null)
            team2TrickText.text = team2Tricks.ToString();

        // Cumulative match score
        if (team1ScoreText != null)
            team1ScoreText.text = team1Score.ToString();

        if (team2ScoreText != null)
            team2ScoreText.text = team2Score.ToString();

        // Labels
        if (team1Label != null)
            team1Label.text = isTeamMode ? "Team 1" : "You & Partner";

        if (team2Label != null)
            team2Label.text = isTeamMode ? "Team 2" : "Opponents";
    }

    /// <summary>Reset trick counts at start of each hand. Keep match score.</summary>
    public void ResetTricks(int team1Score, int team2Score)
    {
        if (team1TrickText != null) team1TrickText.text = "0";
        if (team2TrickText != null) team2TrickText.text = "0";

        if (team1ScoreText != null) team1ScoreText.text = team1Score.ToString();
        if (team2ScoreText != null) team2ScoreText.text = team2Score.ToString();
    }

    /// <summary>Full reset on new match.</summary>
    public void ResetDisplay()
    {
        if (team1TrickText != null) team1TrickText.text = "0";
        if (team2TrickText != null) team2TrickText.text = "0";
        if (team1ScoreText != null) team1ScoreText.text = "0";
        if (team2ScoreText != null) team2ScoreText.text = "0";
    }
}