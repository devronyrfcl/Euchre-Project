using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main notification system controller.
/// Lives in the Main Menu scene.
/// Call NotificationManager.Instance.AddNotification(title, message)
/// from anywhere to push a new notification.
/// Loads history on start and spawns cards for all saved entries.
/// </summary>
public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== PANEL ===")]
    [SerializeField] private GameObject notificationPanel;

    [Header("=== CONTENT ===")]
    [SerializeField] private RectTransform content;
    [SerializeField] private GameObject notificationCardPrefab;

    [Header("=== BADGE (unread count on bell icon) ===")]
    [SerializeField] private GameObject badgeObject;
    [SerializeField] private TMP_Text badgeCountText;

    [Header("=== CLEAR BUTTON ===")]
    [SerializeField] private Button clearAllButton;

    [Header("=== PLAYFAB MANAGER ===")]
    [SerializeField] private PlayFabManager playFabManager;

    // ─────────────────────────────────────────────────────────────
    //  Constants
    // ─────────────────────────────────────────────────────────────

    private const float CARD_HEIGHT = 32.5f;
    private const int MAX_HISTORY = 50; // oldest removed beyond this

    // ─────────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    private NotificationHistory _history;
    private int _unreadCount = 0;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        clearAllButton?.onClick.AddListener(ClearAll);
        notificationPanel?.SetActive(false);

        LoadHistory();
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Call this from anywhere to push a new notification.
    /// title:   short header e.g. "Match Result"
    /// message: full detail e.g. "You won 400 PC! Team 1 scored 10 points."
    /// </summary>
    public void AddNotification(string title, string message)
    {
        NotificationEntry entry = new NotificationEntry(title, message);

        // Add to top of list
        _history.entries.Insert(0, entry);

        // Trim if over max
        if (_history.entries.Count > MAX_HISTORY)
            _history.entries.RemoveAt(_history.entries.Count - 1);

        // Save to XML
        NotificationSaveSystem.Save(_history);

        // Spawn card at top of content
        SpawnCard(entry, atTop: true);

        // Update unread badge
        _unreadCount++;
        RefreshBadge();

        Debug.Log($"[NotificationManager] New notification: [{title}] {message}");
    }

    /// <summary>Open the notification panel and mark all as read.</summary>
    public void OpenPanel()
    {
        notificationPanel?.SetActive(true);
        MarkAllRead();
    }

    /// <summary>Close the notification panel.</summary>
    public void ClosePanel()
    {
        notificationPanel?.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  LOAD HISTORY
    // ─────────────────────────────────────────────────────────────

    private void LoadHistory()
    {
        _history = NotificationSaveSystem.Load();

        // Reset content height
        content.sizeDelta = new Vector2(content.sizeDelta.x, 0f);

        // Count unread
        _unreadCount = 0;

        // Spawn all saved cards in order (newest first — already sorted on save)
        foreach (NotificationEntry entry in _history.entries)
        {
            SpawnCard(entry, atTop: false);
            if (!entry.isRead) _unreadCount++;
        }

        RefreshBadge();
    }

    // ─────────────────────────────────────────────────────────────
    //  SPAWN CARD
    // ─────────────────────────────────────────────────────────────

    private void SpawnCard(NotificationEntry entry, bool atTop)
    {
        if (notificationCardPrefab == null || content == null) return;

        GameObject obj = Instantiate(notificationCardPrefab, content);

        // Move to top of content if new notification
        if (atTop)
            obj.transform.SetAsFirstSibling();

        NotificationCard card = obj.GetComponent<NotificationCard>();
        card?.Setup(entry, playFabManager);

        // Expand content height
        content.sizeDelta = new Vector2(
            content.sizeDelta.x,
            content.sizeDelta.y + CARD_HEIGHT);
    }

    // ─────────────────────────────────────────────────────────────
    //  MARK ALL READ
    // ─────────────────────────────────────────────────────────────

    private void MarkAllRead()
    {
        bool changed = false;

        foreach (NotificationEntry entry in _history.entries)
        {
            if (!entry.isRead)
            {
                entry.isRead = true;
                changed = true;
            }
        }

        if (changed)
            NotificationSaveSystem.Save(_history);

        _unreadCount = 0;
        RefreshBadge();

        // Hide all unread dots on existing cards
        foreach (Transform child in content)
        {
            NotificationCard card = child.GetComponent<NotificationCard>();
            // Cards handle their own dot via Setup — 
            // refresh by hiding dot directly
            Transform dot = child.Find("UnreadIndicator");
            if (dot != null) dot.gameObject.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  CLEAR ALL
    // ─────────────────────────────────────────────────────────────

    private void ClearAll()
    {
        // Destroy all card GameObjects
        foreach (Transform child in content)
            Destroy(child.gameObject);

        // Reset content height
        content.sizeDelta = new Vector2(content.sizeDelta.x, 0f);

        // Clear history
        _history.entries.Clear();
        NotificationSaveSystem.Clear();

        _unreadCount = 0;
        RefreshBadge();

        Debug.Log("[NotificationManager] All notifications cleared.");
    }

    // ─────────────────────────────────────────────────────────────
    //  BADGE
    // ─────────────────────────────────────────────────────────────

    private void RefreshBadge()
    {
        if (badgeObject != null)
            badgeObject.SetActive(_unreadCount > 0);

        if (badgeCountText != null)
            badgeCountText.text = _unreadCount > 99
                ? "99+"
                : _unreadCount.ToString();
    }
}