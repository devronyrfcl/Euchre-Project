using System.Collections;
using TMPro;
using UnityEngine;

public class TurnTimerController : MonoBehaviour
{
    public static TurnTimerController Instance { get; private set; }

    [Header("=== TIMER TEXT ===")]
    [SerializeField] private TMP_Text timerText;

    [Header("=== SETTINGS ===")]
    [SerializeField] private int turnDuration = 30;

    [Header("=== COLORS ===")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color warningColor = Color.yellow;
    [SerializeField] private Color urgentColor = Color.red;

    private Coroutine _timerRoutine;
    private bool _isLocalTurn = false;
    private bool _isBotTurn = false;

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

    // ── isBot: true when the active seat is a bot ─────────────────
    public void StartTimer(bool isLocalTurn, bool isBot = false)
    {
        _isLocalTurn = isLocalTurn;
        _isBotTurn = isBot;

        if (_timerRoutine != null) StopCoroutine(_timerRoutine);
        _timerRoutine = StartCoroutine(TimerRoutine());
    }

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

    private IEnumerator TimerRoutine()
    {
        if (timerText != null)
            timerText.gameObject.SetActive(true);

        int remaining = turnDuration;

        while (remaining > 0)
        {
            UpdateDisplay(remaining);
            yield return new WaitForSeconds(1f);
            remaining--;
        }

        UpdateDisplay(0);
        OnExpired();
    }

    private void UpdateDisplay(int seconds)
    {
        if (timerText == null) return;
        timerText.text = seconds.ToString();

        if (seconds <= 5) timerText.color = urgentColor;
        else if (seconds <= 10) timerText.color = warningColor;
        else timerText.color = normalColor;
    }

    private void OnExpired()
    {
        StopTimer();

        // ── Bot turn expired — master client forces a play ─────────
        if (_isBotTurn)
        {
            if (Photon.Pun.PhotonNetwork.IsMasterClient)
            {
                GameplayNetworkManager.Instance?.ForceBotPlay(
                    GameplayNetworkManager.Instance.TurnSeat);
            }
            return;
        }

        // ── Local human turn expired — auto play ──────────────────
        if (_isLocalTurn)
        {
            EuchreCardManager.Instance?.AutoPlayFirstValidCard();
        }
    }
}