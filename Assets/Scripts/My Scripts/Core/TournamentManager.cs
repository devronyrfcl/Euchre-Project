using System;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Singleton — persists across scenes (main menu ↔ gameplay).
/// Talks to the Tournament CloudScript handlers and caches the current room/entry locally.
///
/// Flow: browse rooms → join a room (free, reserves a seat) → pay the entry fee
/// (triggered the first time Play is pressed) → play matches → leave is only
/// possible before paying.
/// </summary>
public class TournamentManager : MonoBehaviour
{
    public static TournamentManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Constants (mirror CloudScript-side values)
    // ─────────────────────────────────────────────────────────────

    public const int   ENTRY_FEE     = 5000;
    public const int   MAX_PLAYERS   = 100;
    public const int   MATCH_LIMIT   = 10;
    public const double WINDOW_HOURS = 48.0;

    // ─────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────

    public bool                 IsJoined { get; private set; }
    public string               BoardId  { get; private set; } = "";
    public TournamentBoardData  Board    { get; private set; }
    public TournamentPlayerEntry MyEntry { get; private set; }

    /// <summary>Fired whenever Board / MyEntry / IsJoined changes.</summary>
    public event Action OnStatusUpdated;

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─────────────────────────────────────────────────────────────
    //  Status refresh
    // ─────────────────────────────────────────────────────────────

    public void RefreshMyStatus(Action<bool> onDone = null)
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "GetMyTournamentStatus",
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                MyTournamentStatusResult status = Deserialize<MyTournamentStatusResult>(result.FunctionResult);
                if (status == null || !status.success)
                {
                    onDone?.Invoke(false);
                    return;
                }

                IsJoined = status.joined;
                BoardId  = status.boardId  ?? "";
                Board    = status.board;
                MyEntry  = status.myEntry;

