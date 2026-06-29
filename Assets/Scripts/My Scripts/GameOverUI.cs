using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shown at the end of a full match (first team to WIN_SCORE points).
/// Displays winner, final scores, winnings info.
/// Offers rematch (deducts another bet) or return to main menu.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== PANEL ===")]
    [SerializeField] private GameObject    rootPanel;
    [SerializeField] private RectTransform panelRect;

    [Header("=== TEXT ===")]
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text winningsText;
    [SerializeField] private TMP_Text rematchCostText;

    [Header("=== BUTTONS ===")]
    [SerializeField] private Button rematchButton;
    [SerializeField] private Button mainMenuButton;

    [Header("=== CONFETTI / FX ===")]
    [SerializeField] private GameObject winFX;   // optional particle system
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

    /// <summary>
    /// winnerTeam: 1 or 2.
    /// localPlayerWon: controls FX and winnings display.
    /// pot: total PC distributed to winner.
    /// </summary>
    public void Show(int winnerTeam, int team1Score, int team2Score,
                     Action onRematch, Action onMainMenu,
                     bool localPlayerWon = false, int pot = 400, int betAmount = 100)
    {
        _onRematch  = onRematch;
        _onMainMenu = onMainMenu;

        // Winner text
        winnerText.text = localPlayerWon
            ? "🏆 You Win!"
            : $"Team {winnerTeam} Wins!";

        // Score
        scoreText.text =
            $"Final Score\n" +
            $"Team 1 — {team1Score} pts\n" +
            $"Team 2 — {team2Score} pts";

        // Winnings
        winningsText.text = localPlayerWon
            ? $"+{pot} PC added to your balance!"
            : "Better luck next time.";

        // Rematch cost
        if (rematchCostText != null)
            rematchCostText.text = $"Rematch costs {betAmount} PC";

        // FX
        if (winFX  != null) winFX.SetActive(localPlayerWon);
        if (loseFX != null) loseFX.SetActive(!localPlayerWon);

        // Show panel
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
}
