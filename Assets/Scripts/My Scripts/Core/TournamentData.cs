using System;
using System.Collections.Generic;

/// <summary>
/// Pure data models for the tournament system.
/// Mirrors the JSON shape returned by the Tournament CloudScript handlers.
/// </summary>
[Serializable]
public class TournamentPlayerEntry
{
    public string playFabId   = "";
    public string displayName = "";
    public string avatarUrl   = "";
    public int    score       = 0;
    public int    matchesPlayed = 0;
    public bool   hasPaid     = false;
    public long   joinedAt    = 0;   // ms since epoch (UTC)
}

[Serializable]
public class TournamentBoardData
{
    public string boardId        = "";
    public string roomName       = "";
    public int    prizePool      = 0;
    public long   createdAt      = 0;
    public long   timerStartedAt = 0;   // ms since epoch (UTC) — set when the first player joined
    public List<TournamentPlayerEntry> players = new List<TournamentPlayerEntry>();
}

/// <summary>One line item on the tournament game-over score breakdown.</summary>
[Serializable]
public struct ScoreBreakdownEntry
{
    public string title;
    public int    value;

    public ScoreBreakdownEntry(string title, int value)
    {
        this.title = title;
        this.value = value;
    }
}

/// <summary>Lightweight room summary shown in the Tournament List browser.</summary>
[Serializable]
public class TournamentRoomSummary
{
    public string boardId;
    public string roomName;
    public int    playerCount;
}

// ─────────────────────────────────────────────────────────────
//  CloudScript response wrappers (deserialize targets)
// ─────────────────────────────────────────────────────────────

[Serializable]
public class JoinTournamentResult
{
    public bool   success;
    public bool   alreadyJoined;
    public string error;
    public string boardId;
    public TournamentBoardData board;
    public TournamentPlayerEntry myEntry;
}

[Serializable]
public class GetTournamentBoardResult
{
    public bool   success;
    public string error;
    public TournamentBoardData board;
}

[Serializable]
public class MyTournamentStatusResult
{
    public bool   success;
    public bool   joined;
    public string boardId;
    public TournamentBoardData board;
    public TournamentPlayerEntry myEntry;
}

[Serializable]
public class SubmitTournamentScoreResult
{
    public bool   success;
    public string error;
    public int    newScore;
    public int    newRank;
    public int    matchesPlayed;
    public int    prizePool;
}

[Serializable]
public class TournamentRoomListResult
{
    public bool success;
    public string error;
    public List<TournamentRoomSummary> rooms = new List<TournamentRoomSummary>();
}

[Serializable]
public class CreateTournamentRoomResult
{
    public bool   success;
    public string error;
    public string boardId;
    public TournamentBoardData board;
    public TournamentPlayerEntry myEntry;
}

[Serializable]
public class PayTournamentEntryResult
{
    public bool   success;
    public bool   alreadyPaid;
    public string error;
    public TournamentBoardData board;
    public TournamentPlayerEntry myEntry;
}

[Serializable]
public class LeaveTournamentRoomResult
{
    public bool   success;
    public string error;
}

/// <summary>Payload of a pending tournament payout the player hasn't been notified of yet.</summary>
[Serializable]
public class PendingTournamentReward
{
    public string roomName;
    public int    rank;
    public int    reward;
    public int    prizePool;
}

[Serializable]
public class CheckTournamentRewardResult
{
    public bool                  hasReward;
    public PendingTournamentReward reward;
}
