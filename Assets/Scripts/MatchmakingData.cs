using UnityEngine;

public class MatchmakingData : MonoBehaviour
{
    public static MatchmakingData Instance;

    public string PlayerName;
    public bool IsPrivateMatch;
    public string RoomCode;
    public int TeamIndex;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}