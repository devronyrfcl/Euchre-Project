using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Sits on both the "me" and "other player" leaderboard card prefabs.
/// Same component, different prefab visuals — spawner picks the prefab.
/// </summary>
public class TournamentLeaderboardCard : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private Image    avatarImage;
    [SerializeField] private Sprite   defaultAvatar;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text scoreText;

    public void Setup(int rank, TournamentPlayerEntry entry)
    {
        if (entry == null) return;

        if (rankText  != null) rankText.text  = $"#{rank}";
        if (nameText  != null) nameText.text  = string.IsNullOrEmpty(entry.displayName) ? "Player" : entry.displayName;
        if (scoreText != null) scoreText.text = entry.score.ToString("N0");

        StopAllCoroutines();
        StartCoroutine(FetchAvatar(entry.avatarUrl));
    }

    private IEnumerator FetchAvatar(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            ApplyDefaultAvatar();
            yield break;
        }

        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Texture2D tex = DownloadHandlerTexture.GetContent(www);
            Sprite sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));

            if (avatarImage != null)
                avatarImage.sprite = sprite;
        }
        else
        {
            ApplyDefaultAvatar();
        }
    }

    private void ApplyDefaultAvatar()
    {
        if (avatarImage != null && defaultAvatar != null)
            avatarImage.sprite = defaultAvatar;
    }
}
