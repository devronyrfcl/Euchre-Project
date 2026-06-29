using UnityEngine;

/// <summary>
/// First script to run in the gameplay scene.
/// Reads MatchConfig from PlayerPrefs and hands data to
/// GameplayPlayFabManager and GameplayNetworkManager.
/// Execution order should be set to -100 in Script Execution Order settings.
/// </summary>
public class GameplaySceneBootstrapper : MonoBehaviour
{
    [Header("=== SCENE REFERENCES ===")]
    [SerializeField] private GameplayPlayFabManager playFabManager;
    [SerializeField] private GameplayNetworkManager networkManager;

    private MatchConfig _config;

    private void Awake()
    {
        if (!MatchConfigLoader.Exists())
        {
            Debug.LogWarning("[Bootstrapper] No MatchConfig found — using defaults.");
            _config = new MatchConfig();
            _config.localPlayer = new MatchPlayerData
            {
                playFabId = "DEBUG",
                displayName = "Debug Player",
                pokerChips = 5000,
                seat = 0
            };
        }
        else
        {
            _config = MatchConfigLoader.Load();
        }

        Boot();
    }

    private void Boot()
    {
        Debug.Log($"[Bootstrapper] Booting — matchType: {_config.matchType} " +
                  $"| room: '{_config.roomCode}' | bot: {_config.allowBots}");

        // GameplayPlayFabManager reads config itself via MatchConfigLoader.
        // NetworkManager needs the config to know room type and room code.
        if (networkManager != null)
            networkManager.Init(_config);
        else
            Debug.LogError("[Bootstrapper] GameplayNetworkManager reference missing.");
    }
}