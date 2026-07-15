using System;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Singleton — place on a persistent GameObject in the MainMenu scene.
/// Survives scene loads via DontDestroyOnLoad.
/// Resets tasks each calendar day. Progress is saved to PlayerPrefs.
/// </summary>
public class DailyTaskManager : MonoBehaviour
{
    public static DailyTaskManager Instance { get; private set; }

    private const string SAVE_KEY      = "DailyTaskSave";
    private const int    TASKS_PER_DAY = 3;

    private DailyTaskSaveData _saveData;

    /// <summary>Fired whenever task progress or claim state changes.</summary>
    public event Action OnTasksUpdated;

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadOrReset();
    }

    // ─────────────────────────────────────────────────────────────
    //  Load / Daily Reset
    // ─────────────────────────────────────────────────────────────

    private void LoadOrReset()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        string json  = PlayerPrefs.GetString(SAVE_KEY, "");

        if (!string.IsNullOrEmpty(json))
            _saveData = JsonUtility.FromJson<DailyTaskSaveData>(json);

        // New day or no data → fresh tasks
        if (_saveData == null || _saveData.savedDate != today)
            GenerateDailyTasks(today);
    }

    private void GenerateDailyTasks(string date)
    {
        _saveData = new DailyTaskSaveData { savedDate = date };

        // Shuffle pool
        List<DailyTaskDefinition> pool = new List<DailyTaskDefinition>(DailyTaskPool.All);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j   = UnityEngine.Random.Range(0, i + 1);
            var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
        }

        // Pick TASKS_PER_DAY with no duplicate types
        HashSet<TaskType> used = new HashSet<TaskType>();
        foreach (DailyTaskDefinition def in pool)
        {
            if (_saveData.tasks.Count >= TASKS_PER_DAY) break;
            if (used.Contains(def.type))               continue;

            _saveData.tasks.Add(DailyTaskState.FromDefinition(def));
            used.Add(def.type);
        }

        Save();
        Debug.Log($"[DailyTask] Generated {_saveData.tasks.Count} tasks for {date}");
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    public List<DailyTaskState> GetTasks() =>
        _saveData?.tasks ?? new List<DailyTaskState>();

    /// <summary>
    /// Call this from gameplay when the player does something trackable.
    /// Safe to call even if DailyTaskManager doesn't exist (null-conditional at call site).
    /// </summary>
    public void IncrementTask(TaskType type, int amount = 1)
    {
        if (_saveData == null) return;
        bool changed = false;

        foreach (DailyTaskState task in _saveData.tasks)
        {
            if (task.type != type)            continue;
            if (task.isClaimed)               continue;
            if (task.currentCount >= task.targetCount) continue;

            task.currentCount = Mathf.Min(task.currentCount + amount, task.targetCount);
            changed = true;

            Debug.Log($"[DailyTask] {task.title}: {task.currentCount}/{task.targetCount}");
        }

        if (!changed) return;
        Save();
        OnTasksUpdated?.Invoke();
    }

    /// <summary>Mark a completed task as claimed and grant its rewards.</summary>
    public void ClaimTask(string taskId)
    {
        if (_saveData == null) return;

        DailyTaskState task = _saveData.tasks.Find(t => t.id == taskId);
        if (task == null || !task.IsComplete || task.isClaimed) return;

        task.isClaimed = true;
        Save();
        OnTasksUpdated?.Invoke();

        GrantRewards(task);
        Debug.Log($"[DailyTask] Claimed: {task.title} — +{task.rewardGP} GP, +{task.rewardPC} PC");
    }

    // ─────────────────────────────────────────────────────────────
    //  Rewards
    // ─────────────────────────────────────────────────────────────

    private void GrantRewards(DailyTaskState task)
    {
        if (task.rewardPC > 0)
        {
            PlayFabClientAPI.AddUserVirtualCurrency(
                new AddUserVirtualCurrencyRequest { VirtualCurrency = "PC", Amount = task.rewardPC },
                r => Debug.Log($"[DailyTask] Granted {r.Balance} PC (added {task.rewardPC})"),
                e => Debug.LogWarning($"[DailyTask] PC grant failed: {e.GenerateErrorReport()}"));
        }

        if (task.rewardGP > 0)
        {
            PlayFabClientAPI.AddUserVirtualCurrency(
                new AddUserVirtualCurrencyRequest { VirtualCurrency = "GP", Amount = task.rewardGP },
                r => Debug.Log($"[DailyTask] Granted {r.Balance} GP (added {task.rewardGP})"),
                e => Debug.LogWarning($"[DailyTask] GP grant failed: {e.GenerateErrorReport()}"));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Save
    // ─────────────────────────────────────────────────────────────

    private void Save()
    {
        PlayerPrefs.SetString(SAVE_KEY, JsonUtility.ToJson(_saveData));
        PlayerPrefs.Save();
    }
}
