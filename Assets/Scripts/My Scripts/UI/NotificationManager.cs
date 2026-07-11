using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Lean.Gui;
using System.Collections;

public class NotificationManagerInGame : MonoBehaviour
{
    [SerializeField] private LeanWindow WarningMessagePanel;
    [SerializeField] private TMP_Text WarningMessageText;
    [SerializeField] private LeanPulse WarningMessagePulse;
    [SerializeField] private TMP_Text WarningMessagePulseText;



    public void ShowWarningMessage(string message)
    {
        WarningMessageText.text = message;
        WarningMessagePanel.TurnOn();
    }

    public void ShowWarningMessagePulse(string message)
    {
        WarningMessagePulseText.text = message;
        //WarningMessagePulse.Pulse();

        //pulse for 1.4 seconds
        StartCoroutine(PulseForDuration(1.4f));

    }

    IEnumerator PulseForDuration(float duration)
    {
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            WarningMessagePulse.Pulse();
            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }
    


}
