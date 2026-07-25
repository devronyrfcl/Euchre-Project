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
    public int    pcReward;   // reference only — the server is authoritative on reward amount
}

/// <summary>
/// Daily spin wheel — one free spin every 24 hours, enforced server-side via CloudScript
/// (ClaimDailySpin / GetDailySpinStatus). The client never grants currency or decides the
/// result locally; it only animates the wheel to whatever segment the server says won.
///
/// IMPORTANT: the `segments` array here must match DAILY_SPIN_SEGMENTS in CloudScript
/// exactly — same order, same count (18) — since the server returns a segmentIndex that
/// this script maps directly onto this array to compute the landing angle.
/// </summary>
public class SpinManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform      wheel;
    [SerializeField] private GameObject     spinButton;
    [SerializeField] private PlayFabManager playFabManager;

    [Header("Spin Settings")]
    [SerializeField] private float minSpinDuration   = 4f;
    [SerializeField] private float maxSpinDuration   = 6f;
    [SerializeField] private float minExtraRotations = 5f;
    [SerializeField] private float maxExtraRotations = 10f;

    [Header("Wheel Segments")]
    [Tooltip("One entry per wedge, in order starting from the wedge at Z=0. Must match " +
             "the server's DAILY_SPIN_SEGMENTS order exactly. Right-click → Reset To Default Segments.")]
    [SerializeField] private WheelSegment[] segments;

    [Tooltip("Fine-tune if the wrong segment visually lands under the pointer. " +
             "Adjust in small steps (±5°) until it lines up.")]
    [SerializeField] private float pointerOffsetDegrees = 0f;

    [Header("UI")]
    [Tooltip("Shows 'Spin Now!', result after a spin, or 'No Spin Available'")]
    [SerializeField] private TMP_Text resultText;
    [Tooltip("Shows 'Daily spin available!' or a 'Resets in HH:MM:SS' countdown")]
    [SerializeField] private TMP_Text timerText;

    // ─────────────────────────────────────────────────────────────
    //  State — synced from the server, never decided locally
    // ─────────────────────────────────────────────────────────────

    private bool     _available;
    private DateTime _nextAvailableUtc = DateTime.UtcNow;
    private float    _ticker;
    private bool     _statusSynced;

    public bool IsSpinning { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (segments == null || segments.Length == 0)
            ResetToDefaultSegments();
    }

    private void Start()
    {
        RefreshUI();
        SyncStatusFromServer();
    }

    private void Update()
    {
        _ticker += Time.deltaTime;
        if (_ticker < 1f) return;
        _ticker = 0f;

        if (!_available && DateTime.UtcNow >= _nextAvailableUtc)
            _available = true;

        RefreshUI();
    }

    // ─────────────────────────────────────────────────────────────
    //  Status sync (on load — purely for UI, server re-checks on Spin() anyway)
    // ─────────────────────────────────────────────────────────────

    private void SyncStatusFromServer()
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "GetDailySpinStatus",
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                DailySpinStatusResult status = Deserialize<DailySpinStatusResult>(result.FunctionResult);
                if (status == null || !status.success) return;

                _available        = status.available;
                _nextAvailableUtc = EpochMsToUtc(status.nextAvailableAt);
                _statusSynced     = true;

                RefreshUI();
            },
            error => Debug.LogWarning($"[SpinManager] GetDailySpinStatus failed: {error.GenerateErrorReport()}"));
    }

    // ─────────────────────────────────────────────────────────────
    //  Public
    // ─────────────────────────────────────────────────────────────

    public void Spin()
    {
        if (IsSpinning) return;

        if (_statusSynced && !_available)
        {
            ShowResult("No Spin Available");
            return;
        }

        if (spinButton != null) spinButton.SetActive(false);
        if (resultText != null) resultText.text = "Claiming...";

        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "ClaimDailySpin",
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                ClaimDailySpinResult claim = Deserialize<ClaimDailySpinResult>(result.FunctionResult);

                if (claim == null || !claim.success)
                {
                    _available        = false;
                    _nextAvailableUtc = claim != null ? EpochMsToUtc(claim.nextAvailableAt) : DateTime.UtcNow;
                    _statusSynced     = true;

                    if (spinButton != null) spinButton.SetActive(false);
                    ShowResult("No Spin Available");
                    RefreshUI();
                    return;
                }

                _available        = false;
                _nextAvailableUtc = EpochMsToUtc(claim.nextAvailableAt);
                _statusSynced     = true;

                StartCoroutine(SpinRoutine(claim.segmentIndex, claim.reward, claim.label));
            },
            error =>
            {
                Debug.LogWarning($"[SpinManager] ClaimDailySpin failed: {error.GenerateErrorReport()}");
                if (spinButton != null) spinButton.SetActive(true);
                ShowResult("Something went wrong. Please try again.");
            });
    }

    /// <summary>Sets the on-wheel result text AND surfaces the same message via
    /// PlayFabManager.ShowErrorPublic — every outcome (win, lose, unavailable, error)
    /// goes through here so it's shown consistently no matter what the result is.</summary>
    private void ShowResult(string message)
    {
        if (resultText != null) resultText.text = message;
        playFabManager?.ShowErrorPublic(message);
    }

    // ─────────────────────────────────────────────────────────────
    //  Spin coroutine — animates to the server-chosen segment
    // ─────────────────────────────────────────────────────────────

    private IEnumerator SpinRoutine(int segmentIndex, int reward, string label)
    {
        IsSpinning = true;

        float duration       = UnityEngine.Random.Range(minSpinDuration, maxSpinDuration);
        float extraRotations = UnityEngine.Random.Range(minExtraRotations, maxExtraRotations);
        float targetAngle    = ComputeTargetAngleForSegment(segmentIndex);

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

        Debug.Log($"[SpinManager] Landed on segment {segmentIndex} → {label} ({reward} PC), granted server-side.");

        ShowResult(reward > 0 ? $"You won {reward:N0} PC!" : "Better luck next time!");
        playFabManager?.RefreshCurrencies();

        RefreshUI();
    }

    /// <summary>Inverse of the old client-side angle→segment mapping — finds the wheel
    /// rotation that lands the pointer on the given segment's center.</summary>
    private float ComputeTargetAngleForSegment(int segmentIndex)
    {
        if (segments == null || segments.Length == 0) return 0f;

        float segmentSize   = 360f / segments.Length;
        float segmentCenter = segmentIndex * segmentSize + segmentSize / 2f;

        return (segmentCenter - pointerOffsetDegrees + 360f) % 360f;
    }

    // ─────────────────────────────────────────────────────────────
    //  UI refresh
    // ─────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        if (IsSpinning) return;

        if (spinButton != null)
            spinButton.SetActive(_available);

        if (resultText != null)
        {
            if (_available)
            {
                if (resultText.text == "No Spin Available" || resultText.text == "Claiming..." || resultText.text == "")
                    resultText.text = "Spin Now!";
            }
            else
            {
                resultText.text = "No Spin Available";
            }
        }

        if (timerText != null)
        {
            if (_available)
            {
                timerText.text = "Daily spin available!";
            }
            else
            {
                TimeSpan remaining = _nextAvailableUtc - DateTime.UtcNow;
                if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
                timerText.text = $"Resets in {(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static DateTime EpochMsToUtc(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    private static T Deserialize<T>(object functionResult) where T : class
    {
        if (functionResult == null) return null;
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(functionResult);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
    }

    [Serializable]
    private class ClaimDailySpinResult
    {
        public bool   success;
        public string error;
        public int    segmentIndex;
        public string label;
        public int    reward;
        public long   nextAvailableAt;
    }

    [Serializable]
    private class DailySpinStatusResult
    {
        public bool success;
        public bool available;
        public long nextAvailableAt;
    }

    // ─────────────────────────────────────────────────────────────
    //  Editor helper — right-click the component header to use
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
