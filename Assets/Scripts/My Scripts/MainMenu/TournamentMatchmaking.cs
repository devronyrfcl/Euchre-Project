using System.Collections;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;

/// <summary>
/// Main Menu — matchmaking for a tournament match.
/// Connects to Photon (if needed), finds/creates a 4-player room scoped to the
/// player's tournament board, and once full, loads the gameplay scene while
/// staying connected/in-room so GameplayNetworkManager can pick up where this left off.
/// </summary>
public class TournamentMatchmaking : MonoBehaviourPunCallbacks
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private PlayFabManager playFabManager;
    [Tooltip("Status text shown under the Play button while matchmaking")]
    [SerializeField] private TMP_Text       matchmakingText;

    private const string ROOM_PROP_BOARD   = "TBoard";
    private const int    MAX_ROOM_PLAYERS  = 4;
    private const float  TIMEOUT_SECONDS   = 99f;

    private bool  _isMatchmaking;
    private int   _currentCount;
    private Coroutine _routine;
    private Coroutine _dotsRoutine;

    // ─────────────────────────────────────────────────────────────
    //  Public entry point — wire to the Play button
    // ─────────────────────────────────────────────────────────────

    public void Play()
    {
        if (_isMatchmaking) return;

        TournamentManager tm = TournamentManager.Instance;
        if (tm == null || !tm.IsJoined)
            return;

        if (!tm.CanPlay)
        {
            ShowStatus(tm.IsExpired ? "Time Expired" : "No Matches Left");
            return;
        }

        _isMatchmaking = true;
        _currentCount  = 0;
        _routine     = StartCoroutine(MatchmakingRoutine());
        _dotsRoutine = StartCoroutine(AnimateStatus());
    }

    // ─────────────────────────────────────────────────────────────
    //  Matchmaking flow
    // ─────────────────────────────────────────────────────────────

    private IEnumerator MatchmakingRoutine()
    {
        float elapsed = 0f;

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            PhotonNetwork.ConnectUsingSettings();

            while (!PhotonNetwork.IsConnectedAndReady && elapsed < TIMEOUT_SECONDS)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!PhotonNetwork.IsConnectedAndReady)
            {
                FailMatchmaking("Connection failed. Try again.");
                yield break;
            }
        }

        SetLocalPlayerProperties();

        ExitGames.Client.Photon.Hashtable expectedProps = new ExitGames.Client.Photon.Hashtable { { ROOM_PROP_BOARD, TournamentManager.Instance.BoardId } };
        PhotonNetwork.JoinRandomRoom(expectedProps, (byte)MAX_ROOM_PLAYERS);

        while (_isMatchmaking && elapsed < TIMEOUT_SECONDS)
        {
            elapsed += Time.deltaTime;

            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.PlayerCount >= MAX_ROOM_PLAYERS)
            {
                CompleteMatchmaking();
                yield break;
            }

            yield return null;
        }

        if (_isMatchmaking)
            FailMatchmaking("Other players busy. Try again.");
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        if (!_isMatchmaking) return;

        // No open room for this board yet — create one.
        string boardId = TournamentManager.Instance.BoardId;
        string roomName = "Tourn_" + boardId + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);

        RoomOptions opts = new RoomOptions
        {
            MaxPlayers = (byte)MAX_ROOM_PLAYERS,
            IsVisible  = true,
            IsOpen     = true,
            CustomRoomProperties         = new ExitGames.Client.Photon.Hashtable { { ROOM_PROP_BOARD, boardId } },
            CustomRoomPropertiesForLobby = new[] { ROOM_PROP_BOARD }
        };

        PhotonNetwork.CreateRoom(roomName, opts, TypedLobby.Default);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        if (!_isMatchmaking) return;
        FailMatchmaking("Couldn't create match. Try again.");
    }

    public override void OnJoinedRoom()
    {
        if (!_isMatchmaking) return;
        _currentCount = PhotonNetwork.CurrentRoom.PlayerCount;
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!_isMatchmaking) return;

        _currentCount = PhotonNetwork.CurrentRoom.PlayerCount;

        if (_currentCount >= MAX_ROOM_PLAYERS)
            CompleteMatchmaking();
    }

    // ─────────────────────────────────────────────────────────────
    //  Completion / failure
    // ─────────────────────────────────────────────────────────────

    private void CompleteMatchmaking()
    {
        _isMatchmaking = false;
        StopStatusRoutines();
        ShowStatus("Match Found!");

        MatchConfig config = new MatchConfig
        {
            matchType         = "tournament",
            gameMode          = "standard",
            roomCode          = "",
            isRoomCreator     = false,
            allowBots         = false,
            betAmount         = 0,
            totalPot          = 0,
            tournamentBoardId = TournamentManager.Instance.BoardId
        };

        if (playFabManager != null)
        {
            config.localPlayer = new MatchPlayerData
            {
                playFabId   = playFabManager.CachedPlayFabId,
                displayName = playFabManager.CachedDisplayName,
                avatarUrl   = playFabManager.CachedAvatarUrl,
                pokerChips  = playFabManager.CachedPokerChips,
                seat        = 0
            };
        }

        MatchConfigLoader.Save(config);

        if (playFabManager != null)
        {
            playFabManager.StopInvitePolling();
            playFabManager.ShowLoadingThenLoadScene("EuchreGame");
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("EuchreGame");
        }
    }

    private void FailMatchmaking(string reason)
    {
        _isMatchmaking = false;
        StopStatusRoutines();

        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.PlayerCount < MAX_ROOM_PLAYERS)
            PhotonNetwork.LeaveRoom();

        HideStatus();
        NotificationManager.Instance?.AddNotification("Matchmaking Failed", reason);
    }

    private void StopStatusRoutines()
    {
        if (_routine     != null) StopCoroutine(_routine);
        if (_dotsRoutine != null) StopCoroutine(_dotsRoutine);
    }

    // ─────────────────────────────────────────────────────────────
    //  Status text — "Matchmaking 1/4... (4 sec)"
    // ─────────────────────────────────────────────────────────────

    private IEnumerator AnimateStatus()
    {
        int   dotCount  = 0;
        int   seconds   = 0;
        float dotTimer  = 0f;
        float secTimer  = 0f;

        if (matchmakingText != null) matchmakingText.gameObject.SetActive(true);

        while (_isMatchmaking)
        {
            dotTimer += Time.deltaTime;
            secTimer += Time.deltaTime;

            if (dotTimer >= 0.4f)
            {
                dotTimer = 0f;
                dotCount = (dotCount + 1) % 4;
            }

            if (secTimer >= 1f)
            {
                secTimer = 0f;
                seconds = Mathf.Min(seconds + 1, 99);
            }

            string dots = new string('.', dotCount);
            ShowStatus($"Matchmaking {_currentCount}/{MAX_ROOM_PLAYERS}{dots} ({seconds} sec)");

            yield return null;
        }
    }

    private void ShowStatus(string text)
    {
        if (matchmakingText == null) return;
        matchmakingText.gameObject.SetActive(true);
        matchmakingText.text = text;
    }

    private void HideStatus()
    {
        if (matchmakingText == null) return;
        matchmakingText.gameObject.SetActive(false);
    }

    private void SetLocalPlayerProperties()
    {
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "PFId", playFabManager != null ? playFabManager.CachedPlayFabId : "" }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }
}
