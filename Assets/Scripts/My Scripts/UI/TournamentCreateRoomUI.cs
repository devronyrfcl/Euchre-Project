using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Room-creation panel — a name input field and a Create button.
/// Call SubmitCreateRoom() from the Create button's onClick (or directly, it's public).
/// </summary>
public class TournamentCreateRoomUI : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private GameObject     rootPanel;
    [SerializeField] private TMP_InputField roomNameInputField;
    [SerializeField] private Button         createButton;
    [SerializeField] private Button         cancelButton;

    private void Awake()
    {
        createButton?.onClick.AddListener(SubmitCreateRoom);
        cancelButton?.onClick.AddListener(ClosePanel);
    }

    /// <summary>Reads the room name from the input field and creates the room.
    /// Wired to the Create button, but public so it can be called directly too.</summary>
    public void SubmitCreateRoom()
    {
        if (roomNameInputField == null) return;

        string roomName = roomNameInputField.text.Trim();

        if (string.IsNullOrEmpty(roomName))
        {
            NotificationManager.Instance?.AddNotification("Invalid Name", "Please enter a room name.");
            return;
        }

        if (createButton != null) createButton.interactable = false;

        TournamentManager.Instance?.CreateRoom(roomName, (success, error) =>
        {
            if (createButton != null) createButton.interactable = true;

            if (!success)
            {
                NotificationManager.Instance?.AddNotification("Couldn't Create Room", error ?? "Please try again.");
                return;
            }

            roomNameInputField.text = "";
            ClosePanel();
            // TournamentPanelUI listens for OnStatusUpdated and switches to the dashboard view.
        });
    }

    public void ClosePanel()
    {
        rootPanel?.SetActive(false);
    }
}
