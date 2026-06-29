using UnityEngine;

/// <summary>
/// Static helper — reads and writes MatchConfig JSON from PlayerPrefs.
/// Usable from any scene, no MonoBehaviour needed.
/// </summary>
public static class MatchConfigLoader
{
    private const string PREFS_KEY = "MatchConfig";

    // ─── Save ─────────────────────────────────────────────────────

    public static void Save(MatchConfig config)
    {
        if (config == null)
        {
            Debug.LogError("[MatchConfigLoader] Cannot save null config.");
            return;
        }

        string json = JsonUtility.ToJson(config, prettyPrint: false);
        PlayerPrefs.SetString(PREFS_KEY, json);
        PlayerPrefs.Save();
        Debug.Log("[MatchConfigLoader] Config saved:\n" + json);
    }

    // ─── Load ─────────────────────────────────────────────────────

    public static MatchConfig Load()
    {
        if (!PlayerPrefs.HasKey(PREFS_KEY))
        {
            Debug.LogWarning("[MatchConfigLoader] No config found — returning default.");
            return new MatchConfig();
        }

        string json = PlayerPrefs.GetString(PREFS_KEY);

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[MatchConfigLoader] Config key is empty — returning default.");
            return new MatchConfig();
        }

        MatchConfig config = JsonUtility.FromJson<MatchConfig>(json);

        if (config == null)
        {
            Debug.LogError("[MatchConfigLoader] Failed to deserialize config — returning default.");
            return new MatchConfig();
        }

        Debug.Log("[MatchConfigLoader] Config loaded:\n" + json);
        return config;
    }

    // ─── Clear ────────────────────────────────────────────────────

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(PREFS_KEY);
        PlayerPrefs.Save();
    }

    // ─── Exists ───────────────────────────────────────────────────

    public static bool Exists() => PlayerPrefs.HasKey(PREFS_KEY);
}
