using System;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Singleton — persists across scenes (main menu ↔ gameplay).
/// Talks to the Tournament CloudScript handlers (JoinTournament, GetMyTournamentStatus,
/// GetTournamentBoard, SubmitTournamentScore) and caches the current board/entry locally.
/// </summary>
public class TournamentManager : MonoBehaviour
{
    public static TournamentManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Constants (mirror CloudScript-side values)
    // ─────────────────────────────────────────────────────────────

    public const int ENTRY_FEE   = 5000;
    public const int MAX_PLAYERS = 100;
    public const int MATCH_LIMIT = 10;

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
    //  Status / board refresh
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
    //  Join
    // ─────────────────────────────────────────────────────────────

    public void JoinTournament(Action<bool, string> onDone)
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "JoinTournament",
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                JoinTournamentResult joinResult = Deserialize<JoinTournamentResult>(result.FunctionResult);
                if (joinResult == null || !joinResult.success)
                {
                    onDone?.Invoke(false, joinResult?.error ?? "Failed to join tournament.");
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
                Debug.LogWarning($"[TournamentManager] JoinTournament failed: {error.GenerateErrorReport()}");
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
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    public int MatchesRemaining =>
        MyEntry != null ? Mathf.Max(0, MATCH_LIMIT - MyEntry.matchesPlayed) : MATCH_LIMIT;

    public TimeSpan TimeRemaining
    {
        get
        {
            if (MyEntry == null) return TimeSpan.Zero;
            long remainingMs = MyEntry.deadline - NowMs();
            return remainingMs > 0 ? TimeSpan.FromMilliseconds(remainingMs) : TimeSpan.Zero;
        }
    }

    public bool IsExpired => IsJoined && MyEntry != null && NowMs() >= MyEntry.deadline;

    public bool CanPlay => IsJoined && !IsExpired && MatchesRemaining > 0;

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static T Deserialize<T>(object functionResult) where T : class
    {
        if (functionResult == null) return null;
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(functionResult);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
    }
}
