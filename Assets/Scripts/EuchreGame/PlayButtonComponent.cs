using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayButtonComponent : MonoBehaviour
{
    public enum ButtonMode { PlayCard, PickUpTrump, ConfirmWithPartner }
    private ButtonMode currentMode = ButtonMode.PlayCard;

    private Button button;
    private TextMeshProUGUI label;

    void Awake()
    {
        button = GetComponent<Button>();
        label  = GetComponentInChildren<TextMeshProUGUI>();
        button.onClick.AddListener(OnClicked);
    }

    void Start()
    {
        gameObject.SetActive(false);
    }

    void OnClicked()
    {
        GameObject gm = GameObject.Find("GameManager");
        if (gm == null) return;

        if (currentMode == ButtonMode.PlayCard)
            gm.SendMessage("OnPlayButtonPressed",
                SendMessageOptions.DontRequireReceiver);
        else if (currentMode == ButtonMode.PickUpTrump)
            gm.SendMessage("PickButtonClicked",
                SendMessageOptions.DontRequireReceiver);
        else if (currentMode == ButtonMode.ConfirmWithPartner)
            gm.SendMessage("ConfirmWithPartner",
                SendMessageOptions.DontRequireReceiver);
    }

    public void SetMode(ButtonMode mode)
    {
        currentMode = mode;
        if (label == null) return;
        label.text = mode switch
        {
            ButtonMode.PlayCard           => "Play Card",
            ButtonMode.PickUpTrump        => "Pick Up",
            ButtonMode.ConfirmWithPartner => "With Partner",
            _                             => "OK"
        };
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}