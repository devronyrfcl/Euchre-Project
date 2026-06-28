using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FriendCard : MonoBehaviour
{
    [SerializeField] private RawImage profileImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text masterIdText;
    [SerializeField] private Button addFriendButton;
    [SerializeField] private Button removeFriendButton;
    [SerializeField] private Button cardButton;
    [SerializeField] private Texture2D defaultProfileTexture;

    private string _playFabId;
    private PlayFabManager _friendManager;

    public void Setup(string displayName, string playFabId, bool isFriend, Texture2D profileTex, PlayFabManager manager)
    {
        _playFabId = playFabId;
        _friendManager = manager;

        nameText.text = displayName;
        masterIdText.text = playFabId;

        if (profileTex != null)
            profileImage.texture = profileTex;
        else if (defaultProfileTexture != null)
            profileImage.texture = defaultProfileTexture;

        addFriendButton.gameObject.SetActive(!isFriend);
        removeFriendButton.gameObject.SetActive(isFriend);

        addFriendButton.onClick.RemoveAllListeners();
        removeFriendButton.onClick.RemoveAllListeners();

        addFriendButton.onClick.AddListener(() => _friendManager.AddFriend(_playFabId, this));
        removeFriendButton.onClick.AddListener(() => _friendManager.RemoveFriend(_playFabId, this));

        if (cardButton != null)
        {
            cardButton.onClick.RemoveAllListeners();
            // _friendManager is already PlayFabFriendManager — you need a reference to PlayFabManager too
            // Easiest: find it
            var pfManager = FindObjectOfType<PlayFabManager>();
            cardButton.onClick.AddListener(() => pfManager.OpenOtherPlayerProfile(_playFabId));
        }
    }

    // Called after add/remove to flip button state without respawning
    public void SetFriendStatus(bool isFriend)
    {
        addFriendButton.gameObject.SetActive(!isFriend);
        removeFriendButton.gameObject.SetActive(isFriend);
    }

  
}