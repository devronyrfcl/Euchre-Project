using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Counts down from turnDuration each turn.
/// Drives both the TMP_Text display and the
/// GameplayPlayerData.UpdateTimerVisual() on the active player panel.
/// On expiry during local turn → calls EuchreCardManager.AutoPlayFirstValidCard().
/// </summary>
public class TurnTimerController : MonoBehaviour
{
    public static TurnTimerController Instance { get; private set; }

    [Header("=== TIMER TEXT ===")]
    [SerializeField] private TMP_Text timerText;

    [Header("=== SETTINGS ===")]
    [SerializeField] private int turnDuration = 30;

    [Header("=== TEXT COLORS ===")]
    [SerializeField] private Color normalColor  = Color.white;
    [SerializeField] private Color warningColor = Color.yellow;
    [SerializeField] private Color urgentColor  = Color.red;

    private Coroutine          _timerRoutine;
    private bool               _isLocalTurn      = false;
    private int                _activePanelIndex = -1;
    private GameplayPlayerData _activePanel      = null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (timerText != null)
            timerText.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Start countdown for a new turn.
    /// isLocalTurn: true = auto-play on expiry.
    /// panelIndex: 0-3, used to find GameplayPlayerData for visual.
    /// </summary>
    public void StartTimer(bool isLocalTurn, int panelIndex)
    {
        _isLocalTurn      = isLocalTurn;
        _activePanelIndex = panelIndex;

        // Get the panel's GameplayPlayerData
        _activePanel = GameplayPlayFabManager.Instance != null
            ? GameplayPlayFabManager.Instance.GetPlayerPanel(panelIndex)
            : null;

        // Reset all panels' visuals first
        ResetAllPanelVisuals();

        if (_timerRoutine != null) StopCoroutine(_timerRoutine);
        _timerRoutine = StartCoroutine(TimerRoutine());
    }

    /// <summary>Stop and hide timer. Call when any action is taken.</summary>
    public void StopTimer()
    {
        if (_timerRoutine != null)
        {
            StopCoroutine(_timerRoutine);
            _timerRoutine = null;
        }

        if (timerText != null)
            timerText.gameObject.SetActive(false);

        // Reset active panel visual
        _activePanel?.ResetTimerVisual();
        _activePanel      = null;
        _activePanelIndex = -1;
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal
    // ─────────────────────────────────────────────────────────────

    private IEnumerator TimerRoutine()
    {
        if (timerText != null)
            timerText.gameObject.SetActive(true);

        int remaining = turnDuration;

        while (remaining > 0)
        {
            UpdateTextDisplay(remaining);
            _activePanel?.UpdateTimerVisual(remaining, turnDuration);

            yield return new WaitForSeconds(1f);
            remaining--;
        }

        // Final tick at 0
        UpdateTextDisplay(0);
        _activePanel?.UpdateTimerVisual(0, turnDuration);

        yield return new WaitForSeconds(0.2f);

        OnExpired();
    }

    private void UpdateTextDisplay(int seconds)
    {
        if (timerText == null) return;

        timerText.text = seconds.ToString();

        if (seconds <= 5)
            timerText.color = urgentColor;
        else if (seconds <= 10)
            timerText.color = warningColor;
        else
            timerText.color = normalColor;
    }

    private void ResetAllPanelVisuals()
    {
        if (GameplayPlayFabManager.Instance == null) return;

        for (int i = 0; i < 4; i++)
        {
            GameplayPlayerData panel = GameplayPlayFabManager.Instance.GetPlayerPanel(i);
            panel?.ResetTimerVisual();
        }
    }

    private void OnExpired()
    {
        StopTimer();

        if (!_isLocalTurn) return;

        EuchreCardManager cardManager = EuchreCardManager.Instance;
        if (cardManager != null)
            cardManager.AutoPlayFirstValidCard();
        else
            Debug.LogWarning("[TurnTimer] EuchreCardManager.Instance is null on expiry.");
    }
}