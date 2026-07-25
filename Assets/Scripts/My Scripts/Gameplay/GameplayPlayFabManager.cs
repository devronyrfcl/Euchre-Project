using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Handles all PlayFab responsibilities in the gameplay scene:
///   - Fetch local player data from MatchConfig
///   - Fetch other players' profiles via CloudScript (Legacy)
///   - Sync dealer icon and trump UI (called by GameplayNetworkManager)
///   - Deduct bet on match start, distribute winnings on match end
///   - Save match stats to PlayerData
/// </summary>
public class GameplayPlayFabManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Singleton
    // ─────────────────────────────────────────────────────────────

    public static GameplayPlayFabManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== PLAYER UI PANELS ===")]
    [Tooltip("Index 0 = local, 1 = partner, 2 = left opponent, 3 = right opponent")]
    [SerializeField] private GameplayPlayerData[] playerPanels = new GameplayPlayerData[4];

    // ─────────────────────────────────────────────────────────────
    //  Runtime State
    // ─────────────────────────────────────────────────────────────

    private MatchConfig _config;
    private int         _currentDealerSeat  = -1;
    private int         _trumpPickerSeat     = -1;
    private char        _currentTrump        = ' ';

    // PlayFab stat keys — match your existing PlayFabManager constants
    private const string KEY_PLAYER_STATS = "PlayerStats";

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        _config = MatchConfigLoader.Load();

        if (_config == null)
        {
            Debug.LogError("[GameplayPlayFabManager] No match config found.");
            return;
        }

        SetupLocalPlayer();
    }

    // ─────────────────────────────────────────────────────────────
    //  LOCAL PLAYER SETUP
    // ─────────────────────────────────────────────────────────────

    private void SetupLocalPlayer()
    {
        MatchPlayerData local = _config?.localPlayer;

        if (local == null)
        {
            Debug.LogError("[GameplayPlayFabManager] No local player data in config.");
            return;
        }

        if (playerPanels[0] == null)
        {
            Debug.LogError("[GameplayPlayFabManager] playerPanels[0] not assigned.");
            return;
        }

        // Set name and PC from config immediately so panel isn't blank
        playerPanels[0].Setup(
            local.playFabId,
            local.displayName,
            local.pokerChips,
            seat: 0,
            isBot: false);

        // Then fetch fresh name + avatar directly from PlayFab
        // This is the only reliable source — config URL may be stale or empty
        FetchAndApplyLocalProfile();
    }

    private void FetchAndApplyLocalProfile()
    {
        PlayFabClientAPI.GetPlayerProfile(
            new GetPlayerProfileRequest
            {
                ProfileConstraints = new PlayerProfileViewConstraints
                {
                    ShowDisplayName = true,
                    ShowAvatarUrl   = true
                }
            },
            result =>
            {
                if (playerPanels[0] == null) return;

                string displayName = result.PlayerProfile?.DisplayName;
                string avatarUrl   = result.PlayerProfile?.AvatarUrl;

                string finalName = !string.IsNullOrEmpty(displayName)
                    ? displayName
                    : _config.localPlayer.displayName;

                string urlToLoad = !string.IsNullOrEmpty(avatarUrl)
                    ? avatarUrl
                    : _config.localPlayer.avatarUrl;

                // Update game panel (deferred to OnEnable if inactive)
                playerPanels[0].Setup(
                    _config.localPlayer.playFabId,
                    finalName,
                    _config.localPlayer.pokerChips,
                    seat: 0,
                    isBot: false);
                playerPanels[0].LoadAvatar(urlToLoad);

                // Push fresh data to loading panel immediately — it is active
                GameplayNetworkManager.Instance?.UpdateLocalPlayerLoadingSlot(
                    finalName, urlToLoad, _config.localPlayer.pokerChips);
            },
            error =>
            {
                Debug.LogWarning("[GameplayPlayFabManager] Profile fetch failed: "
                            + error.GenerateErrorReport());

                string fallbackUrl = _config.localPlayer.avatarUrl;

                if (playerPanels[0] != null)
                    playerPanels[0].LoadAvatar(fallbackUrl);

                // Still push config data to loading panel as fallback
                GameplayNetworkManager.Instance?.UpdateLocalPlayerLoadingSlot(
                    _config.localPlayer.displayName, fallbackUrl, _config.localPlayer.pokerChips);
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  OTHER PLAYER SETUP
    //  Called by GameplayNetworkManager once Photon room is ready
    //  and all player custom properties are available.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameplayNetworkManager for each non-local player.
    /// seat: 1 = partner, 2 = left opp, 3 = right opp.
    /// </summary>
    public void SetupOtherPlayer(int seat, string playFabId,
                                  string displayName, int pokerChips,
                                  string avatarUrl, bool isBot = false)
    {
        if (seat < 1 || seat > 3)
        {
            Debug.LogWarning($"[GameplayPlayFabManager] Invalid seat: {seat}");
            return;
        }

        if (playerPanels[seat] == null)
        {
            Debug.LogWarning($"[GameplayPlayFabManager] No panel assigned for seat {seat}");
            return;
        }

        if (isBot)
        {
            // Bot — no PlayFab fetch needed
            playerPanels[seat].Setup(playFabId, displayName, pokerChips, seat, isBot: true);
            return;
        }

        // Real player — fetch via CloudScript then set up panel
        playerPanels[seat].Setup(playFabId, displayName, pokerChips, seat, isBot: false);
        FetchOtherPlayerProfile(seat, playFabId);
    }

    // ─────────────────────────────────────────────────────────────
    //  CLOUD SCRIPT — Legacy
    //  GetGameplayPlayerProfile(playFabId) → displayName, avatarUrl
    // ─────────────────────────────────────────────────────────────

    private void FetchOtherPlayerProfile(int seat, string playFabId)
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName        = "GetGameplayPlayerProfile",
                FunctionParameter   = new { playFabId = playFabId },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                if (result.FunctionResult == null)
                {
                    Debug.LogWarning($"[GameplayPlayFabManager] CloudScript returned null for seat {seat}");
                    return;
                }

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(result.FunctionResult);
                CloudProfileResult profile =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<CloudProfileResult>(json);

                if (profile == null) return;

                if (playerPanels[seat] != null)
                {
                    // Update name if CloudScript returned a better one
                    if (!string.IsNullOrEmpty(profile.displayName))
                        playerPanels[seat].Setup(
                            playFabId,
                            profile.displayName,
                            playerPanels[seat].PokerChips,
                            seat,
                            isBot: false);

                    // Load avatar
                    if (!string.IsNullOrEmpty(profile.avatarUrl))
                        playerPanels[seat].LoadAvatar(profile.avatarUrl);
                }
            },
            error =>
            {
                Debug.LogError($"[GameplayPlayFabManager] CloudScript error for seat {seat}: " +
                               error.GenerateErrorReport());
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  DEALER SYNC
    //  Called by GameplayNetworkManager when dealer changes.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pass the seat index (0-3) of the new dealer.
    /// Updates all 4 panels accordingly.
    /// </summary>
    public void SetDealer(int dealerSeat)
    {
        _currentDealerSeat = dealerSeat;

        for (int i = 0; i < playerPanels.Length; i++)
        {
            if (playerPanels[i] != null)
                playerPanels[i].SetDealer(i == dealerSeat);
        }

        Debug.Log($"[GameplayPlayFabManager] Dealer set to seat {dealerSeat}");
    }

    // ─────────────────────────────────────────────────────────────
    //  TRUMP SYNC
    //  Called by GameplayNetworkManager when trump is picked.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pass the seat index of the player who picked trump, and the suit char.
    /// Updates all 4 panels — only the picker's frame enables.
    /// </summary>
    public void SetTrump(int pickerSeat, char suit)
    {
        _trumpPickerSeat = pickerSeat;
        _currentTrump    = suit;

        for (int i = 0; i < playerPanels.Length; i++)
        {
            if (playerPanels[i] != null)
                playerPanels[i].SetTrump(suit, i == pickerSeat);
        }

        Debug.Log($"[GameplayPlayFabManager] Trump set: {suit} by seat {pickerSeat}");
    }

    /// <summary>Clears trump display on all panels (between hands).</summary>
    public void ClearTrump()
    {
        _trumpPickerSeat = -1;
        _currentTrump    = ' ';

        for (int i = 0; i < playerPanels.Length; i++)
        {
            if (playerPanels[i] != null)
                playerPanels[i].ClearTrump();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  TEAM / GO ALONE BADGES
    // ─────────────────────────────────────────────────────────────

    public void SetTeamMode(bool active)
    {
        if (!active)
        {
            for (int i = 0; i < playerPanels.Length; i++)
                if (playerPanels[i] != null)
                    playerPanels[i].SetTeamBadge(false, "");
            return;
        }

        // Seat 0 & 1 = one team, seat 2 & 3 = other team
        if (playerPanels[0] != null) playerPanels[0].SetTeamBadge(true, "Team A");
        if (playerPanels[1] != null) playerPanels[1].SetTeamBadge(true, "Team A");
        if (playerPanels[2] != null) playerPanels[2].SetTeamBadge(true, "Team B");
        if (playerPanels[3] != null) playerPanels[3].SetTeamBadge(true, "Team B");
    }

    public void SetGoAlone(int goAloneSeat)
    {
        for (int i = 0; i < playerPanels.Length; i++)
        {
            if (playerPanels[i] != null)
                playerPanels[i].SetGoAlone(i == goAloneSeat);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ECONOMY — BET DEDUCTION ON MATCH START
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Deducts bet amount from local player's PC on PlayFab.
    /// Called once when all 4 players are seated and match starts.
    /// </summary>
    public void DeductBetOnMatchStart()
    {
        int betAmount = _config?.betAmount ?? 100;

        PlayFabClientAPI.SubtractUserVirtualCurrency(
            new SubtractUserVirtualCurrencyRequest
            {
                VirtualCurrency = "PC",
                Amount          = betAmount
            },
            result =>
            {
                Debug.Log($"[GameplayPlayFabManager] Bet deducted: {betAmount} PC. " +
                          $"New balance: {result.Balance}");

                // Update local panel
                if (playerPanels[0] != null)
                    playerPanels[0].SetPokerChips(result.Balance);
            },
            error =>
            {
                Debug.LogError("[GameplayPlayFabManager] Bet deduction failed: " +
                               error.GenerateErrorReport());
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  ECONOMY — DISTRIBUTE WINNINGS ON MATCH END
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameplayNetworkManager when local player wins the match.
    /// Adds totalPot to local player's PC on PlayFab.
    /// Only called on the winning client.
    /// </summary>
    public void DistributeWinnings()
    {
        int pot = _config?.totalPot ?? 400;

        PlayFabClientAPI.AddUserVirtualCurrency(
            new AddUserVirtualCurrencyRequest
            {
                VirtualCurrency = "PC",
                Amount          = pot
            },
            result =>
            {
                Debug.Log($"[GameplayPlayFabManager] Winnings added: {pot} PC. " +
                          $"New balance: {result.Balance}");

                if (playerPanels[0] != null)
                    playerPanels[0].SetPokerChips(result.Balance);
            },
            error =>
            {
                Debug.LogError("[GameplayPlayFabManager] Winnings distribution failed: " +
                               error.GenerateErrorReport());
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  MATCH STATS SAVE
    //  Called by GameplayNetworkManager at match end.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves match result to PlayFab PlayerData.
    /// Reads existing stats, increments, then saves back.
    /// </summary>
    public void SaveMatchStats(bool won, int tricksWon, int totalTricks, bool isTournament = false)
    {
        // Step 1: Fetch existing stats
        PlayFabClientAPI.GetUserData(
            new GetUserDataRequest { Keys = new List<string> { KEY_PLAYER_STATS } },
            result =>
            {
                PlayerStatsData stats = new PlayerStatsData();

                if (result.Data != null && result.Data.ContainsKey(KEY_PLAYER_STATS))
                {
                    string json = result.Data[KEY_PLAYER_STATS].Value;
                    if (!string.IsNullOrEmpty(json))
                        stats = JsonUtility.FromJson<PlayerStatsData>(json) ?? new PlayerStatsData();
                }

                // Increment stats
                stats.totalGamesPlayed++;
                if (won)
                {
                    stats.totalGamesWon++;
                    stats.playerWin++;
                    stats.totalWinCoins += _config?.totalPot ?? 400;
                }

                // XP reward — tournament matches count for more than a normal match
                int xpGain = isTournament
                    ? (won ? 50 : 15)
                    : (won ? 20 : 5);
                stats.playerXP = Mathf.Max(0, stats.playerXP + xpGain);

                // Recalculate level
                stats.playerLevel = RecalculateLevel(stats.playerXP);

                // Step 2: Save back
                string updatedJson = JsonUtility.ToJson(stats);

                PlayFabClientAPI.UpdateUserData(
                    new UpdateUserDataRequest
                    {
                        Data = new Dictionary<string, string>
                        {
                            { KEY_PLAYER_STATS, updatedJson }
                        }
                    },
                    saveResult =>
                    {
                        Debug.Log("[GameplayPlayFabManager] Match stats saved.");
                    },
                    saveError =>
                    {
                        Debug.LogError("[GameplayPlayFabManager] Save stats error: " +
                                       saveError.GenerateErrorReport());
                    });
            },
            fetchError =>
            {
                Debug.LogError("[GameplayPlayFabManager] Fetch stats error: " +
                               fetchError.GenerateErrorReport());
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  SELECTING TRUMP INDICATOR
    // ─────────────────────────────────────────────────────────────

    public void SetSelectingTrump(int panelIdx)
    {
        for (int i = 0; i < playerPanels.Length; i++)
            playerPanels[i]?.SetSelectingTrump(i == panelIdx);
    }

    public void ClearSelectingTrump()
    {
        for (int i = 0; i < playerPanels.Length; i++)
            playerPanels[i]?.SetSelectingTrump(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  TRICK SCORE PER PLAYER
    // ─────────────────────────────────────────────────────────────

    public void SetPlayerTrickScore(int panelIdx, int tricks)
    {
        if (panelIdx < 0 || panelIdx >= playerPanels.Length) return;
        playerPanels[panelIdx]?.SetTrickScore(tricks);
    }

    public void ResetAllTrickScores()
    {
        for (int i = 0; i < playerPanels.Length; i++)
            playerPanels[i]?.ResetTrickScore();
    }

    // ─────────────────────────────────────────────────────────────
    //  ACCESSORS
    // ─────────────────────────────────────────────────────────────

    public GameplayPlayerData GetPlayerPanel(int seat)
    {
        if (seat < 0 || seat >= playerPanels.Length) return null;
        return playerPanels[seat];
    }

    public MatchConfig Config => _config;

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    // Mirrors LevelThresholds from your PlayFabManager
    private static readonly int[] LevelThresholds =
        { 0, 10, 25, 55, 100, 160, 240, 340, 460, 600 };

    private int RecalculateLevel(int xp)
    {
        int level = 1;
        for (int i = LevelThresholds.Length - 1; i >= 0; i--)
        {
            if (xp >= LevelThresholds[i]) { level = i + 1; break; }
        }
        return level;
    }

    // ─────────────────────────────────────────────────────────────
    //  INTERNAL DATA CLASSES
    // ─────────────────────────────────────────────────────────────

    [System.Serializable]
    private class CloudProfileResult
    {
        public string displayName;
        public string avatarUrl;
    }
}
