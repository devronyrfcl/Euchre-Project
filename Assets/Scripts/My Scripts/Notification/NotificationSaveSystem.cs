using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

/// <summary>
/// Handles saving and loading notification history as XML.
/// Editor   → Assets/NotificationHistory.xml
/// Android  → Application.persistentDataPath
/// iOS      → Application.persistentDataPath
/// </summary>
public static class NotificationSaveSystem
{
    private const string FILE_NAME = "NotificationHistory.xml";

    private static string FilePath
    {
        get
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, FILE_NAME);
#else
            return Path.Combine(Application.persistentDataPath, FILE_NAME);
#endif
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Save
    // ─────────────────────────────────────────────────────────────

    public static void Save(NotificationHistory history)
    {
        try
        {
            XmlSerializer serializer = new XmlSerializer(typeof(NotificationHistory));
            using (StreamWriter writer = new StreamWriter(FilePath))
            {
                serializer.Serialize(writer, history);
            }
            Debug.Log($"[NotificationSave] Saved {history.entries.Count} " +
                      $"notifications to {FilePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[NotificationSave] Save failed: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Load
    // ─────────────────────────────────────────────────────────────

    public static NotificationHistory Load()
    {
        if (!File.Exists(FilePath))
        {
            Debug.Log("[NotificationSave] No history file found — starting fresh.");
            return new NotificationHistory();
        }

        try
        {
            XmlSerializer serializer = new XmlSerializer(typeof(NotificationHistory));
            using (StreamReader reader = new StreamReader(FilePath))
            {
                NotificationHistory history =
                    (NotificationHistory)serializer.Deserialize(reader);
                Debug.Log($"[NotificationSave] Loaded {history.entries.Count} notifications.");
                return history;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[NotificationSave] Load failed: " + e.Message);
            return new NotificationHistory();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Clear all
    // ─────────────────────────────────────────────────────────────

    public static void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);

        Debug.Log("[NotificationSave] History cleared.");
    }
}