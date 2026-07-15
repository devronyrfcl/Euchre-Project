using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  Task type — maps to in-game actions tracked in GameplayNetworkManager
// ─────────────────────────────────────────────────────────────────────────────

public enum TaskType
{
    PlayMatches,
    WinMatches,
    PlayTournament,
    PlayCustomMatch,
    PlaySoloMatch,
    WinTricks,
    GoAlone,
    MarchWin,
    OrderTrump
}

// ─────────────────────────────────────────────────────────────────────────────
//  Static template (not saved — used to generate tasks)
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public class DailyTaskDefinition
{
    public string   id;
    public TaskType type;
    public string   title;
    public int      targetCount;
    public int      rewardGP;
    public int      rewardPC;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Live state — serialized to PlayerPrefs
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public class DailyTaskState
{
    public string   id;
    public TaskType type;
    public string   title;
    public int      targetCount;
    public int      rewardGP;
    public int      rewardPC;
    public int      currentCount;
    public bool     isClaimed;

    public bool IsComplete => currentCount >= targetCount;

    public static DailyTaskState FromDefinition(DailyTaskDefinition def)
    {
        return new DailyTaskState
        {
            id           = def.id,
            type         = def.type,
            title        = def.title,
            targetCount  = def.targetCount,
            rewardGP     = def.rewardGP,
            rewardPC     = def.rewardPC,
            currentCount = 0,
            isClaimed    = false
        };
    }
}

[Serializable]
public class DailyTaskSaveData
{
    public string               savedDate = "";
    public List<DailyTaskState> tasks     = new List<DailyTaskState>();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Task pool — all possible daily tasks
// ─────────────────────────────────────────────────────────────────────────────

public static class DailyTaskPool
{
    public static readonly List<DailyTaskDefinition> All = new List<DailyTaskDefinition>
    {
        // Play matches
        new DailyTaskDefinition { id="play_1",       type=TaskType.PlayMatches,    title="Play 1 Match",         targetCount=1,  rewardGP=30,  rewardPC=100 },
        new DailyTaskDefinition { id="play_3",       type=TaskType.PlayMatches,    title="Play 3 Matches",       targetCount=3,  rewardGP=80,  rewardPC=250 },

        // Win matches
        new DailyTaskDefinition { id="win_1",        type=TaskType.WinMatches,     title="Win 1 Match",          targetCount=1,  rewardGP=60,  rewardPC=300 },
        new DailyTaskDefinition { id="win_2",        type=TaskType.WinMatches,     title="Win 2 Matches",        targetCount=2,  rewardGP=120, rewardPC=600 },

        // Tricks
        new DailyTaskDefinition { id="tricks_5",     type=TaskType.WinTricks,      title="Win 5 Tricks",         targetCount=5,  rewardGP=25,  rewardPC=100 },
        new DailyTaskDefinition { id="tricks_10",    type=TaskType.WinTricks,      title="Win 10 Tricks",        targetCount=10, rewardGP=60,  rewardPC=250 },

        // Tournament
        new DailyTaskDefinition { id="tourn_1",      type=TaskType.PlayTournament, title="Play a Tournament",    targetCount=1,  rewardGP=50,  rewardPC=200 },
        new DailyTaskDefinition { id="tourn_2",      type=TaskType.PlayTournament, title="Play 2 Tournaments",   targetCount=2,  rewardGP=100, rewardPC=400 },

        // Custom
        new DailyTaskDefinition { id="custom_1",     type=TaskType.PlayCustomMatch,title="Play a Custom Match",  targetCount=1,  rewardGP=40,  rewardPC=150 },

        // Solo
        new DailyTaskDefinition { id="solo_1",       type=TaskType.PlaySoloMatch,  title="Play a Solo Match",    targetCount=1,  rewardGP=30,  rewardPC=100 },

        // Special plays
        new DailyTaskDefinition { id="go_alone_1",   type=TaskType.GoAlone,        title="Go Alone Once",        targetCount=1,  rewardGP=80,  rewardPC=350 },
        new DailyTaskDefinition { id="march_1",      type=TaskType.MarchWin,       title="Win a March (5 Tricks)",targetCount=1, rewardGP=100, rewardPC=500 },
        new DailyTaskDefinition { id="order_trump_3",type=TaskType.OrderTrump,     title="Order Trump 3 Times",  targetCount=3,  rewardGP=35,  rewardPC=120 },
    };
}
