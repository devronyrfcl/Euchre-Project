using System;
using System.Collections.Generic;

/// <summary>
/// Pure data model — no MonoBehaviour.
/// Serialized to JSON and saved to PlayerPrefs key "MatchConfig".
/// </summary>
[Serializable]
public class MatchConfig
{
    // ─── Match Setup ──────────────────────────────────────────────
    public string matchType   = "public";   // "public" | "private" | "solo" | "tournament"
    public string gameMode    = "standard"; // "standard" | "freeforall"
    public string roomCode      = "";
    public bool   isRoomCreator = false;  // true = create room, false = join by code
    public int    betAmount   = 100;
    public int    totalPot    = 400;
    public bool   allowBots   = true;
    public float  botFillDelay = 5f;

    // ─── Tournament ───────────────────────────────────────────────
    public string tournamentBoardId = "";  // set when matchType == "tournament"

    // ─── Local Player ─────────────────────────────────────────────
    public MatchPlayerData localPlayer = new MatchPlayerData();

    // ─── Invited Players (private match) ──────────────────────────
    public List<MatchPlayerData> invitedPlayers = new List<MatchPlayerData>();

    // ─── Bot Config ───────────────────────────────────────────────
    public BotMatchConfig botConfig = new BotMatchConfig();
}

[Serializable]
public class MatchPlayerData
{
    public string playFabId   = "";
    public string displayName = "";
    public string avatarUrl   = "";
    public int    pokerChips  = 0;
    public int    seat        = -1;   // -1 = unassigned
}

[Serializable]
public class BotMatchConfig
{
    public int minPC = 2000;
    public int maxPC = 8000;
}
