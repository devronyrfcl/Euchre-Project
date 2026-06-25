using TMPro;
using UnityEngine;

public class LeaderboardCard : MonoBehaviour
{
    public TMP_Text rankCountText;
    public TMP_Text gameLevelCountText;
    public TMP_Text playerNameTitleText;
    public TMP_Text winPointsCountText;

    public void Setup(
        int rank,
        int level,
        string playerName,
        int trophy)
    {
        rankCountText.text = rank.ToString();
        gameLevelCountText.text = level.ToString();
        playerNameTitleText.text = playerName;
        winPointsCountText.text = trophy.ToString();
    }
}