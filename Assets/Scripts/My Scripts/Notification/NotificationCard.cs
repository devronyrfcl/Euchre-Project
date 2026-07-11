using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sits on each notification card prefab.
/// Shows title, short message, timestamp and a Show More button.
/// Show More calls PlayFabManager.ShowError() with full message.
/// </summary>
public class NotificationCard : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;    // one line preview
    [SerializeField] private TMP_Text timestampText;
    [SerializeField] private Button showMoreButton;
    [SerializeField] private Image unreadIndicator; // dot shown if unread

    private string _fullMessage;
    private PlayFabManager _manager;

    // ─────────────────────────────────────────────────────────────
    //  Setup
    // ─────────────────────────────────────────────────────────────

    public void Setup(NotificationEntry entry, PlayFabManager manager)
    {
        _fullMessage = entry.message;
        _manager = manager;

        if (titleText != null)
            titleText.text = entry.title;

        if (messageText != null)
        {
            // Truncate to one line — max 45 chars then ellipsis
            string preview = entry.message.Length > 45
                ? entry.message.Substring(0, 45) + "..."
                : entry.message;
            messageText.text = preview;
        }

        if (timestampText != null)
            timestampText.text = entry.timestamp;

        // Show unread dot
        if (unreadIndicator != null)
            unreadIndicator.gameObject.SetActive(!entry.isRead);

        showMoreButton?.onClick.AddListener(OnShowMoreClicked);
    }

    // ─────────────────────────────────────────────────────────────
    //  Button
    // ─────────────────────────────────────────────────────────────

    private void OnShowMoreClicked()
    {
        if (_manager != null)
            _manager.ShowErrorPublic(_fullMessage);
        else
            Debug.LogWarning("[NotificationCard] PlayFabManager reference missing.");
    }
}