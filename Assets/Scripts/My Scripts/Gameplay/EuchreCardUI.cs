using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sits on each playable card prefab in the local player's hand.
/// Handles tap/click, selection highlight, and raise animation.
/// Communicates back to EuchreCardManager.
/// </summary>
[RequireComponent(typeof(Button))]
[RequireComponent(typeof(Image))]
public class EuchreCardUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Runtime Data
    // ─────────────────────────────────────────────────────────────

    public string CardType    { get; private set; }
    public bool   IsSelected  { get; private set; }
    public int    SlotIndex   { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Private
    // ─────────────────────────────────────────────────────────────

    private EuchreCardManager _manager;
    private RectTransform     _rt;
    private Button            _button;
    private Image             _image;

    private Vector2 _restPosition;
    private float   _restRotation;

    private const float RAISE_AMOUNT  = 18f;
    private const float TWEEN_SPEED   = 0.18f;

    private static readonly Color SELECTED_TINT   = new Color(0.75f, 1f, 0.75f, 1f);
    private static readonly Color UNSELECTED_TINT = Color.white;
    private static readonly Color DISABLED_TINT   = new Color(0.6f, 0.6f, 0.6f, 1f);

    // ─────────────────────────────────────────────────────────────
    //  Init
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rt     = GetComponent<RectTransform>();
        _button = GetComponent<Button>();
        _image  = GetComponent<Image>();

        _button.onClick.AddListener(OnTapped);
    }

    /// <summary>Called by EuchreCardManager after spawning.</summary>
    public void Init(string cardType, int slotIndex, EuchreCardManager manager)
    {
        CardType   = cardType;
        SlotIndex  = slotIndex;
        _manager   = manager;

        // Cache rest state after tween lands
        // Rest position is set by the tween in EuchreCardManager —
        // we read it after the tween completes via a small delay.
        Invoke(nameof(CacheRestState), 0.5f);

        SetInteractable(false);
    }

    private void CacheRestState()
    {
        _restPosition = _rt.anchoredPosition;
        _restRotation = _rt.localEulerAngles.z;
    }

    /// <summary>Re-caches the rest position/rotation from the card's current transform.
    /// Call after moving a card to a new resting spot (e.g. the dealer-discard swap)
    /// so the selection raise animation doesn't jump back to the old spot.</summary>
    public void RefreshRestState() => CacheRestState();

    /// <summary>Updates the fan slot this card occupies (e.g. after a hand re-sort).</summary>
    public void SetSlotIndex(int slotIndex) => SlotIndex = slotIndex;

    // ─────────────────────────────────────────────────────────────
    //  Interactability
    // ─────────────────────────────────────────────────────────────

    public void SetInteractable(bool interactable)
    {
        _button.interactable = interactable;
        _image.color         = interactable ? UNSELECTED_TINT : DISABLED_TINT;

        if (!interactable)
            SetSelected(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Selection
    // ─────────────────────────────────────────────────────────────

    private void OnTapped()
    {
        if (!_button.interactable) return;
        _manager?.OnCardTapped(this);
    }

    public void ToggleSelect()
    {
        SetSelected(!IsSelected);
    }

    public void SetSelected(bool selected)
    {
        IsSelected   = selected;
        _image.color = selected ? SELECTED_TINT : UNSELECTED_TINT;

        // Raise or lower
        float targetY = selected
            ? _restPosition.y + RAISE_AMOUNT
            : _restPosition.y;

        _rt.DOAnchorPosY(targetY, TWEEN_SPEED).SetEase(Ease.OutCubic);
    }
}
