// TrumpPopupComponent.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TrumpPopupComponent : MonoBehaviour
{
    [Header("Suit Buttons")]
    public Button spadesButton;
    public Button heartsButton;
    public Button clubsButton;
    public Button diamondsButton;

    [Header("Pass Button")]
    public Button passButton;

    [Header("Title Text")]
    public TextMeshProUGUI titleText;

    // Stored as GameObject — no typed reference to GameManagerComponent needed
    private GameObject gameManagerObject;

    void Start()
    {
        gameManagerObject = GameObject.Find("GameManager");

        spadesButton  .onClick.AddListener(() => OnSuitPicked("S"));
        heartsButton  .onClick.AddListener(() => OnSuitPicked("H"));
        clubsButton   .onClick.AddListener(() => OnSuitPicked("C"));
        diamondsButton.onClick.AddListener(() => OnSuitPicked("D"));

        if (passButton != null)
            passButton.onClick.AddListener(OnPass);

        Hide();
    }

    public void Show(string excludedSuit, bool canPass)
    {
        gameObject.SetActive(true);

        SetButtonInteractable(spadesButton,   excludedSuit != "S");
        SetButtonInteractable(heartsButton,   excludedSuit != "H");
        SetButtonInteractable(clubsButton,    excludedSuit != "C");
        SetButtonInteractable(diamondsButton, excludedSuit != "D");

        if (passButton != null)
            passButton.gameObject.SetActive(canPass);

        titleText.text = canPass
            ? "Choose Trump or Pass"
            : "You must choose Trump";
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    void OnSuitPicked(string suit)
    {
        Hide();
        // SendMessage calls PickTrumpSuit(string) on GameManagerComponent
        // without needing a direct type reference
        gameManagerObject.SendMessage("PickTrumpSuit", suit,
            SendMessageOptions.DontRequireReceiver);
    }

    void OnPass()
    {
        Hide();
        gameManagerObject.SendMessage("Pass",
            SendMessageOptions.DontRequireReceiver);
    }

    void SetButtonInteractable(Button btn, bool interactable)
    {
        btn.interactable = interactable;

        // CanvasGroup must be on each button GameObject for alpha dimming
        CanvasGroup cg = btn.GetComponent<CanvasGroup>();
        if (cg != null)
            cg.alpha = interactable ? 1f : 0.35f;
    }
}