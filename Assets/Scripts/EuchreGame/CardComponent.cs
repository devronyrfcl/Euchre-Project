using System.Collections.Generic;
using UnityEngine;

public class CardComponent : MonoBehaviour
{
    private static readonly Dictionary<string, string> TypeMap = new Dictionary<string, string>()
    {
        {"9","9"}, {"10","T"}, {"jack","J"},
        {"queen","Q"}, {"king","K"}, {"ace","A"}
    };
    private static readonly Dictionary<string, string> SuitMap = new Dictionary<string, string>()
    {
        {"diamonds","D"}, {"hearts","H"},
        {"spades","S"}, {"clubs","C"}
    };

    private static Dictionary<string, Sprite> CardImages;

    private string cardType;
    private List<bool> unlocked = new List<bool>() { false, false };
    private bool isSelected;
    private Vector3 restPosition;
    private Vector3 targetPosition;

    private const float RaiseAmount  = 0.4f;
    private const float MoveSpeed    = 10f;

    private static readonly Color SelectedTint   = new Color(0.7f, 1f, 0.7f);
    private static readonly Color UnselectedTint = Color.white;

    private SpriteRenderer spriteRenderer;
    private BoxCollider2D  boxCollider;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        boxCollider    = GetComponent<BoxCollider2D>();

        if (CardImages == null || CardImages.Count == 0)
            BuildCardDictionary();

        ResizeColliderToSprite(); // 👈 ADD THIS
    }

    void ResizeColliderToSprite()
    {
        if (spriteRenderer.sprite == null) return;

        boxCollider.size = spriteRenderer.sprite.bounds.size;
        boxCollider.offset = spriteRenderer.sprite.bounds.center;
    }

    void Start()
    {
        // nothing needed here
    }

    void Update()
    {
        SmoothMoveToTarget();
        HandleTapInput();
    }

    // ─── Tap Detection — supports both Legacy and New Input System ─
    void HandleTapInput()
    {
        if (!unlocked.Contains(true)) return;

        bool tappedThisFrame = false;
        Vector3 worldPos     = Vector3.zero;

#if ENABLE_INPUT_SYSTEM
        // ── New Input System path ──────────────────────────────────
        var mouse       = UnityEngine.InputSystem.Mouse.current;
        var touchscreen = UnityEngine.InputSystem.Touchscreen.current;

        if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
        {
            Vector2 pos = touchscreen.primaryTouch.position.ReadValue();
            worldPos        = Camera.main.ScreenToWorldPoint(new Vector3(pos.x, pos.y, 0f));
            worldPos.z      = transform.position.z;
            tappedThisFrame = true;
        }
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 pos = mouse.position.ReadValue();
            worldPos        = Camera.main.ScreenToWorldPoint(new Vector3(pos.x, pos.y, 0f));
            worldPos.z      = transform.position.z;
            tappedThisFrame = true;
        }
#else
        // ── Legacy Input System path ───────────────────────────────
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                worldPos        = Camera.main.ScreenToWorldPoint(touch.position);
                worldPos.z      = transform.position.z;
                tappedThisFrame = true;
            }
        }
        else if (Input.GetMouseButtonDown(0))
        {
            worldPos        = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            worldPos.z      = transform.position.z;
            tappedThisFrame = true;
        }
#endif

        if (!tappedThisFrame) return;

        if (boxCollider != null && boxCollider.bounds.Contains(worldPos))
        {
            if (isSelected)
            {
                Deselect();
            }
            else
            {
                GameObject gm = GameObject.Find("GameManager");
                if (gm != null)
                    gm.SendMessage("DeselectAllCards",
                        SendMessageOptions.DontRequireReceiver);
                Select();
            }
        }
    }

    public void Select()
    {
        isSelected           = true;
        spriteRenderer.color = SelectedTint;
        SetTargetPosition(new Vector3(
            restPosition.x,
            restPosition.y + RaiseAmount,
            restPosition.z - 1f));
    }

    public void Deselect()
    {
        isSelected           = false;
        spriteRenderer.color = UnselectedTint;
        SetTargetPosition(restPosition);
    }

    void SmoothMoveToTarget()
    {
        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            Time.deltaTime * MoveSpeed);
    }

    public void BuildCardDictionary()
    {
        CardImages = new Dictionary<string, Sprite>();
        foreach (var type in TypeMap)
        {
            foreach (var suit in SuitMap)
            {
                string key  = type.Value + suit.Value;
                string path = $"Images/Cards/{type.Key}_of_{suit.Key}";
                Sprite spr  = Resources.Load<Sprite>(path);
                if (spr == null)
                    Debug.LogWarning($"Missing sprite: Resources/{path}");
                else
                    CardImages[key] = spr;
            }
        }
        Sprite back = Resources.Load<Sprite>("Images/Cards/card_back");
        if (back == null)
            Debug.LogWarning("Missing sprite: Resources/Images/Cards/card_back");
        else
            CardImages["CB"] = back;

        Debug.Log($"CardComponent: Loaded {CardImages.Count} card sprites.");
    }

    void OnDrawGizmos()
    {
        if (boxCollider == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(boxCollider.bounds.center, boxCollider.bounds.size);
    }

    public void SetCard(string card)
    {
        cardType = card;

        if (CardImages == null || CardImages.Count == 0)
            BuildCardDictionary();

        if (CardImages.ContainsKey(card))
        {
            spriteRenderer.sprite = CardImages[card];
            ResizeColliderToSprite(); // 👈 VERY IMPORTANT
        }
        else
        {
            Debug.LogError($"No sprite for card key '{card}'");
        }
    }

    public void SetRestPosition(Vector3 pos)
    {
        restPosition   = pos;
        targetPosition = pos;
    }

    public void SetTargetPosition(Vector3 pos) => targetPosition = pos;
    public Vector3 GetTargetPosition()         => targetPosition;
    public Vector3 GetRestPosition()           => restPosition;
    public string  GetCardType()               => cardType;
    public bool    IsSelected()                => isSelected;

    public void SetUnlocked(List<bool> newUnlocked)
    {
        unlocked = newUnlocked;
        if (!unlocked.Contains(true))
            Deselect();
    }

    public string GetCardTypeIfSelectedForPlay()
    {
        if (unlocked.Count < 1 || !unlocked[0] || !isSelected) return null;
        return cardType;
    }

    public string GetCardTypeIfSelectedForDiscard()
    {
        if (unlocked.Count < 2 || !unlocked[1] || !isSelected) return null;
        return cardType;
    }
}