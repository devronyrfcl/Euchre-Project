using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the ScrollRect's parent panel.
/// Spawns DailyTaskCard prefabs into the ScrollRect Content transform.
/// Subscribe to DailyTaskManager.OnTasksUpdated to refresh cards live.
/// </summary>
public class DailyTaskUI : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [Tooltip("The Content child of the ScrollRect")]
    [SerializeField] private RectTransform content;
    [Tooltip("Prefab with DailyTaskCard component")]
    [SerializeField] private GameObject    cardPrefab;

    private const float CARD_HEIGHT = 47.7727f;

    private readonly List<DailyTaskCard> _cards = new List<DailyTaskCard>();

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (DailyTaskManager.Instance != null)
            DailyTaskManager.Instance.OnTasksUpdated += RefreshAll;

        SpawnCards();
    }

    private void OnDisable()
    {
        if (DailyTaskManager.Instance != null)
            DailyTaskManager.Instance.OnTasksUpdated -= RefreshAll;
    }

    // ─────────────────────────────────────────────────────────────
    //  Spawn
    // ─────────────────────────────────────────────────────────────

    private void SpawnCards()
    {
        // Clear any previously spawned cards and reset content height
        foreach (Transform child in content)
            Destroy(child.gameObject);
        _cards.Clear();

        if (content != null)
            content.sizeDelta = new Vector2(content.sizeDelta.x, 0f);

        if (DailyTaskManager.Instance == null || cardPrefab == null || content == null)
            return;

        List<DailyTaskState> tasks = DailyTaskManager.Instance.GetTasks();

        foreach (DailyTaskState task in tasks)
        {
            GameObject    obj  = Instantiate(cardPrefab, content);
            DailyTaskCard card = obj.GetComponent<DailyTaskCard>();

            if (card == null) continue;

            // Grow content height for each card
            content.sizeDelta = new Vector2(content.sizeDelta.x,
                                            content.sizeDelta.y + CARD_HEIGHT);

            DailyTaskState captured = task;
            card.Setup(captured, id => DailyTaskManager.Instance?.ClaimTask(id));
            _cards.Add(card);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Refresh (called on OnTasksUpdated event)
    // ─────────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        List<DailyTaskState> tasks = DailyTaskManager.Instance?.GetTasks();
        if (tasks == null) return;

        for (int i = 0; i < _cards.Count && i < tasks.Count; i++)
            _cards[i].Refresh(tasks[i]);
    }
}
