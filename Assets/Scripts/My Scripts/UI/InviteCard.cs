using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sits on each invite card prefab spawned in Invite Panel Content.
/// Shows friend name, game name, game level.
/// Invite button calls back to PlayFabManager.
/// </summary>
public class InviteCard : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private TMP_Text friendNameText;
    [SerializeField] private TMP_Text gameNameText;
    [SerializeField] private TMP_Text gameLevelText;
    [SerializeField] private Button   inviteButton;
    [SerializeField] private Image    avatarImage;
    [SerializeField] private Sprite   defaultAvatar;

    private string        _friendPlayFabId;
    private string        _friendName;
    private PlayFabManager _manager;

    public void Setup(string friendPlayFabId, string friendName,
                      string gameName, string gameLevel,
                      Texture2D avatar, PlayFabManager manager)
    {
        _friendPlayFabId = friendPlayFabId;
        _friendName      = friendName;
        _manager         = manager;

        if (friendNameText != null) friendNameText.text = friendName;
        if (gameNameText   != null) gameNameText.text   = gameName;
        if (gameLevelText  != null) gameLevelText.text  = $"Level {gameLevel}";

        if (avatarImage != null)
        {
            if (avatar != null)
                avatarImage.sprite = Sprite.Create(avatar,
                    new Rect(0, 0, avatar.width, avatar.height),
                    new Vector2(0.5f, 0.5f));
            else
                avatarImage.sprite = defaultAvatar;
        }

        inviteButton?.onClick.AddListener(OnInviteClicked);
    }

    private void OnInviteClicked()
    {
        _manager?.SendMatchInviteTo(_friendPlayFabId, _friendName);
    }
}