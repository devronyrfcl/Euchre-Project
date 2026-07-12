# Euchre Project — Claude Context

## Project Overview
Unity multiplayer Euchre card game. Networking via **Photon PUN2**, backend via **PlayFab** (Legacy CloudScript). Target platform: mobile (Android/iOS). Uses DOTween for all animations and TextMeshPro for UI text.

## Key Scripts (all under `Assets/Scripts/My Scripts/`)

| File | Role |
|------|------|
| `Gameplay/GameplayNetworkManager.cs` | Core game controller — Photon callbacks, RPC orchestration, all game state |
| `Gameplay/GameplayPlayFabManager.cs` | PlayFab calls, player panel references (4× GameplayPlayerData) |
| `Gameplay/GameplayPlayerData.cs` | UI component for one player slot (name, avatar, PC coins, dealer icon, trump, trick score, timer) |
| `Gameplay/EuchreCardManager.cs` | Card visuals — deal animation, hand display, card selection, table play animation |
| `Gameplay/EuchreCardUI.cs` | Sits on each playable card prefab; tap handler, selection state, raise animation |
| `Gameplay/TrickScoreDisplay.cs` | Team-level trick/score HUD |
| `Gameplay/TurnIndicatorController.cs` | Arrow/indicator showing whose turn it is |
| `Gameplay/TurnTimerController.cs` | Countdown timer per turn |
| `Core/MatchConfig.cs` | Pure data model, JSON-serialized to PlayerPrefs key `"MatchConfig"` |
| `Core/MatchConfigLoader.cs` | Loads/saves MatchConfig |
| `Core/MatchConfigBuilder.cs` | Main-menu-side builder for MatchConfig |
| `UI/TeamVoteUI.cs` | Team vote panel |
| `UI/TrumpSelectionUI.cs` | Trump selection panel (phase 1 & 2) |
| `UI/GameOverUI.cs` | Match end screen |

## Architecture Patterns

### Singleton pattern
`GameplayNetworkManager`, `GameplayPlayFabManager`, `EuchreCardManager` all use:
```csharp
public static T Instance { get; private set; }
// Awake: if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this;
```

### Seat / Panel mapping
- **Absolute seat** (0–3): fixed in Photon room. Seat 0 = local player, 1 = partner, 2 = left opponent, 3 = right opponent.
- **Panel index** = relative to local player (always same mapping in this project).
- `RemapSeatToPanel(int absoluteSeat)` in `GameplayNetworkManager` converts absolute → relative panel index.

### Loading panel slot ownership
- **Slot 0** (local player) is exclusively owned by `Init()` and `UpdateLocalPlayerLoadingSlot()` (called from PlayFab callback). Never write to slot 0 from `RPC_SeatConfirmed` or `LoadExistingPlayersInRoom`.
- Slots 1–3 are set via `RemapSeatToPanel(seat)` from Photon events.

### Inactive GameObject / coroutine guard
`GameplayPlayerData.LoadAvatar` defers avatar fetching when the object is inactive:
- Stores URL in `_pendingAvatarUrl` if `!gameObject.activeInHierarchy`
- `OnEnable()` picks it up and starts the coroutine

### Trick score tracking
- **Per-match cumulative** in `_seatTricks` dict + `GameplayPlayerData.SetTrickScore` — resets only in `RPC_ResetMatchTotals` (called from `StartNewMatch`).
- **Per-hand team tricks** (`_team1Tricks`, `_team2Tricks`) — reset each hand in `RPC_StartHand`.

## Audio — `AudioManager.Instance.PlaySFX(string)`
| Clip name | When fired |
|-----------|-----------|
| `"Intro_1"` | Team vote result (game starts) — `RPC_TeamVoteResult` |
| `"TrumpSelected"` | Trump picked by anyone — `RPC_TrumpOrdered` & `RPC_TrumpSuitChosen` |
| `"MyTrun"` | Local player's turn begins — `RPC_BeginTurn` |
| `"GiveCard"` | Each card dealt — `EuchreCardManager.SpawnShuffleCard` |
| `"PlayCard"` | Any player plays a card — `RPC_CardPlayed` |

## Card System (EuchreCardManager)

### Sprite map
24 sprites, order: for each type `{9,T,J,Q,K,A}` × suit `{C,D,H,S}` → key e.g. `"9C"`, `"TD"`.

### Deal animation
- `DealCoroutine`: delay between cards = **0.22f** seconds (intentionally slow for realism)
- `SpawnShuffleCard` animation duration = **0.38f**

### Card play (double-tap to throw)
`OnCardTapped`: first tap selects/raises the card; second tap on the same card calls `ConfirmPlaySelectedCard()`.

### Table card positions & rotations
```csharp
TABLE_CARD_POSITIONS[]  // Vector2, canvas anchored, per panel index (0=me,1=partner,2=left,3=right)
TABLE_CARD_ROTATIONS_Z[] // float Z rotation per panel index — adjust values per visual preference
// Applied as: Quaternion.Euler(35f, 0f, TABLE_CARD_ROTATIONS_Z[panelIndex])
```
Current Z values: `{ 0f, 0f, 90f, -90f }` — left/right cards rotated to lay flat.

### Deal throw rotations
```csharp
DEAL_THROW_ROTATIONS_Z[] = { 0f, 0f, -90f, 90f }  // for dealing animation to each player
```

## First Loading Panel (Intro Lobby)
Sequence on `Init()`:
1. `allPlayerUICards` disabled, `firstLoadingPanel` enabled
2. Slot 0 filled with local player data (from MatchConfig, refreshed by PlayFab callback)
3. Slots 1–3 show "Joining..." / "0 PC" until players arrive
4. On `RPC_StartTeamVote` → `IntroThenVote()` coroutine:
   - Bots fill → 1-second delay → animation starts
   - Player cards slide out (player1+3 left, player2+4 right), VS goes up, panel fades to 0 alpha (was 170/255)
   - `allPlayerUICards` enabled, `firstLoadingPanel` disabled

## GameState Enum
```
Connecting → WaitingForPlayers → TeamVote → Dealing → TrumpPhase1 → TrumpPhase2 → Playing → ScoringTrick → HandOver → MatchOver
```

## Photon Custom Property Keys
| Key | Meaning |
|-----|---------|
| `"PFId"` | PlayFab ID |
| `"Name"` | Display name |
| `"Avt"` | Avatar URL |
| `"PC"` | Poker chips |
| `"Seat"` | Absolute seat (0–3) |
| `"Bot"` | Is bot player |

## MatchConfig
Loaded from `PlayerPrefs` key `"MatchConfig"` via `MatchConfigLoader.Load()`. Key fields:
- `matchType`: `"public"` | `"private"` | `"solo"`
- `betAmount`, `totalPot`
- `botFillDelay` (default 5f — delay before bots fill empty slots)
- `localPlayer`: `MatchPlayerData` with `playFabId`, `displayName`, `avatarUrl`, `pokerChips`

## Win Condition
`WIN_SCORE = 10` points. Teams score 1–4 points per hand depending on tricks won vs. euchre.

## Dependencies
- **Photon PUN2** (`Photon.Pun`, `Photon.Realtime`, `ExitGames.Client.Photon`)
- **PlayFab SDK** (`PlayFab`, `PlayFab.ClientModels`)
- **DOTween Pro** (`DG.Tweening`)
- **TextMeshPro** (`TMPro`)
- **Lean GUI** (`Lean.Gui`) — used on card buttons
- **UnityWebRequest / UnityWebRequestTexture** — avatar image loading
