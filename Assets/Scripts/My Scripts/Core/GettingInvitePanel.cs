using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shown when local player receives a match invite.
/// Accept → load match with shared room code.
/// Reject → clear invite.
/// </summary>
public class GettingInvitePanel : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private GameObject rootPanel;
    [SerializeField] private TMP_Text   inviterNameText;
    [SerializeField] private TMP_Text   gameNameText;
    [SerializeField] private TMP_Text   gameLevelText;
    [SerializeField] private Button     acceptButton;
    [SerializeField] private Button     rejectButton;

    private string         _roomCode;
    private PlayFabManager _manager;

    private void Awake()
    {
        acceptButton?.onClick.AddListener(OnAccept);
        rejectButton?.onClick.AddListener(OnReject);
        rootPanel?.SetActive(false);
    }

    public void Show(string inviterName, string gameName,
                     string gameLevel, string roomCode,
                     PlayFabManager manager)
    {
        _roomCode = roomCode;
        _manager  = manager;

        if (inviterNameText != null)
            inviterNameText.text = $"{inviterName} invited you!";

        if (gameNameText  != null) gameNameText.text  = gameName;
        if (gameLevelText != null) gameLevelText.text = $"Level {gameLevel}";

        rootPanel?.SetActive(true);
    }

    public void Hide()
    {
        rootPanel?.SetActive(false);
    }

    private void OnAccept()
    {
        Hide();
        _manager?.AcceptInvite(_roomCode);
    }

    private void OnReject()
    {
        Hide();
        _manager?.RejectInvite();
    }
}