using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the tournament leaderboard ScrollRect's parent panel.
/// Spawns one card per player on the current tournament board — "me" card for the
/// local player, "other" card for everyone else. Shows all players, even 0 score.
/// </summary>
public class TournamentLeaderboardUI : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [Tooltip("The Content child of the ScrollRect")]
    [SerializeField] private RectTransform content;
    [SerializeField] private GameObject    meCardPrefab;
    [SerializeField] private GameObject    otherCardPrefab;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (TournamentManager.Instance != null)
        {
            TournamentManager.Instance.OnStatusUpdated += SpawnCards;
            TournamentManager.Instance.RefreshBoard(_ => SpawnCards());
        }

        SpawnCards();
    }

    private void OnDisable()
    {
        if (TournamentManager.Instance != null)
            TournamentManager.Instance.OnStatusUpdated -= SpawnCards;
    }

    // ─────────────────────────────────────────────────────────────
    //  Spawn
    // ─────────────────────────────────────────────────────────────

    private void SpawnCards()
    {
        foreach (GameObject go in _spawned)
            if (go != null) Destroy(go);
        _spawned.Clear();

        TournamentManager tm = TournamentManager.Instance;
        if (tm == null || tm.Board?.players == null || content == null) return;

        List<TournamentPlayerEntry> sorted = new List<TournamentPlayerEntry>(tm.Board.players);
        sorted.Sort((a, b) => b.score.CompareTo(a.score));

        string myPlayFabId = tm.MyEntry?.playFabId;

        for (int i = 0; i < sorted.Count; i++)
        {
            TournamentPlayerEntry entry = sorted[i];
            bool isMe = !string.IsNullOrEmpty(myPlayFabId) && entry.playFabId == myPlayFabId;

            GameObject prefab = isMe ? meCardPrefab : otherCardPrefab;
            if (prefab == null) continue;

            GameObject obj = Instantiate(prefab, content);
            TournamentLeaderboardCard card = obj.GetComponent<TournamentLeaderboardCard>();
            card?.Setup(i + 1, entry);

            _spawned.Add(obj);
        }
    }
}
