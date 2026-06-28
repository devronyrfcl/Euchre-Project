using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LeaderboardCard : MonoBehaviour
{
    [Header("=== TEXTS ===")]
    public TMP_Text rankCountText;
    public TMP_Text gameLevelCountText;
    public TMP_Text playerNameTitleText;
    public TMP_Text winPointsCountText;

    [Header("=== IMAGE ===")]
    public RawImage profileImage;

    [Header("=== BUTTON ===")]
    [SerializeField] private Button cardButton;

    [SerializeField] private Texture2D defaultProfileTexture;

    private string _playFabId;

    public void Setup(int rank, int level, string playerName, int trophy,
                      string avatarUrl = null, string playFabId = null,
                      PlayFabManager manager = null)
    {
        rankCountText.text = rank.ToString();
        gameLevelCountText.text = level.ToString();
        playerNameTitleText.text = playerName;
        winPointsCountText.text = trophy.ToString();

        _playFabId = playFabId;

        StartCoroutine(LoadProfileImage(avatarUrl));

        if (cardButton != null && manager != null && !string.IsNullOrEmpty(playFabId))
        {
            cardButton.onClick.RemoveAllListeners();
            cardButton.onClick.AddListener(() => manager.OpenOtherPlayerProfile(_playFabId));
        }
    }

    private IEnumerator LoadProfileImage(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            if (profileImage != null && defaultProfileTexture != null)
                profileImage.texture = defaultProfileTexture;
            yield break;
        }

        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
            profileImage.texture = DownloadHandlerTexture.GetContent(www);
        else if (defaultProfileTexture != null)
            profileImage.texture = defaultProfileTexture;
    }
}