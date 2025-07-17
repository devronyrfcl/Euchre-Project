using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class TimeTest : MonoBehaviour
{
    public TimeModuleManager timeModuleManager;

    public float TimerRange;
    public float TimerDelay;

    public UnityEvent Uthso;


    // Start is called before the first frame update
    void Start()
    {
        //SetTimerProperties();
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void SetTimerProperties()
    {

        timeModuleManager.SetupTimer(TimeModuleManager.TimerMode.CountUp, TimeModuleManager.TimeDisplayFormat.MinutesSecondsMoments, TimeModuleManager.SliderUpdateMode.PulsePerSecond, 10f, 5f);
        timeModuleManager.StartTimer();
    }

    public void UthsoXYZ()
    {
        Uthso.Invoke();
    }
}
