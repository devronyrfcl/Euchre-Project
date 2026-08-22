using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Lean.Gui;
using System;

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
    [SerializeField] private RectTransform shufflecardHolder;

    [Header("=== PREFABS ===")]
    [SerializeField] private GameObject shuffleCardPrefab;  // backside card for dealing anim
    [SerializeField] private GameObject playableCardPrefab; // front-face card with button

    [Header("=== CARD SPRITES ===")]
    [Tooltip("Assign 24 card sprites in order: 9C,9D,9H,9S,TC,TD...")]
    [SerializeField] private List<Sprite> cardSprites = new List<Sprite>();
    [SerializeField] private Sprite       cardBackSprite;

    [Header("=== Error Panel ===")]
    [SerializeField] private NotificationManagerInGame notificationManager;

    [Header("=== PLAYER PANEL POSITIONS ===")]
    [Tooltip("Assign the RectTransform of each player panel root. " +
         "Index 0=me, 1=partner, 2=left, 3=right")]
    [SerializeField] private RectTransform[] playerPanelRects = new RectTransform[4];

    [Header("=== WINNER PARTICLE POSITIONS ===")]
    [Tooltip("Assign separate RectTransforms for particle spawn position per player. " +
         "Index 0=me, 1=partner, 2=left, 3=right")]
    [SerializeField] private RectTransform[] winnerParticlePositions = new RectTransform[4];

    [SerializeField] private GameObject winnerParticlePrefab;



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

    private static readonly float[] DEAL_THROW_ROTATIONS_Z = new float[]
    {
        0f,   // Seat 0 — me
        0f,   // Seat 1 — partner up
        -90f,   // Seat 2 — left enemy
        90f    // Seat 3 — right enemy
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

    private static readonly float[] TABLE_CARD_ROTATIONS_Z = new float[]
    {
        0f,   // me
        0f,   // partner
        90f,   // left
        -90f    // right
    };

    [Header("=== TABLE CARD ROTATION ===")]
    [SerializeField] private float tableCardRotationZ = 0f;

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

    // Dealer discard state — single tap discards immediately, no select+confirm
    private bool _discardMode = false;

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

    /// <summary>Looks up a card's front-face sprite by code (e.g. "9C", "TD").</summary>
    public Sprite GetCardSprite(string cardType) =>
        _spriteMap.TryGetValue(cardType, out Sprite sprite) ? sprite : null;

    public Sprite CardBackSprite => cardBackSprite;

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

        // Deal 5 cards to each of 4 players, in the same two-pass 2-3-2-3 / 3-2-3-2
        // batches (starting left of the dealer) that GameplayNetworkManager.DealCards()
        // actually deals with, so the animation matches the real deal order.
        int[][] passCounts = { new[] { 2, 3, 2, 3 }, new[] { 3, 2, 3, 2 } };
        int startSeat = (dealerSeat + 1) % 4;

        foreach (int[] counts in passCounts)
        {
            for (int i = 0; i < 4; i++)
            {
                int seat  = (startSeat + i) % 4;
                int panel = RemapSeatToPanel(seat, localSeat);
                int count = counts[i];

                for (int c = 0; c < count; c++)
                {
                    Vector2 destination = panel == 0
                        ? LOCAL_RECEIVE_POS
                        : DEAL_THROW_POSITIONS[panel];

                    SpawnShuffleCard(dealerOrigin, destination, panel);
                    yield return new WaitForSeconds(0.22f);
                }
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

    private void SpawnShuffleCard(Vector2 from, Vector2 to, int panelIndex = 0)
    {
        GameObject obj = Instantiate(shuffleCardPrefab, shufflecardHolder);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = from;
        rt.localScale       = Vector3.one;

        float rotZ = panelIndex < DEAL_THROW_ROTATIONS_Z.Length
            ? DEAL_THROW_ROTATIONS_Z[panelIndex] : 0f;
        rt.localRotation = Quaternion.Euler(0f, 0f, rotZ);

        AudioManager.Instance.PlaySFX("GiveCard");

        // DOTween move to destination
        rt.DOAnchorPos(to, 0.38f).SetEase(Ease.OutCubic);

        // Keep shuffle cards; they are removed when hand is revealed
        StartCoroutine(DestroyAfter(obj, 2.5f));
    }

    private void SpawnUpcard(string cardType)
    {
        if (_upcardObj != null) Destroy(_upcardObj);

        _upcardObj = Instantiate(shuffleCardPrefab, shufflecardHolder);
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
    //  DEALER PICKUP & DISCARD (Round 1 order-up only)
    // ─────────────────────────────────────────────────────────────

    // The picked-up upcard, while it sits as a 6th card awaiting discard.
    private EuchreCardUI _pickupCardUI;

    /// <summary>Called by GameplayNetworkManager when the local player is the dealer
    /// and someone ordered up the upcard. Animates it into the hand as a 6th card,
    /// then lets the dealer tap any of the 6 cards to discard it.</summary>
    public void BeginDealerDiscard(string upcardCardType)
    {
        _discardMode  = true;
        _pickupCardUI = null;

        foreach (EuchreCardUI card in _handCards)
            if (card != null) card.SetInteractable(true);

        ShowDealerPickup(upcardCardType);
    }

    private void ShowDealerPickup(string cardType)
    {
        if (_upcardObj == null)
        {
            StartCoroutine(AddCardToHand(cardType));
            return;
        }

        RectTransform rt = _upcardObj.GetComponent<RectTransform>();
        rt.DOAnchorPos(LOCAL_RECEIVE_POS, 0.3f)
            .SetEase(Ease.InCubic)
            .OnComplete(() =>
            {
                if (_upcardObj != null) Destroy(_upcardObj);
                _upcardObj = null;
                StartCoroutine(AddCardToHand(cardType));
            });
    }

    // Adds a 6th card to the local player's hand visually — always appends,
    // never overwrites an existing hand card slot.
    private IEnumerator AddCardToHand(string cardType)
    {
        yield return new WaitForSeconds(0.1f);

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
        {
            cardUI.Init(cardType, _handCards.Count, this);
            cardUI.SetInteractable(_discardMode);
        }

        _handCards.Add(cardUI);
        _pickupCardUI = cardUI;

        // Extrapolate one step past the last fan anchor so the 6th card leans in
        // at the same angle as the rest of the hand instead of sitting flat.
        int last = HAND_POSITIONS.Length - 1;
        Vector2 destPos = HAND_POSITIONS[last] + (HAND_POSITIONS[last] - HAND_POSITIONS[last - 1]);
        float   destRot = HAND_ROTATIONS_Z[last] + (HAND_ROTATIONS_Z[last] - HAND_ROTATIONS_Z[last - 1]);

        rt.DOAnchorPos(destPos, 0.3f).SetEase(Ease.OutBack)
            .OnComplete(() => cardUI?.RefreshRestState());
        rt.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
        rt.DOLocalRotate(new Vector3(0f, 0f, destRot), 0.3f).SetEase(Ease.OutCubic);
    }

    /// <summary>Removes the tapped card from hand, reflows the remaining 5 cards back
    /// into the fan to close the gap, and reports the discard to the network layer.</summary>
    private void HandleDiscardTap(EuchreCardUI tappedCard)
    {
        if (tappedCard == null) return;

        string cardType = tappedCard.CardType;

        _discardMode = false;

        foreach (EuchreCardUI card in _handCards)
            if (card != null) card.SetInteractable(false);

        _handCards.Remove(tappedCard);
        Destroy(tappedCard.gameObject);
        _pickupCardUI = null;

        ReflowHand();

        GameplayNetworkManager.Instance?.LocalDiscardCard(cardType);
    }

    // ─────────────────────────────────────────────────────────────
    //  HAND REFLOW (close the gap left by a played/discarded card)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Re-spaces the remaining hand cards across the same fan curve used for
    /// a full 5-card hand, so removing a card slides its neighbors in to close the gap
    /// instead of leaving an empty slot.</summary>
    private void ReflowHand()
    {
        int n = _handCards.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            EuchreCardUI card = _handCards[i];
            if (card == null) continue;

            float t = n == 1 ? 0.5f : (float)i / (n - 1);
            Vector2 pos = SampleCurve(HAND_POSITIONS, t);
            float   rot = SampleCurve(HAND_ROTATIONS_Z, t);

            card.SetSlotIndex(i);
            card.transform.SetSiblingIndex(i);

            RectTransform rt = card.GetComponent<RectTransform>();
            rt.DOAnchorPos(pos, 0.25f).SetEase(Ease.OutCubic)
                .OnComplete(() => card.RefreshRestState());
            rt.DOLocalRotate(new Vector3(0f, 0f, rot), 0.25f).SetEase(Ease.OutCubic);
        }
    }

    private Vector2 SampleCurve(Vector2[] points, float t)
    {
        float scaled = t * (points.Length - 1);
        int   idx0   = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, points.Length - 1);
        int   idx1   = Mathf.Clamp(idx0 + 1, 0, points.Length - 1);
        return Vector2.Lerp(points[idx0], points[idx1], scaled - idx0);
    }

    private float SampleCurve(float[] points, float t)
    {
        float scaled = t * (points.Length - 1);
        int   idx0   = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, points.Length - 1);
        int   idx1   = Mathf.Clamp(idx0 + 1, 0, points.Length - 1);
        return Mathf.Lerp(points[idx0], points[idx1], scaled - idx0);
    }

    // ─────────────────────────────────────────────────────────────
    //  HAND SORT (group by suit once trump is known)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Re-orders the local hand so trump (including the left bower)
    /// leads, followed by the remaining suits grouped together, highest card
    /// first within each group. Call once trump is finalized.</summary>
    public void SortHandByTrump(char trump, char leftBowerSuit)
    {
        if (_handCards.Count == 0) return;

        List<EuchreCardUI> sorted = new List<EuchreCardUI>(_handCards);
        sorted.Sort((a, b) =>
        {
            int groupA = SuitSortGroup(a.CardType, trump, leftBowerSuit);
            int groupB = SuitSortGroup(b.CardType, trump, leftBowerSuit);
            if (groupA != groupB) return groupA.CompareTo(groupB);

            int rankA = RankValue(a.CardType, trump, leftBowerSuit);
            int rankB = RankValue(b.CardType, trump, leftBowerSuit);
            return rankB.CompareTo(rankA);
        });

        for (int i = 0; i < sorted.Count && i < HAND_POSITIONS.Length; i++)
        {
            EuchreCardUI card = sorted[i];
            card.SetSlotIndex(i);
            card.transform.SetSiblingIndex(i);

            RectTransform rt = card.GetComponent<RectTransform>();
            rt.DOAnchorPos(HAND_POSITIONS[i], 0.3f)
                .SetEase(Ease.OutBack)
                .OnComplete(() => card.RefreshRestState());
            rt.DOLocalRotate(new Vector3(0f, 0f, HAND_ROTATIONS_Z[i]), 0.3f)
                .SetEase(Ease.OutCubic);
        }

        _handCards = sorted;
    }

    private int SuitSortGroup(string cardType, char trump, char leftBowerSuit)
    {
        char type = cardType[0];
        char suit = cardType[1];
        bool isTrump = suit == trump || (type == 'J' && suit == leftBowerSuit);
        if (isTrump) return 0;

        const string order = "CDHS";
        int idx = order.IndexOf(suit);
        return idx + 1;
    }

    private int RankValue(string cardType, char trump, char leftBowerSuit)
    {
        char type = cardType[0];
        char suit = cardType[1];

        if (type == 'J' && suit == trump) return 100;       // right bower
        if (type == 'J' && suit == leftBowerSuit) return 99; // left bower

        switch (type)
        {
            case 'A': return 6;
            case 'K': return 5;
            case 'Q': return 4;
            case 'J': return 3;
            case 'T': return 2;
            case '9': return 1;
            default:  return 0;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  CARD SELECTION (local player's turn)
    // ─────────────────────────────────────────────────────────────

    public void EnableCardSelection(bool enabled, List<string> playedCards,
                                     char trump, Dictionary<char, char> altSuits)
    {
        //playable card holder pos will be -56 on y axis when it's not your turn, and 0 when it is your turn
        if (enabled)
            cardHolder.DOAnchorPosY(0f, 0.3f).SetEase(Ease.OutCubic);
        else
            cardHolder.DOAnchorPosY(-56f, 0.3f).SetEase(Ease.OutCubic);

        if (playedCards != null)
            Debug.Log($"[CardManager] EnableCardSelection called. Enabled={enabled} " +
                      $"PlayedCards={string.Join(",", playedCards)} Trump={trump} AltSuits={altSuits}");
        if (altSuits != null)
            Debug.Log($"[CardManager] AltSuits: {string.Join(",", altSuits)}");


        _selectionEnabled = enabled;
        _currentPlayedCards  = playedCards;
        _currentTrump        = trump;
        _currentAltSuits     = altSuits;

        Debug.Log($"[CardManager] EnableCardSelection={enabled} HandCount={_handCards.Count}");

        foreach (EuchreCardUI card in _handCards)
            if (card != null) card.SetInteractable(enabled);

        
    }

    /// <summary>Called by EuchreCardUI when local player taps a card.</summary>
    public void OnCardTapped(EuchreCardUI tappedCard)
    {
        if (_discardMode)
        {
            HandleDiscardTap(tappedCard);
            return;
        }

        if (!_selectionEnabled) return;

        if (tappedCard.IsSelected)
        {
            // Second tap on already-selected card → play it
            ConfirmPlaySelectedCard();
            return;
        }

        // First tap → select this card, deselect others
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
            notificationManager.ShowWarningMessage("It's not your turn yet!");
            
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
            notificationManager.ShowWarningMessage("Please select a card to play.");
           
            return;
        }

        if (!IsValidPlay(selected.CardType))
        {
            Debug.LogWarning("[CardManager] Invalid play — must follow suit.");
            notificationManager.ShowWarningMessage("Invalid play! You must follow suit.");
            
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
        ReflowHand();

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
        GameObject obj = Instantiate(shuffleCardPrefab, shufflecardHolder);
        RectTransform rt = obj.GetComponent<RectTransform>();

        // Start from player's panel position
        Vector2 startPos = panelIndex == 0
            ? new Vector2(0f, -200f)
            : DEAL_THROW_POSITIONS[panelIndex];

        rt.anchoredPosition = startPos;
        rt.sizeDelta        = new Vector2(56f, 76f);
        rt.localRotation    = Quaternion.Euler(35f, 0f, TABLE_CARD_ROTATIONS_Z[panelIndex]);

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
        // Default clear — just destroys. 
        // Use FlyCardsToWinner() instead when a winner is known.
        StartCoroutine(ClearTableCoroutine());
    }

    private IEnumerator ClearTableCoroutine()
    {
        yield return new WaitForSeconds(0.3f);

        foreach (GameObject c in _tableCards)
        {
            if (c == null) continue;
            RectTransform rt = c.GetComponent<RectTransform>();
            if (rt != null)
                rt.DOScale(Vector3.zero, 0.2f)
                    .SetEase(Ease.InBack)
                    .OnComplete(() => { if (c != null) Destroy(c); });
        }
        _tableCards.Clear();
    }


    /// <summary>
    /// Called by GameplayNetworkManager after trick winner is decided.
    /// All 4 table cards fly to the winning player's panel position,
    /// then vanish. winnerPanelIndex: 0=me, 1=partner, 2=left, 3=right.
    /// </summary>
    public void FlyCardsToWinner(int winnerPanelIndex, Action onComplete = null)
    {
        StartCoroutine(FlyCardsToWinnerCoroutine(winnerPanelIndex, onComplete));
        

    }

    private IEnumerator FlyCardsToWinnerCoroutine(int winnerPanelIndex, Action onComplete)
    {
        if (_tableCards.Count == 0)
        {
            onComplete?.Invoke();
            yield break;
        }

        // Get winner panel world position converted to canvas space
        Vector2 targetPos = GetWinnerCanvasPosition(winnerPanelIndex);

        // Small delay so player can see the played cards before they fly
        yield return new WaitForSeconds(0.6f);

        // ── ADD THIS LINE ──────────────────────────────────────────
        SpawnWinnerParticle(winnerPanelIndex);

        float flyDuration = 0.35f;
        float delayPerCard = 0.04f;

        List<GameObject> cardsToFly = new List<GameObject>(_tableCards);
        _tableCards.Clear();

        for (int i = 0; i < cardsToFly.Count; i++)
        {
            GameObject card = cardsToFly[i];
            if (card == null) continue;

            RectTransform rt = card.GetComponent<RectTransform>();
            if (rt == null) continue;

            // Fly card to winner panel
            rt.DOAnchorPos(targetPos, flyDuration)
                .SetEase(Ease.InCubic)
                .SetDelay(i * delayPerCard);

            // Shrink and fade as it arrives
            rt.DOScale(Vector3.zero, flyDuration * 0.5f)
                .SetEase(Ease.InBack)
                .SetDelay(i * delayPerCard + flyDuration * 0.6f);

            // Destroy after animation
            float destroyDelay = flyDuration + delayPerCard * i + 0.1f;
            StartCoroutine(DestroyAfter(card, destroyDelay));
        }

        // Wait for all cards to finish flying
        float totalDuration = flyDuration
            + delayPerCard * (cardsToFly.Count - 1)
            + 0.15f;

        yield return new WaitForSeconds(totalDuration);

        onComplete?.Invoke();
    }

    private Vector2 GetWinnerCanvasPosition(int panelIndex)
    {
        if (panelIndex < 0 || panelIndex >= playerPanelRects.Length
            || playerPanelRects[panelIndex] == null)
        {
            // Fallback — fly to center
            return Vector2.zero;
        }

        


        RectTransform panelRt = playerPanelRects[panelIndex];

        // Convert panel world corners to canvas local position
        Vector3[] corners = new Vector3[4];
        panelRt.GetWorldCorners(corners);

        // Center of panel in world space
        Vector3 worldCenter = (corners[0] + corners[2]) / 2f;

        // Convert to canvas local position
        RectTransform canvasRt = gameCanvas.GetComponent<RectTransform>();
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRt,
            RectTransformUtility.WorldToScreenPoint(null, worldCenter),
            null,
            out localPos);

        return localPos;
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
        ReflowHand();

        GameplayNetworkManager.Instance?.LocalPlayCard(cardType);
    }

    /// <summary>
    /// Spawns winner particle at the winner's designated particle position.
    /// Called at the start of FlyCardsToWinnerCoroutine.
    /// </summary>
    /// 
    private void SpawnWinnerParticle(int winnerPanelIndex)
    {
        if (winnerParticlePrefab == null)
        {
            Debug.LogWarning("[CardManager] winnerParticlePrefab not assigned.");
            return;
        }

        if (winnerPanelIndex < 0 || winnerPanelIndex >= winnerParticlePositions.Length
            || winnerParticlePositions[winnerPanelIndex] == null)
        {
            Debug.LogWarning($"[CardManager] No particle position for panel {winnerPanelIndex}.");
            return;
        }

        RectTransform sourceRt = winnerParticlePositions[winnerPanelIndex];

        // Convert source world position to canvas local position
        RectTransform canvasRt = gameCanvas.GetComponent<RectTransform>();
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(
            null, sourceRt.position);

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRt,
            screenPoint,
            null,
            out localPoint);

        // Spawn under canvas
        GameObject particle = Instantiate(winnerParticlePrefab, gameCanvas.transform);
        RectTransform particleRt = particle.GetComponent<RectTransform>();

        if (particleRt != null)
        {
            particleRt.anchoredPosition = localPoint;
            particleRt.localScale = Vector3.one;
            particleRt.localRotation = Quaternion.identity;
        }

        AudioManager.Instance.PlaySFX("TrickWin_1");

        Destroy(particle, 5f);
    }




    /*private void SpawnWinnerParticle(int winnerPanelIndex)
    {
        if (winnerParticlePrefab == null)
        {
            Debug.LogWarning("[CardManager] winnerParticlePrefab not assigned.");
            return;
        }

        if (winnerPanelIndex < 0 || winnerPanelIndex >= winnerParticlePositions.Length
            || winnerParticlePositions[winnerPanelIndex] == null)
        {
            Debug.LogWarning($"[CardManager] No particle position assigned for panel {winnerPanelIndex}.");
            return;
        }

        // Spawn under canvas so it renders correctly in Screen Space
        GameObject particle = Instantiate(
            winnerParticlePrefab,
            gameCanvas.transform);

        RectTransform particleRt = particle.GetComponent<RectTransform>();

        if (particleRt != null)
        {
            // Match position of the designated particle RectTransform
            RectTransform sourceRt = winnerParticlePositions[winnerPanelIndex];
            particleRt.anchoredPosition = sourceRt.anchoredPosition;
            particleRt.localScale = sourceRt.localScale;
            particleRt.localRotation = sourceRt.localRotation;
        }

        Destroy(particle, 5f);
    }*/

}
