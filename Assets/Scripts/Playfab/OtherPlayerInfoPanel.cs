using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class OtherPlayerInfoPanel : MonoBehaviour
{
    [Header("=== PANEL ===")]
    [SerializeField] private GameObject panel;

    [Header("=== TEXTS ===")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text emailText;
    [SerializeField] private TMP_Text userIdText;
    [SerializeField] private TMP_Text gameLevelText;
    [SerializeField] private TMP_Text gameXPText;
    [SerializeField] private TMP_Text totalGamesPlayedText;
    [SerializeField] private TMP_Text totalGamesWonText;
    [SerializeField] private TMP_Text totalTournPlayedText;
    [SerializeField] private TMP_Text totalTournWonText;
    [SerializeField] private Texture2D defaultProfileTexture;

    [Header("=== IMAGE ===")]
    [SerializeField] private RawImage profileImage;

    public void Show(OtherPlayerData data)
    {
        panel.SetActive(true);

        nameText.text = data.displayName ?? "--";
        emailText.text = data.email ?? "--";
        userIdText.text = data.playFabId ?? "--";
        gameLevelText.text = data.stats != null ? data.stats.playerLevel.ToString() : "--";
        gameXPText.text = data.stats != null ? data.stats.playerXP.ToString() : "--";
        totalGamesPlayedText.text = data.stats != null ? data.stats.totalGamesPlayed.ToString() : "--";
        totalGamesWonText.text = data.stats != null ? data.stats.totalGamesWon.ToString() : "--";
        totalTournPlayedText.text = data.stats != null ? data.stats.totalTournPlayed.ToString() : "--";
        totalTournWonText.text = data.stats != null ? data.stats.totalTournWon.ToString() : "--";

        StartCoroutine(LoadImage(data.avatarUrl));
    }

    public void Close()
    {
        panel.SetActive(false);
    }

    private IEnumerator LoadImage(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            if (defaultProfileTexture != null)
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

// ── Data container ────────────────────────────────────────────
[System.Serializable]
public class OtherPlayerData
{
    public string displayName;
    public string avatarUrl;
    public string playFabId;
    public string email;
    public OtherPlayerStats stats;
}

[System.Serializable]
public class OtherPlayerStats
{
    public int playerLevel;
    public int playerXP;
    public int totalGamesPlayed;
    public int totalGamesWon;
    public int totalTournPlayed;
    public int totalTournWon;
}

// ── CloudScript response wrapper ──────────────────────────────
[System.Serializable]
public class CloudScriptGetOtherPlayerResult
{
    public string displayName;
    public string avatarUrl;
    public string playFabId;
    public string email;
    public int playerLevel;
    public int playerXP;
    public int totalGamesPlayed;
    public int totalGamesWon;
    public int totalTournPlayed;
    public int totalTournWon;
}