                OnStatusUpdated?.Invoke();
                onDone?.Invoke(true);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] RefreshMyStatus failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(false);
            });
    }

    public void RefreshBoard(Action<bool> onDone = null)
    {
        if (string.IsNullOrEmpty(BoardId))
        {
            onDone?.Invoke(false);
            return;
        }

        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "GetTournamentBoard",
                FunctionParameter = new { boardId = BoardId },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                GetTournamentBoardResult boardResult = Deserialize<GetTournamentBoardResult>(result.FunctionResult);
                if (boardResult == null || !boardResult.success)
                {
                    onDone?.Invoke(false);
                    return;
                }

                Board = boardResult.board;
                OnStatusUpdated?.Invoke();
                onDone?.Invoke(true);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] RefreshBoard failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(false);
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Browse — Tournament List
    // ─────────────────────────────────────────────────────────────

    public void GetRoomList(Action<bool, List<TournamentRoomSummary>> onDone)
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "GetTournamentRoomList",
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                TournamentRoomListResult listResult = Deserialize<TournamentRoomListResult>(result.FunctionResult);
                if (listResult == null || !listResult.success)
                {
                    onDone?.Invoke(false, null);
                    return;
                }

                onDone?.Invoke(true, listResult.rooms);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] GetRoomList failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(false, null);
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Create a named room
    // ─────────────────────────────────────────────────────────────

    public void CreateRoom(string roomName, Action<bool, string> onDone)
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "CreateTournamentRoom",
                FunctionParameter = new { roomName = roomName },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                CreateTournamentRoomResult createResult = Deserialize<CreateTournamentRoomResult>(result.FunctionResult);
                if (createResult == null || !createResult.success)
                {
                    onDone?.Invoke(false, createResult?.error ?? "Failed to create room.");
                    return;
                }

                IsJoined = true;
                BoardId  = createResult.boardId;
                Board    = createResult.board;
                MyEntry  = createResult.myEntry;

                OnStatusUpdated?.Invoke();
                onDone?.Invoke(true, null);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] CreateRoom failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(false, error.GenerateErrorReport());
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Join a specific room (free — payment happens separately)
    // ─────────────────────────────────────────────────────────────

    public void JoinRoom(string boardId, Action<bool, string> onDone)
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "JoinTournamentRoom",
                FunctionParameter = new { boardId = boardId },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                JoinTournamentResult joinResult = Deserialize<JoinTournamentResult>(result.FunctionResult);
                if (joinResult == null || !joinResult.success)
                {
                    onDone?.Invoke(false, joinResult?.error ?? "Failed to join room.");
                    return;
                }

                IsJoined = true;
                BoardId  = joinResult.boardId;
                Board    = joinResult.board;
                MyEntry  = joinResult.myEntry;

                OnStatusUpdated?.Invoke();
                onDone?.Invoke(true, null);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] JoinRoom failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(false, error.GenerateErrorReport());
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Pay entry fee — call the first time Play is pressed
    // ─────────────────────────────────────────────────────────────

    public void PayEntryFee(Action<bool, string> onDone)
    {
        if (string.IsNullOrEmpty(BoardId))
        {
            onDone?.Invoke(false, "Not in a tournament room.");
            return;
        }

        if (HasPaid)
        {
            onDone?.Invoke(true, null);
            return;
        }

        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "PayTournamentEntry",
                FunctionParameter = new { boardId = BoardId },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                PayTournamentEntryResult payResult = Deserialize<PayTournamentEntryResult>(result.FunctionResult);
                if (payResult == null || !payResult.success)
                {
                    onDone?.Invoke(false, payResult?.error ?? "Payment failed.");
                    return;
                }

                Board   = payResult.board;
                MyEntry = payResult.myEntry;

                OnStatusUpdated?.Invoke();
                onDone?.Invoke(true, null);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] PayEntryFee failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(false, error.GenerateErrorReport());
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Leave — only allowed before paying
    // ─────────────────────────────────────────────────────────────

    public void LeaveRoom(Action<bool, string> onDone)
    {
        if (string.IsNullOrEmpty(BoardId))
        {
            onDone?.Invoke(false, "Not in a tournament room.");
            return;
        }

        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "LeaveTournamentRoom",
                FunctionParameter = new { boardId = BoardId },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                LeaveTournamentRoomResult leaveResult = Deserialize<LeaveTournamentRoomResult>(result.FunctionResult);
                if (leaveResult == null || !leaveResult.success)
                {
                    onDone?.Invoke(false, leaveResult?.error ?? "Failed to leave room.");
                    return;
                }

                IsJoined = false;
                BoardId  = "";
                Board    = null;
                MyEntry  = null;

                OnStatusUpdated?.Invoke();
                onDone?.Invoke(true, null);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] LeaveRoom failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(false, error.GenerateErrorReport());
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Score submission — called from GoToMainMenuFromTournament()
    // ─────────────────────────────────────────────────────────────

    public void SubmitMatchScore(int scoreDelta, Action<SubmitTournamentScoreResult> onDone)
    {
        if (string.IsNullOrEmpty(BoardId))
        {
            onDone?.Invoke(null);
            return;
        }

        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "SubmitTournamentScore",
                FunctionParameter = new { boardId = BoardId, scoreDelta = scoreDelta },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                SubmitTournamentScoreResult scoreResult = Deserialize<SubmitTournamentScoreResult>(result.FunctionResult);

                if (scoreResult != null && scoreResult.success && MyEntry != null)
                {
                    MyEntry.score         = scoreResult.newScore;
                    MyEntry.matchesPlayed = scoreResult.matchesPlayed;
                    if (Board != null) Board.prizePool = scoreResult.prizePool;
                    OnStatusUpdated?.Invoke();
                }

                onDone?.Invoke(scoreResult);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] SubmitMatchScore failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(null);
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Pending reward — a room expired (48h) while the player was away and
    //  the backend already paid them out automatically. Check on login /
    //  main menu load and surface it as a notification once, then clear it.
    // ─────────────────────────────────────────────────────────────

    public void CheckPendingReward(Action<bool, PendingTournamentReward> onDone)
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "CheckTournamentReward",
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                CheckTournamentRewardResult rewardResult = Deserialize<CheckTournamentRewardResult>(result.FunctionResult);
                if (rewardResult == null || !rewardResult.hasReward)
                {
                    onDone?.Invoke(false, null);
                    return;
                }

                onDone?.Invoke(true, rewardResult.reward);
            },
            error =>
            {
                Debug.LogWarning($"[TournamentManager] CheckPendingReward failed: {error.GenerateErrorReport()}");
                onDone?.Invoke(false, null);
            });
    }

    public void ClearPendingReward()
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "ClearTournamentReward",
                GeneratePlayStreamEvent = false
            },
            result => { },
            error => Debug.LogWarning($"[TournamentManager] ClearPendingReward failed: {error.GenerateErrorReport()}"));
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    public bool HasPaid => MyEntry != null && MyEntry.hasPaid;

    public int MatchesRemaining =>
        MyEntry != null ? Mathf.Max(0, MATCH_LIMIT - MyEntry.matchesPlayed) : MATCH_LIMIT;

    /// <summary>Time remaining in the shared room timer (starts when the first player joined).</summary>
    public TimeSpan TimeRemaining
    {
        get
        {
            if (Board == null || Board.timerStartedAt == 0) return TimeSpan.Zero;
            long deadlineMs   = Board.timerStartedAt + (long)(WINDOW_HOURS * 3600000);
            long remainingMs  = deadlineMs - NowMs();
            return remainingMs > 0 ? TimeSpan.FromMilliseconds(remainingMs) : TimeSpan.Zero;
        }
    }

    public bool IsExpired => IsJoined && Board != null && Board.timerStartedAt != 0 && TimeRemaining <= TimeSpan.Zero;

    public bool CanPlay => IsJoined && !IsExpired && MatchesRemaining > 0;

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static T Deserialize<T>(object functionResult) where T : class
    {
        if (functionResult == null) return null;
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(functionResult);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
    }
}
