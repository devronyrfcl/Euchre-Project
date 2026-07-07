using UnityEngine;

/// <summary>
/// Sits in the Main Menu scene on any persistent GameObject (e.g. PlayFabManager's GameObject).
/// Call BuildAndSave() from your Play / Join buttons.
/// Reads local player data directly from PlayFabManager.
/// </summary>
public class MatchConfigBuilder : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private PlayFabManager playFabManager;

    [SerializeField] private float BotFillDelayForPublicMatch = 5f;

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC ENTRY POINTS — hook these to your UI buttons
    // ─────────────────────────────────────────────────────────────

    /// <summary>Called by "Quick Match" / "Public Match" button.</summary>
    public void BuildPublicMatch()
    {
        MatchConfig config = BuildBase();
        config.matchType    = "public";
        config.roomCode     = "";
        config.allowBots    = true;
        config.botFillDelay = BotFillDelayForPublicMatch;

        MatchConfigLoader.Save(config);
        LoadGameplayScene();
    }

    /// <summary>Called by "Private Match" button. Pass in the room code from your input field.</summary>
    public void BuildPrivateMatch(string roomCode)
    {
        if (string.IsNullOrEmpty(roomCode))
        {
            Debug.LogWarning("[MatchConfigBuilder] Room code is empty.");
            return;
        }

        MatchConfig config = BuildBase();
        config.matchType    = "private";
        config.roomCode     = roomCode.Trim().ToUpper();
        config.allowBots    = true;   // fill empty seats with bots if friends don't join
        config.botFillDelay = 30f;    // longer wait for private rooms

        MatchConfigLoader.Save(config);
        LoadGameplayScene();
    }

    /// <summary>Called by "Solo / Practice" button.</summary>
    public void BuildSoloMatch()
    {
        MatchConfig config = BuildBase();
        config.matchType    = "solo";
        config.roomCode     = "";
        config.allowBots    = true;
        config.botFillDelay = 5f;     // fill immediately

        MatchConfigLoader.Save(config);
        LoadGameplayScene();
    }

    // ─────────────────────────────────────────────────────────────
    //  INTERNAL BUILD
    // ─────────────────────────────────────────────────────────────

    private MatchConfig BuildBase()
    {
        MatchConfig config = new MatchConfig
        {
            betAmount = 100,
            totalPot  = 400,
            botConfig = new BotMatchConfig { minPC = 2000, maxPC = 8000 }
        };

        // Pull data from PlayFabManager
        if (playFabManager != null)
        {
            config.localPlayer = new MatchPlayerData
            {
                playFabId   = playFabManager.CachedPlayFabId,
                displayName = playFabManager.CachedDisplayName,
                avatarUrl   = playFabManager.CachedAvatarUrl,
                pokerChips  = playFabManager.CachedPokerChips,
                seat        = 0   // local player is always seat 0
            };
        }
        else
        {
            Debug.LogWarning("[MatchConfigBuilder] PlayFabManager reference missing.");
            config.localPlayer = new MatchPlayerData
            {
                playFabId   = "UNKNOWN",
                displayName = "Player",
                avatarUrl   = "",
                pokerChips  = 5000,
                seat        = 0
            };
        }

        return config;
    }

    private void LoadGameplayScene()
    {
        // Stop invite polling before leaving
        if (playFabManager != null)
            playFabManager.StopInvitePolling();

        // Use PlayFabManager's loading transition instead of direct scene load
        if (playFabManager != null)
            playFabManager.ShowLoadingThenLoadScene("EuchreGame");
        else
            // Fallback if reference missing
            UnityEngine.SceneManagement.SceneManager.LoadScene("EuchreGame");
    }
}
