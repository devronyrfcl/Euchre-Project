using TMPro;
using UnityEngine;

/// <summary>Sits on the "Player Score Brakedown" card prefab.</summary>
public class PlayerScoreBreakdownCard : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text valueText;

    public void Setup(string title, int value)
    {
        if (titleText != null) titleText.text = title;
        if (valueText != null) valueText.text = $"+{value}";
    }
}
