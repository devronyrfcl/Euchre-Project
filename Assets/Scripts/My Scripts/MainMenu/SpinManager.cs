using System;
using System.Collections;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEngine;

[Serializable]
public struct WheelSegment
{
    public string label;
    public int    pcReward;   // 0 = LOSE
}

public class SpinManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform  wheel;
    [SerializeField] private GameObject spinButton;

    [Header("Spin Settings")]
    [SerializeField] private float minSpinDuration   = 4f;
    [SerializeField] private float maxSpinDuration   = 6f;
    [SerializeField] private float minExtraRotations = 5f;
    [SerializeField] private float maxExtraRotations = 10f;

    [Header("Wheel Segments")]
    [SerializeField] private WheelSegment[] segments;
    [SerializeField] private float pointerOffsetDegrees = 0f;

    [Header("UI")]
    [Tooltip("Shows 'Spin Now!', result after a spin, or 'No Spin Available'")]
    [SerializeField] private TMP_Text resultText;
    [Tooltip("Shows HH:MM:SS countdown until spins reset")]
    [SerializeField] private TMP_Text timerText;

    // ─────────────────────────────────────────────────────────────
    //  Spin allowance — 4 per 24 hours
    // ─────────────────────────────────────────────────────────────

    private const int    MAX_SPINS_PER_DAY   = 4;
    private const double RESET_HOURS         = 24.0;
    private const string PREF_SPINS_LEFT     = "SpinManager_SpinsLeft";
    private const string PREF_RESET_TIME     = "SpinManager_ResetTime";

    private int      _spinsLeft;
    private DateTime _nextResetUtc;
    private float    _ticker;

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (segments == null || segments.Length == 0)
            ResetToDefaultSegments();

        LoadSpinState();
    }

    private void Start()
    {
        RefreshUI();
    }

    private void Update()
    {
        _ticker += Time.deltaTime;
        if (_ticker < 1f) return;
        _ticker = 0f;

        // Auto-reset when the window expires
        if (_spinsLeft == 0 && DateTime.UtcNow >= _nextResetUtc)
        {
            _spinsLeft = MAX_SPINS_PER_DAY;
            SaveSpinState();
        }

        RefreshUI();
    }

    // ─────────────────────────────────────────────────────────────
    //  Public
    // ─────────────────────────────────────────────────────────────

    public bool IsSpinning { get; private set; }

    public void Spin()
    {
        if (IsSpinning)    return;
        if (_spinsLeft <= 0)
        {
            if (resultText != null) resultText.text = "No Spin Available";
            return;
        }

        _spinsLeft--;

        // Start the 24-hour window on the first spin of the day
        if (_spinsLeft == MAX_SPINS_PER_DAY - 1)
            _nextResetUtc = DateTime.UtcNow.AddHours(RESET_HOURS);

        SaveSpinState();
        StartCoroutine(SpinRoutine());
    }

    // ─────────────────────────────────────────────────────────────
    //  Spin coroutine
    // ─────────────────────────────────────────────────────────────

    private IEnumerator SpinRoutine()
    {
        IsSpinning = true;

        if (spinButton != null) spinButton.SetActive(false);
        if (resultText != null) resultText.text = "";

        float duration       = UnityEngine.Random.Range(minSpinDuration, maxSpinDuration);
        float targetAngle    = UnityEngine.Random.Range(0f, 360f);
        float extraRotations = UnityEngine.Random.Range(minExtraRotations, maxExtraRotations);

        float startAngle = wheel.eulerAngles.z;
        float endAngle   = startAngle + (extraRotations * 360f) + targetAngle;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t      = Mathf.Clamp01(elapsed / duration);
            float easedT = 1f - Mathf.Pow(1f - t, 3f);   // Ease Out Cubic

            wheel.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startAngle, endAngle, easedT));

            yield return null;
        }

        wheel.rotation = Quaternion.Euler(0f, 0f, endAngle);

        IsSpinning = false;

        // Re-show spin button only if spins remain
        if (spinButton != null)
            spinButton.SetActive(_spinsLeft > 0);

        EvaluateResult(endAngle);
        RefreshUI();
    }

    // ─────────────────────────────────────────────────────────────
    //  Determine winning segment and grant reward
    // ─────────────────────────────────────────────────────────────

    private void EvaluateResult(float endAngle)
    {
        if (segments == null || segments.Length == 0) return;

        float segmentSize     = 360f / segments.Length;
        float normalizedAngle = ((endAngle % 360f) + 360f) % 360f;
        float adjustedAngle   = (normalizedAngle + pointerOffsetDegrees + 360f) % 360f;
        int   index           = Mathf.FloorToInt(adjustedAngle / segmentSize) % segments.Length;

        WheelSegment won = segments[index];

        Debug.Log($"[SpinManager] Segment {index} → {won.label} ({won.pcReward} PC)");

        if (resultText != null)
        {
            resultText.text = won.pcReward > 0
                ? $"You won {won.pcReward:N0} PC!"
                : "Better luck next time!";
        }

        if (won.pcReward > 0)
            GrantPC(won.pcReward);
    }

    // ─────────────────────────────────────────────────────────────
    //  UI refresh
    // ─────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        bool hasSpins = _spinsLeft > 0;

        // Spin button visibility (only when not mid-spin)
        if (spinButton != null && !IsSpinning)
            spinButton.SetActive(hasSpins);

        // Result text default state
        if (resultText != null && !IsSpinning)
        {
            // Only overwrite if it's currently showing a default/state string
            // (don't overwrite mid-win message while timer ticks)
            if (hasSpins)
            {
                // Only reset to "Spin Now!" if not showing a won/lost result
                if (resultText.text == "No Spin Available" || resultText.text == "")
                    resultText.text = "Spin Now!";
            }
            else
            {
                resultText.text = "No Spin Available";
            }
        }

        // Timer text
        if (timerText != null)
        {
            if (hasSpins)
            {
                timerText.text = $"{_spinsLeft} / {MAX_SPINS_PER_DAY} spins left";
            }
            else
            {
                TimeSpan remaining = _nextResetUtc - DateTime.UtcNow;
                if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
                timerText.text = $"Resets in {(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  PlayFab — grant PC
    // ─────────────────────────────────────────────────────────────

    private void GrantPC(int amount)
    {
        PlayFabClientAPI.AddUserVirtualCurrency(
            new AddUserVirtualCurrencyRequest
            {
                VirtualCurrency = "PC",
                Amount          = amount
            },
            result => Debug.Log($"[SpinManager] +{amount} PC granted. New balance: {result.Balance}"),
            error  => Debug.LogWarning($"[SpinManager] Grant failed: {error.GenerateErrorReport()}"));
    }

    // ─────────────────────────────────────────────────────────────
    //  Save / Load spin state
    // ─────────────────────────────────────────────────────────────

    private void SaveSpinState()
    {
        PlayerPrefs.SetInt(PREF_SPINS_LEFT, _spinsLeft);
        PlayerPrefs.SetString(PREF_RESET_TIME, _nextResetUtc.Ticks.ToString());
        PlayerPrefs.Save();
    }

    private void LoadSpinState()
    {
        _spinsLeft = PlayerPrefs.GetInt(PREF_SPINS_LEFT, MAX_SPINS_PER_DAY);

        string saved = PlayerPrefs.GetString(PREF_RESET_TIME, "");
        if (!string.IsNullOrEmpty(saved) && long.TryParse(saved, out long ticks))
            _nextResetUtc = new DateTime(ticks, DateTimeKind.Utc);
        else
            _nextResetUtc = DateTime.UtcNow.AddHours(RESET_HOURS);

        // If the reset time has already passed, restore full spins
        if (_spinsLeft == 0 && DateTime.UtcNow >= _nextResetUtc)
        {
            _spinsLeft    = MAX_SPINS_PER_DAY;
            _nextResetUtc = DateTime.UtcNow.AddHours(RESET_HOURS);
            SaveSpinState();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Editor helper
    // ─────────────────────────────────────────────────────────────

    [ContextMenu("Reset To Default Segments")]
    private void ResetToDefaultSegments()
    {
        segments = new WheelSegment[]
        {
            new WheelSegment { label = "500 PC",  pcReward = 500  },
            new WheelSegment { label = "800 PC",  pcReward = 800  },
            new WheelSegment { label = "100 PC",  pcReward = 100  },
            new WheelSegment { label = "200 PC",  pcReward = 200  },
            new WheelSegment { label = "1000 PC", pcReward = 1000 },
            new WheelSegment { label = "300 PC",  pcReward = 300  },
            new WheelSegment { label = "JACKPOT", pcReward = 5000 },
            new WheelSegment { label = "200 PC",  pcReward = 200  },
            new WheelSegment { label = "700 PC",  pcReward = 700  },
            new WheelSegment { label = "400 PC",  pcReward = 400  },
            new WheelSegment { label = "500 PC",  pcReward = 500  },
            new WheelSegment { label = "LOSE",    pcReward = 0    },
            new WheelSegment { label = "1000 PC", pcReward = 1000 },
            new WheelSegment { label = "100 PC",  pcReward = 100  },
            new WheelSegment { label = "1500 PC", pcReward = 1500 },
            new WheelSegment { label = "100 PC",  pcReward = 100  },
            new WheelSegment { label = "500 PC",  pcReward = 500  },
            new WheelSegment { label = "100 PC",  pcReward = 100  },
        };
    }
}
