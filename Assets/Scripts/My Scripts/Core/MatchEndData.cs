/// <summary>
/// Plain data container passed from GameplayNetworkManager
/// to GameOverUI at match end.
/// </summary>
[System.Serializable]
public class MatchEndData
{
    public bool   localWon;
    public int    winnerTeam;
    public int    team1Score;
    public int    team2Score;
    public int    team1Tricks;
    public int    team2Tricks;
    public int    pot;
    public int    betAmount;
    public int    trophyChange;    // positive = gained, negative = lost
    public string lastHandSummary;
    public bool   isTeamMode;
    public int    localTeam;       // 1 or 2
}