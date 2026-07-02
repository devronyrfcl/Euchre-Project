using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages all card visuals in the gameplay scene.
/// Handles: deal animation, hand display, card selection,
///          card play animation to table, table clear.
///
/// Coordinate system is Canvas-space (RectTransform).
/// All positions are anchored positions in the Canvas.
/// </summary>
public class EuchreCardManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    // Add singleton at top of class
    public static EuchreCardManager Instance { get; private set; }

    [Header("=== CANVAS ===")]
    [SerializeField] private Canvas      gameCanvas;
    [SerializeField] private RectTransform cardHolder; // "PlayerCardHolder" object

    [Header("=== PREFABS ===")]
    [SerializeField] private GameObject shuffleCardPrefab;  // backside card for dealing anim
    [SerializeField] private GameObject playableCardPrefab; // front-face card with button

    [Header("=== CARD SPRITES ===")]
    [Tooltip("Assign 24 card sprites in order: 9C,9D,9H,9S,TC,TD...")]
    [SerializeField] private List<Sprite> cardSprites = new List<Sprite>();
    [SerializeField] private Sprite       cardBackSprite;

    [Header("=== PLAY BUTTON ===")]
    [SerializeField] private GameObject playCardButton; // assign in Inspector

    // ─────────────────────────────────────────────────────────────
    //  Deal Source Positions (canvas anchored positions)
    //  Index 0 = local (me), 1 = partner, 2 = left, 3 = right
    // ─────────────────────────────────────────────────────────────

    // Where shuffle cards FLY TO for each player
    // "Throw" destination per player panel

    
    private static readonly Vector2[] DEAL_THROW_POSITIONS = new Vector2[]
    {
        new Vector2(   0f,    0f),   // Seat 0 — me (not used as throw target, cards go to hand)
        new Vector2( -77.3f, 145.5f),// Seat 1 — partner up
        new Vector2(-270f,   20.5f), // Seat 2 — left enemy
        new Vector2( 255f,   17f)    // Seat 3 — right enemy
    };

    // Where local player's received cards go before revealing hand
    private static readonly Vector2 LOCAL_RECEIVE_POS = new Vector2(0f, -147f);

    // ─────────────────────────────────────────────────────────────
    //  Card-through positions per player (where played cards sit)
    //  35 degrees X rotation on each
    // ─────────────────────────────────────────────────────────────

    private static readonly Vector2[] TABLE_CARD_POSITIONS = new Vector2[]
    {
        new Vector2(   0f,    0f),   // me
        new Vector2(   0f,   70f),   // partner
        new Vector2( -43.7f, 40f),   // left
        new Vector2(  46f,   40f)    // right
    };

    private static readonly Vector3 TABLE_CARD_ROTATION = new Vector3(35f, 0f, 0f);

    // ─────────────────────────────────────────────────────────────
    //  Hand Card Positions (local player's 5 cards in hand)
    // ─────────────────────────────────────────────────────────────

    private static readonly Vector2[] HAND_POSITIONS = new Vector2[]
    {
        
        new Vector2(-54.9f, 0.8f),
        new Vector2( -2f,    11.85f),
        new Vector2( 36f,    11.45f),
        new Vector2( 73f,     4.95f),
        new Vector2(103.2f, -10.1f)
    };

    private static readonly float[] HAND_ROTATIONS_Z = new float[]
    {
        17.35f, 6.48f, -4.76f, -20.2f, -34f
    };

    private static readonly Vector2 HAND_CARD_SIZE = new Vector2(120f, 165f);

    // ─────────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    // sprite lookup: "9C", "TD", etc. → Sprite
    private Dictionary<string, Sprite> _spriteMap = new Dictionary<string, Sprite>();

    // Active playable card objects in local hand
    private List<EuchreCardUI> _handCards = new List<EuchreCardUI>();

    // Cards currently on the table (played this trick)
    private List<GameObject> _tableCards = new List<GameObject>();

    // Upcard shown during trump phase
    private GameObject _upcardObj;

    // Follow-suit validation state
    private List<string> _currentPlayedCards;
    private char         _currentTrump;
    private Dictionary<char, char> _currentAltSuits;
    private bool         _selectionEnabled = false;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildSpriteMap();
    }

    // ─────────────────────────────────────────────────────────────
    //  SPRITE MAP
    // ─────────────────────────────────────────────────────────────

    private void BuildSpriteMap()
    {
        // Expected order: for each type (9,T,J,Q,K,A) × suit (C,D,H,S)
        string[] types = { "9", "T", "J", "Q", "K", "A" };
        string[] suits = { "C", "D", "H", "S" };

        int idx = 0;
        foreach (string type in types)
        {
            foreach (string suit in suits)
            {
                if (idx < cardSprites.Count && cardSprites[idx] != null)
                    _spriteMap[type + suit] = cardSprites[idx];
                idx++;
            }
        }

        Debug.Log($"[CardManager] Built sprite map with {_spriteMap.Count} entries.");
    }

    // ─────────────────────────────────────────────────────────────
    //  DEAL ANIMATION
    //  Called after RPC_ReceiveHand on all clients.
    //  dealerSeat → which direction cards fly FROM
    //  localSeat  → which panel index am I
    // ─────────────────────────────────────────────────────────────

    public void AnimateDeal(List<string> localHand, string upcard,
                             int dealerSeat, int localSeat)
    {
        StartCoroutine(DealCoroutine(localHand, upcard, dealerSeat, localSeat));
    }

    private IEnumerator DealCoroutine(List<string> localHand, string upcard,
                                       int dealerSeat, int localSeat)
    {
        // Determine dealer's throw origin (canvas space)
        // Dealer is absolute seat → remap to panel index
        int dealerPanel = RemapSeatToPanel(dealerSeat, localSeat);
        Vector2 dealerOrigin = GetDealerOrigin(dealerPanel);

        // Deal 5 cards to each of 4 players
        // Panel 0 = me, 1 = partner, 2 = left, 3 = right
        for (int cardIdx = 0; cardIdx < 5; cardIdx++)
        {
            for (int panel = 0; panel < 4; panel++)
            {
                // Skip dealer panel as source (they deal out)
                Vector2 destination = panel == 0
                    ? LOCAL_RECEIVE_POS
                    : DEAL_THROW_POSITIONS[panel];

                SpawnShuffleCard(dealerOrigin, destination);
                yield return new WaitForSeconds(0.08f);
            }
        }

        // Wait for last shuffle card to arrive
        yield return new WaitForSeconds(0.4f);

        // Spawn upcard
        SpawnUpcard(upcard);

        yield return new WaitForSeconds(0.3f);

        // Reveal local hand
        yield return StartCoroutine(RevealLocalHand(localHand));

        // Notify network that deal is done
        GameplayNetworkManager.Instance?.BeginTrumpPhase1();
    }

    private Vector2 GetDealerOrigin(int dealerPanel)
    {
        // Dealer throws from their panel position
        // Panel 0 = me = bottom center
        switch (dealerPanel)
        {
            case 1: return DEAL_THROW_POSITIONS[1]; // partner
            case 2: return DEAL_THROW_POSITIONS[2]; // left
            case 3: return DEAL_THROW_POSITIONS[3]; // right
            default: return new Vector2(0f, -200f); // me (bottom)
        }
    }

    private void SpawnShuffleCard(Vector2 from, Vector2 to)
    {
        GameObject obj = Instantiate(shuffleCardPrefab, gameCanvas.transform);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = from;
        rt.localScale       = Vector3.one;

        // DOTween move to destination
        rt.DOAnchorPos(to, 0.25f).SetEase(Ease.OutCubic);

        // Keep shuffle cards; they are removed when hand is revealed
        StartCoroutine(DestroyAfter(obj, 2.5f));
    }

    private void SpawnUpcard(string cardType)
    {
        if (_upcardObj != null) Destroy(_upcardObj);

        _upcardObj = Instantiate(shuffleCardPrefab, gameCanvas.transform);
        RectTransform rt = _upcardObj.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0f, 20f); // center table

        // Show front face
        Image img = _upcardObj.GetComponent<Image>();
        if (img != null && _spriteMap.ContainsKey(cardType))
            img.sprite = _spriteMap[cardType];

        _upcardObj.transform.DOScale(Vector3.one * 1.1f, 0.3f).SetEase(Ease.OutBack);
    }

    public void HideUpcard()
    {
        if (_upcardObj == null) return;
        _upcardObj.transform
            .DOScale(Vector3.zero, 0.2f)
            .SetEase(Ease.InBack)
            .OnComplete(() =>
            {
                if (_upcardObj != null) Destroy(_upcardObj);
            });
    }

    private IEnumerator RevealLocalHand(List<string> hand)
    {
        // Clear any existing hand cards
        ClearHandCards();

        for (int i = 0; i < hand.Count && i < 5; i++)
        {
            string cardType = hand[i];

            GameObject obj = Instantiate(playableCardPrefab, cardHolder);
            RectTransform rt = obj.GetComponent<RectTransform>();

            // Start at receive position, scale 0
            rt.anchoredPosition = LOCAL_RECEIVE_POS;
            rt.sizeDelta        = HAND_CARD_SIZE;
            rt.localScale       = Vector3.zero;

            // Set sprite
            Image img = obj.GetComponent<Image>();
            if (img != null && _spriteMap.ContainsKey(cardType))
                img.sprite = _spriteMap[cardType];

            // Set up EuchreCardUI component
            EuchreCardUI cardUI = obj.GetComponent<EuchreCardUI>();
            if (cardUI != null)
                cardUI.Init(cardType, i, this);

            _handCards.Add(cardUI);

            // Tween to hand position
            Vector2 targetPos = HAND_POSITIONS[i];
            float   targetRot = HAND_ROTATIONS_Z[i];

            rt.DOAnchorPos(targetPos, 0.35f).SetEase(Ease.OutBack);
            rt.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack);
            rt.DOLocalRotate(new Vector3(0f, 0f, targetRot), 0.35f).SetEase(Ease.OutCubic);

            yield return new WaitForSeconds(0.1f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  DEALER PICKUP (upcard goes to dealer's hand)
    // ─────────────────────────────────────────────────────────────

    public void ShowDealerPickup(string cardType)
    {
        if (_upcardObj == null) return;

        RectTransform rt = _upcardObj.GetComponent<RectTransform>();
        rt.DOAnchorPos(LOCAL_RECEIVE_POS, 0.3f)
            .SetEase(Ease.InCubic)
            .OnComplete(() =>
            {
                if (_upcardObj != null) Destroy(_upcardObj);
                // Add upcard to hand visually
                StartCoroutine(AddCardToHand(cardType));
            });
    }

    private IEnumerator AddCardToHand(string cardType)
    {
        yield return new WaitForSeconds(0.1f);

        int slot = _handCards.Count < 5 ? _handCards.Count : 4;

        GameObject obj = Instantiate(playableCardPrefab, cardHolder);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = LOCAL_RECEIVE_POS;
        rt.sizeDelta        = HAND_CARD_SIZE;
        rt.localScale       = Vector3.zero;

        Image img = obj.GetComponent<Image>();
        if (img != null && _spriteMap.ContainsKey(cardType))
            img.sprite = _spriteMap[cardType];

        EuchreCardUI cardUI = obj.GetComponent<EuchreCardUI>();
        if (cardUI != null)
            cardUI.Init(cardType, slot, this);

        if (slot < _handCards.Count)
            _handCards[slot] = cardUI;
        else
            _handCards.Add(cardUI);

        rt.DOAnchorPos(slot < HAND_POSITIONS.Length ? HAND_POSITIONS[slot] : HAND_POSITIONS[4], 0.3f)
            .SetEase(Ease.OutBack);
        rt.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
    }

    // ─────────────────────────────────────────────────────────────
    //  CARD SELECTION (local player's turn)
    // ─────────────────────────────────────────────────────────────

    public void EnableCardSelection(bool enabled, List<string> playedCards,
                                     char trump, Dictionary<char, char> altSuits)
    {
        _selectionEnabled    = enabled;
        _currentPlayedCards  = playedCards;
        _currentTrump        = trump;
        _currentAltSuits     = altSuits;

        Debug.Log($"[CardManager] EnableCardSelection={enabled} HandCount={_handCards.Count}");

        foreach (EuchreCardUI card in _handCards)
            if (card != null) card.SetInteractable(enabled);

        // Show/hide play button with turn state
        if (playCardButton != null)
            playCardButton.SetActive(enabled);
    }

    /// <summary>Called by EuchreCardUI when local player taps a card.</summary>
    public void OnCardTapped(EuchreCardUI tappedCard)
    {
        if (!_selectionEnabled) return;

        // Deselect all others
        foreach (EuchreCardUI card in _handCards)
            if (card != null && card != tappedCard)
                card.SetSelected(false);

        tappedCard.ToggleSelect();
    }

    /// <summary>Called by Play button — plays the selected card.</summary>
    public void ConfirmPlaySelectedCard()
    {
        // DEBUG — remove after fixing
        Debug.Log($"[CardManager] ConfirmPlay called. SelectionEnabled={_selectionEnabled} " +
                $"HandCards={_handCards.Count}");

        if (!_selectionEnabled)
        {
            Debug.LogWarning("[CardManager] Selection not enabled — not your turn yet.");
            return;
        }

        EuchreCardUI selected = null;
        foreach (EuchreCardUI card in _handCards)
        {
            if (card != null && card.IsSelected)
            {
                selected = card;
                break;
            }
        }

        if (selected == null)
        {
            Debug.LogWarning("[CardManager] No card selected.");
            return;
        }

        if (!IsValidPlay(selected.CardType))
        {
            Debug.LogWarning("[CardManager] Invalid play — must follow suit.");
            // Optional: show message to player
            return;
        }

        string cardType = selected.CardType;

        _selectionEnabled = false;

        // Disable all cards
        foreach (EuchreCardUI card in _handCards)
            if (card != null) card.SetInteractable(false);

        // Remove from hand
        _handCards.Remove(selected);
        Destroy(selected.gameObject);

        // Notify network
        if (GameplayNetworkManager.Instance != null)
            GameplayNetworkManager.Instance.LocalPlayCard(cardType);
        else
            Debug.LogError("[CardManager] GameplayNetworkManager.Instance is null!");
    }
    private bool IsValidPlay(string cardType)
    {
        if (_currentPlayedCards == null || _currentPlayedCards.Count == 0) return true;
        if (_currentAltSuits == null || !_currentAltSuits.ContainsKey(_currentTrump)) return true;

        char leadSuit = _currentPlayedCards[0][1];
        if (_currentPlayedCards[0][0] == 'J' &&
            _currentPlayedCards[0][1] == _currentAltSuits[_currentTrump])
            leadSuit = _currentTrump;

        bool isLeftBower = cardType[0] == 'J' &&
                           _currentAltSuits.ContainsKey(_currentTrump) &&
                           cardType[1] == _currentAltSuits[_currentTrump];

        bool followsSuit = (cardType[1] == leadSuit && !isLeftBower)
                        || (isLeftBower && leadSuit == _currentTrump);

        if (followsSuit) return true;

        // Check if player has any card that follows suit
        foreach (EuchreCardUI card in _handCards)
        {
            if (card == null) continue;
            bool cardIsLeft = card.CardType[0] == 'J' &&
                              _currentAltSuits.ContainsKey(_currentTrump) &&
                              card.CardType[1] == _currentAltSuits[_currentTrump];
            bool hasLead = (card.CardType[1] == leadSuit && !cardIsLeft)
                        || (cardIsLeft && leadSuit == _currentTrump);
            if (hasLead) return false; // has a card that follows suit → must play it
        }

        return true; // no card follows suit → any card is valid
    }

    // ─────────────────────────────────────────────────────────────
    //  CARD PLAY ANIMATION TO TABLE
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a played card and animates it to the table position.
    /// panelIndex: 0=me, 1=partner, 2=left, 3=right
    /// </summary>
    public void AnimateCardToTable(int panelIndex, string cardType)
    {
        GameObject obj = Instantiate(shuffleCardPrefab, gameCanvas.transform);
        RectTransform rt = obj.GetComponent<RectTransform>();

        // Start from player's panel position
        Vector2 startPos = panelIndex == 0
            ? new Vector2(0f, -200f)
            : DEAL_THROW_POSITIONS[panelIndex];

        rt.anchoredPosition = startPos;
        rt.sizeDelta        = new Vector2(67.1137f, 91.4f);
        rt.localRotation    = Quaternion.Euler(TABLE_CARD_ROTATION);

        // Show front face
        Image img = obj.GetComponent<Image>();
        if (img != null && _spriteMap.ContainsKey(cardType))
            img.sprite = _spriteMap[cardType];
        else if (img != null)
            img.sprite = cardBackSprite;

        Vector2 tablePos = TABLE_CARD_POSITIONS[panelIndex];

        rt.DOAnchorPos(tablePos, 0.3f).SetEase(Ease.OutCubic);

        _tableCards.Add(obj);
    }

    public void ClearTableCards()
    {
        StartCoroutine(ClearTableCoroutine());
    }

    private IEnumerator ClearTableCoroutine()
    {
        yield return new WaitForSeconds(0.5f);

        foreach (GameObject c in _tableCards)
        {
            if (c == null) continue;
            RectTransform rt = c.GetComponent<RectTransform>();
            if (rt != null)
                rt.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack)
                    .OnComplete(() => { if (c != null) Destroy(c); });
        }

        _tableCards.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    private void ClearHandCards()
    {
        foreach (EuchreCardUI c in _handCards)
            if (c != null) Destroy(c.gameObject);
        _handCards.Clear();
    }

    private int RemapSeatToPanel(int seat, int localSeat)
    {
        if (seat == localSeat) return 0;
        int diff = (seat - localSeat + 4) % 4;
        return diff switch { 2 => 1, 1 => 2, 3 => 3, _ => 0 };
    }

    private IEnumerator DestroyAfter(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (obj != null) Destroy(obj);
    }
    // Add this method at the bottom of the class
    /// <summary>Called by TurnTimerController when time runs out on local turn.</summary>
    public void AutoPlayFirstValidCard()
    {
        if (!_selectionEnabled || _handCards.Count == 0) return;

        // Find first valid card
        EuchreCardUI toPlay = null;
        foreach (EuchreCardUI card in _handCards)
        {
            if (card == null) continue;
            if (IsValidPlay(card.CardType))
            {
                toPlay = card;
                break;
            }
        }

        if (toPlay == null && _handCards.Count > 0)
            toPlay = _handCards[0]; // fallback — play first card regardless

        if (toPlay == null) return;

        Debug.Log($"[CardManager] Auto-playing: {toPlay.CardType}");

        string cardType   = toPlay.CardType;
        _selectionEnabled = false;

        foreach (EuchreCardUI card in _handCards)
            if (card != null) card.SetInteractable(false);

        _handCards.Remove(toPlay);
        Destroy(toPlay.gameObject);

        GameplayNetworkManager.Instance?.LocalPlayCard(cardType);
    }
}
