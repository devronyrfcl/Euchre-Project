using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Sits on the Tournament Room Card prefab spawned in the Tournament List.</summary>
public class TournamentRoomCard : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private Button   joinButton;

    private string          _boardId;
    private Action<string>  _onJoin;

    public void Setup(TournamentRoomSummary room, Action<string> onJoin)
    {
        if (room == null) return;

        _boardId = room.boardId;
        _onJoin  = onJoin;

        if (roomNameText    != null) roomNameText.text    = room.roomName;
        if (playerCountText != null) playerCountText.text = $"{room.playerCount} / {TournamentManager.MAX_PLAYERS}";

        joinButton?.onClick.RemoveAllListeners();
        joinButton?.onClick.AddListener(() => _onJoin?.Invoke(_boardId));
    }
}
