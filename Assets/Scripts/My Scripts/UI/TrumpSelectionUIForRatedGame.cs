using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Trump-selection popup for Rated matches.
/// Same two phases as TrumpSelectionUI (Order Up / Pass, then Choose Suit / Pass),
/// but with NO go-alone option — Rated matches are always team mode, so ordering up
/// (or choosing a suit) resolves immediately without a "go alone?" follow-up prompt.
///
/// Called by GameplayNetworkManager when it is the local player's turn, in place of
/// TrumpSelectionUI, whenever MatchConfig.matchType == "rated".
/// </summary>
public class TrumpSelectionUIForRatedGame : MonoBehaviour, ITrumpSelectionUI
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== ROOT PANEL ===")]
    [SerializeField] private GameObject  rootPanel;
    [SerializeField] private RectTransform panelRect;

    [Header("=== TITLE ===")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;

    [Header("=== UPCARD DISPLAY (Phase 1) ===")]
    [SerializeField] private GameObject  upcardDisplay;
    [SerializeField] private Image       upcardSuitIcon;
    [SerializeField] private Sprite      clubsIcon;
    [SerializeField] private Sprite      diamondsIcon;
    [SerializeField] private Sprite      heartsIcon;
    [SerializeField] private Sprite      spadesIcon;

    [Header("=== SUIT BUTTONS (Phase 2) ===")]
    [SerializeField] private GameObject  suitButtonGroup;
    [SerializeField] private Button      clubsButton;
    [SerializeField] private Button      diamondsButton;
    [SerializeField] private Button      heartsButton;
    [SerializeField] private Button      spadesButton;

    [Header("=== ACTION BUTTONS ===")]
    [Tooltip("Phase 1 'Yes' — order up the upcard as trump. No go-alone follow-up in rated mode.")]
    [SerializeField] private Button      orderUpButton;
    [Tooltip("'No' — pass. Used in both Phase 1 and Phase 2.")]
    [SerializeField] private Button      passButton;

    // ─────────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    private Action       _onOrderUp;
    private Action       _onPass;
    private Action<char> _onSuitPicked;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        orderUpButton?.onClick.AddListener(OnOrderUpClicked);
        passButton?.onClick.AddListener(OnPassClicked);

        clubsButton?.onClick.AddListener(()    => OnSuitPicked('C'));
        diamondsButton?.onClick.AddListener(() => OnSuitPicked('D'));
        heartsButton?.onClick.AddListener(()   => OnSuitPicked('H'));
        spadesButton?.onClick.AddListener(()   => OnSuitPicked('S'));

        rootPanel?.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  PHASE 1 — Order Up / Pass
    // ─────────────────────────────────────────────────────────────

    public void ShowPhase1(char upcardSuit, string upcardCardType, bool canPass, bool canOrderUp,
                            Action onOrderUp, Action onPass)
    {
        _onOrderUp = onOrderUp;
        _onPass    = onPass;

        titleText.text    = "Order Up?";
        subtitleText.text = !canOrderUp
            ? "You need a natural trump card to order this up — pass instead."
            : canPass
                ? "Order up this suit as trump, or pass."
                : "You must order up — dealer's choice.";

        upcardDisplay?.SetActive(true);
        suitButtonGroup?.SetActive(false);

        if (upcardSuitIcon != null)
        {
            Sprite cardSprite = EuchreCardManager.Instance?.GetCardSprite(upcardCardType);
            upcardSuitIcon.sprite = cardSprite != null ? cardSprite : GetSuitSprite(upcardSuit);
        }

        orderUpButton?.gameObject.SetActive(true);
        SetSuitButtonState(orderUpButton, canOrderUp);

        if (passButton != null)
            passButton.gameObject.SetActive(canPass);

        ShowPanel();
    }

    // ─────────────────────────────────────────────────────────────
    //  PHASE 2 — Choose Suit
    // ─────────────────────────────────────────────────────────────

    public void ShowPhase2(string excludedSuit, bool canPass, Func<char, bool> canCallSuit,
                            Action<char> onSuitPicked, Action onPass)
    {
        _onSuitPicked = onSuitPicked;
        _onPass       = onPass;

        titleText.text    = "Choose Trump";
        subtitleText.text = canPass
            ? "Pick a trump suit or pass."
            : "You must choose trump — dealer is forced.";

        upcardDisplay?.SetActive(false);
        suitButtonGroup?.SetActive(true);
        orderUpButton?.gameObject.SetActive(false);

        if (passButton != null)
            passButton.gameObject.SetActive(canPass);

        SetSuitButtonState(clubsButton,    excludedSuit != "C" && (canCallSuit?.Invoke('C') ?? true));
        SetSuitButtonState(diamondsButton, excludedSuit != "D" && (canCallSuit?.Invoke('D') ?? true));
        SetSuitButtonState(heartsButton,   excludedSuit != "H" && (canCallSuit?.Invoke('H') ?? true));
        SetSuitButtonState(spadesButton,   excludedSuit != "S" && (canCallSuit?.Invoke('S') ?? true));

        ShowPanel();
    }

    // ─────────────────────────────────────────────────────────────
    //  HIDE
    // ─────────────────────────────────────────────────────────────

    public void Hide()
    {
        if (panelRect != null)
        {
            panelRect.DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack)
                .OnComplete(() => rootPanel?.SetActive(false));
        }
        else
        {
            rootPanel?.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  BUTTON CALLBACKS
    // ─────────────────────────────────────────────────────────────

    private void OnOrderUpClicked()
    {
        // No go-alone step — resolve immediately, unlike TrumpSelectionUI.
        Hide();
        _onOrderUp?.Invoke();
    }

    private void OnPassClicked()
    {
        Hide();
        _onPass?.Invoke();
    }

    private void OnSuitPicked(char suit)
    {
        Hide();
        _onSuitPicked?.Invoke(suit);
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    private void ShowPanel()
    {
        rootPanel?.SetActive(true);

        if (panelRect != null)
        {
            panelRect.localScale = Vector3.zero;
            panelRect.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
        }
    }

    private Sprite GetSuitSprite(char suit) => suit switch
    {
        'C' => clubsIcon,
        'D' => diamondsIcon,
        'H' => heartsIcon,
        'S' => spadesIcon,
        _   => null
    };

    private void SetSuitButtonState(Button btn, bool interactable)
    {
        if (btn == null) return;
        btn.interactable = interactable;

        CanvasGroup cg = btn.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = interactable ? 1f : 0.3f;
    }
}
