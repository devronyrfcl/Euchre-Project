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
    public long   joinedAt    = 0;   // ms since epoch (UTC)
    public long   deadline    = 0;   // ms since epoch (UTC) — 48h after joinedAt
}

[Serializable]
public class TournamentBoardData
{
    public string boardId   = "";
    public int    prizePool = 0;
    public long   createdAt = 0;
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
