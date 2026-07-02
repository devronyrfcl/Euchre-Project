using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Counts down from 30 each turn.
/// If timer hits 0 on local player's turn → auto plays a card.
/// GameplayNetworkManager calls StartTimer() / StopTimer() each turn.
/// </summary>
public class TurnTimerController : MonoBehaviour
{
    public static TurnTimerController Instance { get; private set; }

    [Header("=== TIMER TEXT ===")]
    [SerializeField] private TMP_Text timerText;

    [Header("=== SETTINGS ===")]
    [SerializeField] private int turnDuration = 30;

    [Header("=== COLORS ===")]
    [SerializeField] private Color normalColor  = Color.white;
    [SerializeField] private Color warningColor = Color.yellow; // <= 10s
    [SerializeField] private Color urgentColor  = Color.red;    // <= 5s

    private Coroutine _timerRoutine;
    private bool      _isLocalTurn = false;

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

    // ─── Called by GameplayNetworkManager ────────────────────────

    /// <summary>
    /// Call at the start of every turn (including bots and opponents).
    /// isLocalTurn: true only when it's the local human player's turn.
    /// </summary>
    public void StartTimer(bool isLocalTurn)
    {
        _isLocalTurn = isLocalTurn;

        if (_timerRoutine != null) StopCoroutine(_timerRoutine);
        _timerRoutine = StartCoroutine(TimerRoutine());
    }

    /// <summary>Call when turn ends (card played, trump selected, etc.).</summary>
    public void StopTimer()
    {
        if (_timerRoutine != null)
        {
            StopCoroutine(_timerRoutine);
            _timerRoutine = null;
        }

        if (timerText != null)
            timerText.gameObject.SetActive(false);
    }

    // ─── Internal ────────────────────────────────────────────────

    private IEnumerator TimerRoutine()
    {
        if (timerText != null)
            timerText.gameObject.SetActive(true);

        int remaining = turnDuration;

        while (remaining > 0)
        {
            UpdateTimerText(remaining);
            yield return new WaitForSeconds(1f);
            remaining--;
        }

        // Time is up
        UpdateTimerText(0);
        OnTimerExpired();
    }

    private void UpdateTimerText(int seconds)
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

    private void OnTimerExpired()
    {
        StopTimer();

        if (!_isLocalTurn) return;

        // Auto play for local player
        EuchreCardManager cardManager = EuchreCardManager.Instance;
        if (cardManager == null) return;

        // Try to auto-select and play first valid card
        cardManager.AutoPlayFirstValidCard();
    }
}