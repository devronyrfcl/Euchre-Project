using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "Tournament List" panel — shown when the local player hasn't joined a
/// tournament room yet. Spawns a TournamentRoomCard per open room and lets the
/// player join one, or open the Create Room panel to start a new one.
/// </summary>
public class TournamentListUI : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [Tooltip("The Content child of the ScrollRect")]
    [SerializeField] private RectTransform content;
    [SerializeField] private GameObject    roomCardPrefab;
    [SerializeField] private GameObject    createRoomPanel;
    [SerializeField] private Button        refreshButton;
    [SerializeField] private Button        createRoomButton;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        refreshButton?.onClick.AddListener(RefreshList);
        createRoomButton?.onClick.AddListener(OpenCreateRoomPanel);

        RefreshList();
    }

    private void OnDisable()
    {
        refreshButton?.onClick.RemoveListener(RefreshList);
        createRoomButton?.onClick.RemoveListener(OpenCreateRoomPanel);
    }

    // ─────────────────────────────────────────────────────────────
    //  Room list
    // ─────────────────────────────────────────────────────────────

    public void RefreshList()
    {
        TournamentManager.Instance?.GetRoomList((success, rooms) =>
        {
            if (!success || rooms == null) return;
            SpawnCards(rooms);
        });
    }

    private void SpawnCards(List<TournamentRoomSummary> rooms)
    {
        foreach (GameObject go in _spawned)
            if (go != null) Destroy(go);
        _spawned.Clear();

        if (content == null || roomCardPrefab == null) return;

        foreach (TournamentRoomSummary room in rooms)
        {
            GameObject obj = Instantiate(roomCardPrefab, content);
            TournamentRoomCard card = obj.GetComponent<TournamentRoomCard>();
            card?.Setup(room, OnJoinClicked);
            _spawned.Add(obj);
        }
    }

    private void OnJoinClicked(string boardId)
    {
        TournamentManager.Instance?.JoinRoom(boardId, (success, error) =>
        {
            if (!success)
                NotificationManager.Instance?.AddNotification("Couldn't Join Room", error ?? "Please try again.");

            // On success, TournamentPanelUI listens for OnStatusUpdated and
            // switches from the room list to the joined-room dashboard itself.
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  Create room panel
    // ─────────────────────────────────────────────────────────────

    public void OpenCreateRoomPanel()
    {
        createRoomPanel?.SetActive(true);
    }
}
