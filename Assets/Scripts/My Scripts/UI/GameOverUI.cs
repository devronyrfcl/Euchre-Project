using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Game over panel — shows full match stats, score breakdown,
/// trophy change, and winner announcement.
/// Shown to all players at match end.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== PANEL ===")]
    [SerializeField] private GameObject    rootPanel;
    [SerializeField] private RectTransform panelRect;

    [Header("=== WINNER ANNOUNCEMENT ===")]
    [SerializeField] private TMP_Text winnerTitleText;
        // e.g. "🏆 You Win!"  or  "Team 2 Wins!"
    [SerializeField] private TMP_Text winnerSubtitleText;
        // e.g. "Congratulations! Your team dominated!"

    [Header("=== SCORE BREAKDOWN ===")]
    [SerializeField] private TMP_Text team1ScoreText;
        // e.g. "Team 1:  10 pts"
    [SerializeField] private TMP_Text team2ScoreText;
        // e.g. "Team 2:  7 pts"
    [SerializeField] private TMP_Text tricksText;
        // e.g. "Last Hand Tricks — T1: 3  |  T2: 2"
    [SerializeField] private TMP_Text lastHandSummaryText;
        // e.g. "Trump caller: Team 1 | Mode: Team Play\nResult: Made it! +1 pt"

    [Header("=== ECONOMY ===")]
    [SerializeField] private TMP_Text winningsText;
        // e.g. "+400 PC added!"  or  "Better luck next time."
    [SerializeField] private TMP_Text rematchCostText;
        // e.g. "Rematch costs 100 PC"

    [Header("=== TROPHY ===")]
    [SerializeField] private TMP_Text trophyChangeText;
        // e.g. "+30 🏆"  or  "-15 🏆"
    [SerializeField] private TMP_Text finalScoreText;
        // shows the numeric final trophy value (for future use)

    [Header("=== BUTTONS ===")]
    [SerializeField] private Button rematchButton;
    [SerializeField] private Button mainMenuButton;

    [Header("=== FX ===")]
    [SerializeField] private GameObject winFX;
    [SerializeField] private GameObject loseFX;

    // ─────────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    private Action _onRematch;
    private Action _onMainMenu;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        rematchButton?.onClick.AddListener(OnRematchClicked);
        mainMenuButton?.onClick.AddListener(OnMainMenuClicked);
        rootPanel?.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Show
    // ─────────────────────────────────────────────────────────────

    public void Show(MatchEndData data, Action onRematch, Action onMainMenu)
    {
        _onRematch  = onRematch;
        _onMainMenu = onMainMenu;

        // ── Winner title ───────────────────────────────────────
        if (winnerTitleText != null)
        {
            winnerTitleText.text = data.localWon
                ? "🏆 You Win!"
                : $"Team {data.winnerTeam} Wins!";
        }

        // ── Subtitle / congratulation ──────────────────────────
        if (winnerSubtitleText != null)
        {
            winnerSubtitleText.text = data.localWon
                ? GetWinMessage(data)
                : GetLoseMessage(data);
        }

        // ── Score breakdown ────────────────────────────────────
        if (team1ScoreText != null)
            team1ScoreText.text = $"Team 1 — {data.team1Score} pts";

        if (team2ScoreText != null)
            team2ScoreText.text = $"Team 2 — {data.team2Score} pts";

        if (tricksText != null)
            tricksText.text =
                $"Last Hand Tricks\n" +
                $"Team 1: {data.team1Tricks}  |  Team 2: {data.team2Tricks}";

        if (lastHandSummaryText != null)
            lastHandSummaryText.text = data.lastHandSummary;

        // ── Economy ───────────────────────────────────────────
        if (winningsText != null)
        {
            winningsText.text = data.localWon
                ? $"+{data.pot} PC added to your balance!"
                : "Better luck next time.";
        }

        if (rematchCostText != null)
            rematchCostText.text = $"Rematch costs {data.betAmount} PC";

        // ── Trophy ────────────────────────────────────────────
        int trophy = data.trophyChange;

        if (trophyChangeText != null)
        {
            trophyChangeText.text  = trophy > 0
                ? $"+{trophy} 🏆"
                : $"{trophy} 🏆";
            trophyChangeText.color = trophy > 0
                ? new Color(1f, 0.84f, 0f)   // gold
                : new Color(1f, 0.35f, 0.35f); // red
        }

        // Final score text — show and log
        // This value will be used for leaderboard trophy later
        int finalTrophyValue = trophy; // placeholder — will add to existing trophy later
        string finalScoreLog = $"[GameOverUI] Final Trophy Change: " +
                               $"{(trophy > 0 ? "+" : "")}{trophy} | " +
                               $"Match Result: {(data.localWon ? "WIN" : "LOSS")} | " +
                               $"Score T1:{data.team1Score} T2:{data.team2Score}";

        Debug.Log(finalScoreLog);

        if (finalScoreText != null)
            finalScoreText.text = $"Trophy: {(trophy > 0 ? "+" : "")}{trophy}";

        // ── FX ────────────────────────────────────────────────
        if (winFX  != null) winFX.SetActive(data.localWon);
        if (loseFX != null) loseFX.SetActive(!data.localWon);

        // ── Show panel ────────────────────────────────────────
        rootPanel?.SetActive(true);

        if (panelRect != null)
        {
            panelRect.localScale = Vector3.zero;
            panelRect.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Hide
    // ─────────────────────────────────────────────────────────────

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
    //  Button Callbacks
    // ─────────────────────────────────────────────────────────────

    private void OnRematchClicked()
    {
        Hide();
        _onRematch?.Invoke();
    }

    private void OnMainMenuClicked()
    {
        Hide();
        _onMainMenu?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    //  Message Helpers
    // ─────────────────────────────────────────────────────────────

    private string GetWinMessage(MatchEndData data)
    {
        if (!data.isTeamMode)
            return "Outstanding solo performance! You crushed it!";

        if (data.team1Tricks == 5 || data.team2Tricks == 5)
            return "Incredible! Your team swept all 5 tricks!";

        return "Well played! Your team called it and delivered!";
    }

    private string GetLoseMessage(MatchEndData data)
    {
        bool wasEuchred = (data.localTeam == 1 && data.team1Score < data.team2Score)
                       || (data.localTeam == 2 && data.team2Score < data.team1Score);

        if (wasEuchred)
            return "You got euchred! Better luck next hand.";

        return $"Team {data.winnerTeam} reached {(data.winnerTeam == 1 ? data.team1Score : data.team2Score)} points first. Good game!";
    }
}