/*
To call and set the timer properties from another script use
(in case of Set Set a countup and MM:SS:MM visual mod of 10 seconds with a 2-second delay):

    timeModuleManager.SetupTimer(TimeModuleManager.TimerMode.CountUp, TimeModuleManager.TimeDisplayFormat.MinutesSecondsMoments, TimeModuleManager.SliderUpdateMode.PulsePerSecond, 10f, 5f);
    timeModuleManager.StartTimer();

use direct refernce "TimeModuleManager" or search using "FindObjectOfType<TimeModuleManager>()"
*/

using System.Collections;
using UnityEngine.Events;
using UnityEngine;
using TMPro;
using UnityEngine.UI;



public class TimeModuleManager : MonoBehaviour
{


    public enum TimerMode //these modes are used to countdown and count up
    {
        Countdown,
        CountUp
    }

    public enum TimeDisplayFormat
    {
        MinutesSeconds,        // MM:SS
        MinutesSecondsMoments, // MM:SS:MM (moments = 1/60 sec)
        SecondsMinutes,        // SS:MM (less common)
        SecondsMilliseconds,    // SS.fff (seconds.milliseconds)
        SecondsOnly // only seconds
    }
    public enum SliderUpdateMode
    {
        Smooth,
        PerSecond,
        PulsePerSecond
    }

    [Header("Timer Settings")]
    [Tooltip("Total target time in seconds (can have fractional part for moments)")]
    public float targetTotalSeconds = 60f;

    [Tooltip("Delay in seconds before the timer actually starts")]
    public float startDelay = 0f;

    public TimerMode timerMode = TimerMode.Countdown;

    [Header("UI")]
    public TextMeshProUGUI timerText;

    [Header("Slider Visuals")]
    public Slider timerSlider;
    public SliderUpdateMode sliderUpdateMode = SliderUpdateMode.Smooth;

    [Header("Display Format")]
    public TimeDisplayFormat displayFormat = TimeDisplayFormat.MinutesSecondsMoments;

    [Header("Events - Start")]
    public UnityEvent onCountdownStarted;
    public UnityEvent onCountUpStarted;

    [Header("Events - Finish")]
    public UnityEvent onCountdownFinished;
    public UnityEvent onCountUpFinished;

    [Header("Debug")]
    public bool enableDebugLog = false; //In case of need debug logs turn on the bool in inspector.

    private float currentTime;
    private Coroutine timerCoroutine;
    private float lastSecondReported = -1f;

    // Start is called before the first frame update
    void Start()
    {
        StartTimer();
    }



    public void StartTimer()
    {
        if (timerCoroutine != null)
            StopCoroutine(timerCoroutine);

        timerCoroutine = StartCoroutine(DelayedStart());
    }

    public void StopTimer()
    {
        if (timerCoroutine != null)
        {
            StopCoroutine(timerCoroutine);
            timerCoroutine = null;
        }
    }

    IEnumerator DelayedStart()
    {
        if (startDelay > 0f)
        {
            yield return new WaitForSeconds(startDelay);
        }

        currentTime = (timerMode == TimerMode.Countdown) ? targetTotalSeconds : 0f;

        if (timerMode == TimerMode.Countdown)
            onCountdownStarted?.Invoke();
        else
            onCountUpStarted?.Invoke();

        timerCoroutine = StartCoroutine(RunTimer());
    }

    IEnumerator RunTimer()
    {
        while (true)
        {
            DisplayTime(currentTime);

            if (timerMode == TimerMode.Countdown)
            {
                if (currentTime <= 0f)
                {
                    onCountdownFinished?.Invoke();
                    yield break;
                }

                currentTime -= 1f / 60f;
                if (currentTime < 0f) currentTime = 0f;
            }
            else // CountUp
            {
                if (currentTime >= targetTotalSeconds)
                {
                    onCountUpFinished?.Invoke();
                    yield break;
                }

                currentTime += 1f / 60f;
                if (currentTime > targetTotalSeconds) currentTime = targetTotalSeconds;
            }

            UpdateRoundSlider(currentTime);

            yield return new WaitForSeconds(1f / 60f);
        }


    }

    void DisplayTime(float time)
    {
        int totalMoments = Mathf.RoundToInt(time * 60f);

        int minutes = totalMoments / 3600;
        int seconds = (totalMoments % 3600) / 60;
        int moments = totalMoments % 60;

        string formattedTime = displayFormat switch
        {
            TimeDisplayFormat.MinutesSeconds => $"{minutes:D2}:{seconds:D2}",
            TimeDisplayFormat.MinutesSecondsMoments => $"{minutes:D2}:{seconds:D2}:{moments:D2}",
            TimeDisplayFormat.SecondsMinutes => $"{seconds:D2}:{minutes:D2}",
            TimeDisplayFormat.SecondsMilliseconds => $"{Mathf.FloorToInt(time):D2}.{(time - Mathf.Floor(time)) * 1000:000}",
            TimeDisplayFormat.SecondsOnly => $"{Mathf.FloorToInt(time)}", // 👈 new line for seconds only
            _ => $"{minutes:D2}:{seconds:D2}:{moments:D2}"
        };

        if (enableDebugLog)
            Debug.Log(formattedTime);

        if (timerText != null)
            timerText.text = formattedTime;
    }



    public void SetupTimer(TimerMode mode, TimeDisplayFormat textvisualmod, SliderUpdateMode slidermod, float targetSeconds, float delaySeconds)
    {
        timerMode = mode;
        displayFormat = textvisualmod;
        sliderUpdateMode = slidermod;
        targetTotalSeconds = targetSeconds;
        startDelay = delaySeconds;
    }
    
    void UpdateRoundSlider(float time)
    {
        if (timerSlider == null) return;

        float valueToSet = 0f;

        if (sliderUpdateMode == SliderUpdateMode.Smooth)
        {
            if (timerMode == TimerMode.CountUp)
                valueToSet = time / targetTotalSeconds;
            else // Countdown
                valueToSet = 1f - (time / targetTotalSeconds);
        }
        else if (sliderUpdateMode == SliderUpdateMode.PerSecond)
        {
            int intTime = Mathf.FloorToInt(time);
            if (lastSecondReported != intTime || time <= 0f || time >= targetTotalSeconds)
            {
                lastSecondReported = intTime;

                if (timerMode == TimerMode.CountUp)
                    valueToSet = intTime / targetTotalSeconds;
                else // Countdown
                    valueToSet = 1f - (intTime / targetTotalSeconds);
            }
            else
            {
             return; // No update this frame
            }
        }
        else if (sliderUpdateMode == SliderUpdateMode.PulsePerSecond)
        {
            float t = time % 1f;

            if (timerMode == TimerMode.Countdown)
                valueToSet = 1f - t; // 1 → 0
            else
                valueToSet = t;     // 0 → 1
        }

        timerSlider.value = Mathf.Clamp01(valueToSet);
    }


}
