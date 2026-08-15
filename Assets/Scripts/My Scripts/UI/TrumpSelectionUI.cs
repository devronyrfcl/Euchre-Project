using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles both trump selection phases.
///
/// Phase 1 — "Order Up or Pass":
///   Upcard suit is shown. Player can order up (dealer picks it up) or pass.
///   Dealer is forced to pick if all others pass.
///
/// Phase 2 — "Choose Trump Suit":
///   Player picks any suit except the upcard suit, or passes.
///   Dealer is forced to pick in phase 2.
///
/// Called by GameplayNetworkManager when it is the local player's turn.
/// </summary>
public class TrumpSelectionUI : MonoBehaviour, ITrumpSelectionUI
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
    [SerializeField] private Button      orderUpButton;   // Phase 1 confirm
    [SerializeField] private Button      passButton;
    [SerializeField] private Button      goAloneButton;   // shown after order up

    [Header("=== GO ALONE CONFIRM GROUP ===")]
    [SerializeField] private GameObject  goAloneGroup;    // "Go Alone?" yes/no
    [SerializeField] private Button      goAloneYesButton;
    [SerializeField] private Button      goAloneNoButton;

    // ─────────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    private Action         _onOrderUp;
    private Action         _onPass;
    private Action<char>   _onSuitPicked;
    private bool           _phase1Active;
    private string         _excludedSuit;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        orderUpButton?.onClick.AddListener(OnOrderUpClicked);
        passButton?.onClick.AddListener(OnPassClicked);
        goAloneButton?.onClick.AddListener(OnGoAloneClicked);

        goAloneYesButton?.onClick.AddListener(OnGoAloneYes);
        goAloneNoButton?.onClick.AddListener(OnGoAloneNo);

        clubsButton?.onClick.AddListener(()    => OnSuitPicked('C'));
        diamondsButton?.onClick.AddListener(() => OnSuitPicked('D'));
        heartsButton?.onClick.AddListener(()   => OnSuitPicked('H'));
        spadesButton?.onClick.AddListener(()   => OnSuitPicked('S'));

        rootPanel?.SetActive(false);
        goAloneGroup?.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  PHASE 1 — Order Up / Pass
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Show Phase 1 popup.
    /// upcardSuit: suit char of the upcard.
    /// canPass: false when dealer is forced to pick.
    /// canOrderUp: false when the player lacks a natural trump card (house rule) —
    ///             the dealer is always exempt from this requirement.
    /// </summary>
    public void ShowPhase1(char upcardSuit, string upcardCardType, bool canPass, bool canOrderUp,
                            Action onOrderUp, Action onPass)
    {
        _onOrderUp    = onOrderUp;
        _onPass       = onPass;
        _phase1Active = true;

        titleText.text    = "Order Up?";
        subtitleText.text = !canOrderUp
            ? "You need a natural trump card to order this up — pass instead."
            : canPass
                ? "Order up this suit as trump, or pass."
                : "You must order up — dealer's choice.";

        // Show the actual upcard face
        upcardDisplay?.SetActive(true);
        suitButtonGroup?.SetActive(false);
        goAloneGroup?.SetActive(false);

        if (upcardSuitIcon != null)
        {
            Sprite cardSprite = EuchreCardManager.Instance?.GetCardSprite(upcardCardType);
            upcardSuitIcon.sprite = cardSprite != null ? cardSprite : GetSuitSprite(upcardSuit);
        }

        orderUpButton?.gameObject.SetActive(true);
        SetSuitButtonState(orderUpButton, canOrderUp);

        if (passButton != null)
            passButton.gameObject.SetActive(canPass);

        goAloneButton?.gameObject.SetActive(false);

        ShowPanel();
    }

    // ─────────────────────────────────────────────────────────────
    //  PHASE 2 — Choose Suit
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Show Phase 2 popup.
    /// excludedSuit: the upcard's suit — cannot be selected.
    /// canPass: false when this is the dealer (forced pick).
    /// canCallSuit: per-suit natural-trump check (house rule) — the dealer is always
    ///              exempt, so pass a predicate that always returns true for the dealer.
    /// </summary>
    public void ShowPhase2(string excludedSuit, bool canPass, Func<char, bool> canCallSuit,
                            Action<char> onSuitPicked, Action onPass)
    {
        _onSuitPicked  = onSuitPicked;
        _onPass        = onPass;
        _phase1Active  = false;
        _excludedSuit  = excludedSuit;

        titleText.text    = "Choose Trump";
        subtitleText.text = canPass
            ? "Pick a trump suit or pass."
            : "You must choose trump — dealer is forced.";

        upcardDisplay?.SetActive(false);
        suitButtonGroup?.SetActive(true);
        goAloneGroup?.SetActive(false);
        orderUpButton?.gameObject.SetActive(false);
        goAloneButton?.gameObject.SetActive(false);

        if (passButton != null)
            passButton.gameObject.SetActive(canPass);

        // Disable excluded suit button, and any suit the player lacks natural trump for
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
        // Show go alone option
        orderUpButton?.gameObject.SetActive(false);
        passButton?.gameObject.SetActive(false);
        goAloneButton?.gameObject.SetActive(true);

        titleText.text    = "Go Alone?";
        subtitleText.text = "Play without your partner, or play together.";
    }

    private void OnGoAloneClicked()
    {
        goAloneButton?.gameObject.SetActive(false);
        goAloneGroup?.SetActive(true);
    }

    private void OnGoAloneYes()
    {
        goAloneGroup?.SetActive(false);
        Hide();
        GameplayNetworkManager.Instance?.LocalGoAlone();
        _onOrderUp?.Invoke();
    }

    private void OnGoAloneNo()
    {
        goAloneGroup?.SetActive(false);
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
