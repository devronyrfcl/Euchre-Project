using System;
using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Full Photon PUN2 network manager for Euchre gameplay.
/// Handles: connection, room join/create (public random or private by code),
///          seat assignment (modular, first-come-first-served with slot system ready),
///          dealing, trump phases 1 & 2, go-alone, trick taking,
///          hand scoring, match scoring, bot fill, economy callbacks.
///
/// Seat layout (absolute):
///   0 = local player always
///   1 = partner  (opposite in team mode; seat+2 % 4)
///   2 = left opponent
///   3 = right opponent
///
/// Panel index = relative to local player (same mapping).
/// </summary>
public class GameplayNetworkManager : MonoBehaviourPunCallbacks
{
    // ─────────────────────────────────────────────────────────────
    //  Singleton
    // ─────────────────────────────────────────────────────────────

    public static GameplayNetworkManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== MANAGERS ===")]
    [SerializeField] private GameplayPlayFabManager playFabManager;
    [SerializeField] private EuchreCardManager      cardManager;
    [SerializeField] private TrumpSelectionUI       trumpUI;
    [SerializeField] private TeamVoteUI             teamVoteUI;
    [SerializeField] private GameOverUI             gameOverUI;
    [SerializeField] private NotificationManager      notificationManager;

    [Header("=== STATUS ===")]
    [SerializeField] private TMPro.TMP_Text statusText;

    [Header("=== TURN INDICATOR ===")]
    [SerializeField] private TurnIndicatorController turnIndicator;

    // Add field at top
    [Header("=== TIMER ===")]
    [SerializeField] private TurnTimerController turnTimer;

    [Header("=== SCORE DISPLAY ===")]
    [SerializeField] private TrickScoreDisplay trickDisplay;
    // ─────────────────────────────────────────────────────────────
    //  Photon Property Keys
    // ─────────────────────────────────────────────────────────────

    // Player custom properties
    private const string PROP_PLAYFAB_ID   = "PFId";
    private const string PROP_DISPLAY_NAME = "Name";
    private const string PROP_AVATAR_URL   = "Avt";
    private const string PROP_POKER_CHIPS  = "PC";
    private const string PROP_SEAT         = "Seat";
    private const string PROP_IS_BOT       = "Bot";

    // ─────────────────────────────────────────────────────────────
    //  Game State
    // ─────────────────────────────────────────────────────────────

    public enum GameState
    {
        Connecting, WaitingForPlayers, TeamVote,
        Dealing, TrumpPhase1, TrumpPhase2,
        Playing, ScoringTrick, HandOver, MatchOver
    }

    private string SeatName(int seat)
    {
        return _seatToName.ContainsKey(seat) ? _seatToName[seat] : $"Player {seat + 1}";
    }

    // ─────────────────────────────────────────────────────────────
    //  Runtime State
    // ─────────────────────────────────────────────────────────────

    private MatchConfig _config;
    private GameState   _state          = GameState.Connecting;

    // Seats
    private int         _localSeat      = 0;
    private List<int>   _takenSeats     = new List<int>();
    private Dictionary<int, int>    _seatToActor  = new Dictionary<int, int>();
    private Dictionary<int, string> _seatToName   = new Dictionary<int, string>();

    // Hand / trick state
    private int         _dealerSeat     = 0;
    private int         _turnSeat       = 0;
    private char        _trump          = ' ';
    private int         _trumpPickerSeat = -1;
    private int         _cycled         = 0;
    private int         _skipSeat       = -1;

    // Team vote
    private bool        _isTeamMode     = false;
    private int         _teamVoteYes    = 0;
    private int         _teamVoteNo     = 0;
    private bool        _localVoteDone  = false;

    // Scoring
    private int         _team1Score     = 0;
    private int         _team2Score     = 0;
    private int         _team1Tricks    = 0;
    private int         _team2Tricks    = 0;
    private const int   WIN_SCORE       = 10;

    // Cards
    private Dictionary<int, List<string>> _hands   = new Dictionary<int, List<string>>();
    private List<string>                  _played  = new List<string>();
    private List<string>                  _deck    = new List<string>();

    // Trick turn order (built at start of each trick)
    private List<int>   _trickOrder     = new List<int>();

    // Bots
    private List<int>   _botSeats       = new List<int>();
    private Coroutine   _botFillRoutine;

    private static readonly string[] BOT_NAMES = { "Jusef", "Opu", "Monir" };

    private static readonly string[] CARD_TYPES = { "9","T","J","Q","K","A" };
    private static readonly string[] CARD_SUITS = { "C","D","H","S" };

    private static readonly Dictionary<char,char> ALT_SUITS = new Dictionary<char,char>
        { {'S','C'},{'C','S'},{'H','D'},{'D','H'} };

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Called by GameplaySceneBootstrapper on Awake.</summary>
    public void Init(MatchConfig config)
    {
        _config = config;
        SetStatus("Connecting...");
        PhotonNetwork.ConnectUsingSettings();
    }

    // ─────────────────────────────────────────────────────────────
    //  PHOTON CONNECTION
    // ─────────────────────────────────────────────────────────────

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Network] Connected to Master.");

        // Push local player identity into Photon custom properties
        // so all clients can read them from Player.CustomProperties
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { PROP_PLAYFAB_ID,   _config.localPlayer.playFabId   },
            { PROP_DISPLAY_NAME, _config.localPlayer.displayName  },
            { PROP_AVATAR_URL,   _config.localPlayer.avatarUrl    },
            { PROP_POKER_CHIPS,  _config.localPlayer.pokerChips   },
            { PROP_IS_BOT,       false                            }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);

        if (_config.matchType == "private" && !string.IsNullOrEmpty(_config.roomCode))
            JoinOrCreatePrivateRoom();
        else
            JoinPublicRoom();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[Network] Disconnected: {cause}");

        SetStatus("Connection lost. Reconnecting...");
        StartCoroutine(ReconnectDelay());
    }

    private IEnumerator ReconnectDelay()
    {
        yield return new WaitForSeconds(2f);
        if (_state != GameState.MatchOver)
            PhotonNetwork.ConnectUsingSettings();
    }

    // ─────────────────────────────────────────────────────────────
    //  ROOM JOIN / CREATE
    // ─────────────────────────────────────────────────────────────

    private void JoinPublicRoom()
    {
        PhotonNetwork.JoinRandomRoom();
    }

    private void JoinOrCreatePrivateRoom()
    {
        RoomOptions opts = BuildRoomOptions(visible: false);
        PhotonNetwork.JoinOrCreateRoom(_config.roomCode, opts, TypedLobby.Default);
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        // No public room found — create one with random ID
        string id = GenerateRoomId();
        RoomOptions opts = BuildRoomOptions(visible: true);
        PhotonNetwork.JoinOrCreateRoom(id, opts, TypedLobby.Default);
    }

    private RoomOptions BuildRoomOptions(bool visible)
    {
        return new RoomOptions
        {
            IsVisible  = visible,
            MaxPlayers = 4
        };
    }

    private string GenerateRoomId()
    {
        string id = "";
        for (int i = 0; i < 8; i++) id += UnityEngine.Random.Range(0, 9).ToString();
        return id;
    }

    // ─────────────────────────────────────────────────────────────
    //  ROOM EVENTS
    // ─────────────────────────────────────────────────────────────

    public override void OnCreatedRoom()
    {
        Debug.Log("[Network] Room created — I am host.");
        _takenSeats.Clear();

        // Host always takes seat 0
        AssignLocalSeat(0);
        _takenSeats.Add(0);
        _seatToActor[0] = PhotonNetwork.LocalPlayer.ActorNumber;

        SetStatus("Waiting for players (1/4)...");
        SetState(GameState.WaitingForPlayers);

        if (_config.allowBots)
            _botFillRoutine = StartCoroutine(BotFillCountdown());
    }

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.IsMasterClient) return; // handled in OnCreatedRoom

        Debug.Log("[Network] Joined room — requesting seat.");
        SetStatus("Waiting for players...");
        SetState(GameState.WaitingForPlayers);

        photonView.RPC(nameof(RPC_RequestSeat), RpcTarget.MasterClient,
            PhotonNetwork.LocalPlayer.ActorNumber,
            _config.localPlayer.playFabId,
            _config.localPlayer.displayName,
            _config.localPlayer.avatarUrl,
            _config.localPlayer.pokerChips);

        // ── FIX: load existing players already in the room ──────────
        StartCoroutine(LoadExistingPlayersInRoom());
    }

    private IEnumerator LoadExistingPlayersInRoom()
    {
        // Wait for seat assignment RPC to come back first
        yield return new WaitForSeconds(0.5f);

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            // Skip self
            if (player.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber) continue;

            object seatObj, pfIdObj, nameObj, pcObj, avatarObj, botObj;

            if (!player.CustomProperties.TryGetValue(PROP_SEAT, out seatObj)) continue;
            if (!player.CustomProperties.TryGetValue(PROP_PLAYFAB_ID, out pfIdObj)) continue;
            if (!player.CustomProperties.TryGetValue(PROP_DISPLAY_NAME, out nameObj)) continue;
            if (!player.CustomProperties.TryGetValue(PROP_POKER_CHIPS, out pcObj)) continue;

            player.CustomProperties.TryGetValue(PROP_AVATAR_URL, out avatarObj);
            player.CustomProperties.TryGetValue(PROP_IS_BOT, out botObj);

            int seat = (int)seatObj;
            string playFabId = pfIdObj?.ToString() ?? "";
            string displayName = nameObj?.ToString() ?? "Player";
            int pokerChips = pcObj is int pc ? pc : 0;
            string avatarUrl = avatarObj?.ToString() ?? "";
            bool isBot = botObj is bool b && b;

            int panelIdx = RemapSeatToPanel(seat);

            // Show immediately
            playFabManager?.SetupOtherPlayer(
                panelIdx, playFabId, displayName, pokerChips, avatarUrl, isBot);

            // Then fetch fresh avatar and name from PlayFab
            if (!isBot)
                RefreshOtherPlayerPanel(panelIdx, playFabId, displayName, pokerChips);

            Debug.Log($"[Network] Loaded existing player: {displayName} at seat {seat}");
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.LogWarning($"[Network] Player left: {otherPlayer.NickName}");

        notificationManager.ShowWarningMessagePulse($"{otherPlayer.NickName} has left the game.");

        if (_state == GameState.MatchOver) return;
        if (!PhotonNetwork.IsMasterClient) return;

        if (_config.allowBots)
            ReplaceWithBot(otherPlayer);
    }

    // ─────────────────────────────────────────────────────────────
    //  SEAT ASSIGNMENT
    //  Modular: first-come-first-served now.
    //  To add slot picking later, replace the seat-selection logic
    //  in RPC_RequestSeat with a slot availability check.
    // ─────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_RequestSeat(int actorNumber, string playFabId,
                                  string displayName, string avatarUrl, int pokerChips)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // Find next free seat (1, 2, or 3 — 0 is host)
        int seat = -1;
        for (int i = 1; i <= 3; i++)
        {
            if (!_takenSeats.Contains(i)) { seat = i; break; }
        }

        if (seat == -1)
        {
            Debug.LogWarning("[Network] Room full — ignoring seat request.");
            notificationManager.ShowWarningMessage("Room is full — unable to join.");
            return;
        }

        _takenSeats.Add(seat);
        _seatToActor[seat]  = actorNumber;
        _seatToName[seat]   = displayName;

        // Announce to everyone
        photonView.RPC(nameof(RPC_SeatConfirmed), RpcTarget.All,
            actorNumber, seat, playFabId, displayName, avatarUrl, pokerChips, false);

        int totalSeated = _takenSeats.Count + _botSeats.Count;
        SetStatus($"Waiting for players ({totalSeated}/4)...");

        if (totalSeated >= 4)
        {
            if (_botFillRoutine != null) StopCoroutine(_botFillRoutine);
            photonView.RPC(nameof(RPC_StartTeamVote), RpcTarget.All);
        }
    }

    /*[PunRPC]
    private void RPC_SeatConfirmed(int actorNumber, int seat,
                                    string playFabId, string displayName,
                                    string avatarUrl, int pokerChips, bool isBot)
    {
        _seatToActor[seat] = actorNumber;
        _seatToName[seat]  = displayName;

        bool isLocalPlayer = actorNumber == PhotonNetwork.LocalPlayer.ActorNumber;

        if (isLocalPlayer)
        {
            AssignLocalSeat(seat);
            if (!_takenSeats.Contains(seat)) _takenSeats.Add(seat);
        }
        else if (isBot)
        {
            if (!_botSeats.Contains(seat)) _botSeats.Add(seat);
        }
        else
        {
            if (!_takenSeats.Contains(seat)) _takenSeats.Add(seat);
        }

        // Update PlayFab panel for non-local players
        if (!isLocalPlayer)
        {
            int panelIdx = RemapSeatToPanel(seat);
            playFabManager.SetupOtherPlayer(
                panelIdx, playFabId, displayName, pokerChips, avatarUrl, isBot);
        }

        // Also push to PlayFab TeamVoteUI icon
        teamVoteUI?.SetVoteIcon(RemapSeatToPanel(seat), voted: false, yes: false);

        int totalSeated = _takenSeats.Count + _botSeats.Count;
        SetStatus($"Waiting for players ({totalSeated}/4)...");
    }*/

    [PunRPC]
    private void RPC_SeatConfirmed(int actorNumber, int seat,
                                string playFabId, string displayName,
                                string avatarUrl, int pokerChips, bool isBot)
    {
        _seatToActor[seat] = actorNumber;
        _seatToName[seat] = displayName;

        bool isLocalPlayer = actorNumber == PhotonNetwork.LocalPlayer.ActorNumber;

        if (isLocalPlayer)
        {
            AssignLocalSeat(seat);
            if (!_takenSeats.Contains(seat)) _takenSeats.Add(seat);
        }
        else if (isBot)
        {
            if (!_botSeats.Contains(seat)) _botSeats.Add(seat);
        }
        else
        {
            if (!_takenSeats.Contains(seat)) _takenSeats.Add(seat);
        }

        // ── FIX: always update panel for non-local players ──────────
        if (!isLocalPlayer)
        {
            int panelIdx = RemapSeatToPanel(seat);

            // Immediately set name and PC from RPC data (available instantly)
            playFabManager?.SetupOtherPlayer(
                panelIdx, playFabId, displayName, pokerChips, avatarUrl, isBot);

            // Then refresh from PlayFab for accurate name + avatar
            // (RPC data is reliable but avatar needs a fresh fetch)
            if (!isBot)
                RefreshOtherPlayerPanel(panelIdx, playFabId, displayName, pokerChips);
        }

        teamVoteUI?.SetVoteIcon(RemapSeatToPanel(seat), voted: false, yes: false);

        int totalSeated = _takenSeats.Count + _botSeats.Count;
        SetStatus($"Waiting for players ({totalSeated}/4)...");
    }



    /// <summary>
    /// Fetches fresh profile data from PlayFab for a non-local player
    /// and updates their panel. Called every time a player joins.
    /// </summary>
    private void RefreshOtherPlayerPanel(int panelIdx, string playFabId,
                                          string fallbackName, int fallbackPC)
    {
        PlayFab.PlayFabClientAPI.ExecuteCloudScript(
            new PlayFab.ClientModels.ExecuteCloudScriptRequest
            {
                FunctionName = "GetGameplayPlayerProfile",
                FunctionParameter = new { playFabId = playFabId },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                if (result.FunctionResult == null) return;

                string json = Newtonsoft.Json.JsonConvert
                    .SerializeObject(result.FunctionResult);

                CloudProfileResult profile = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<CloudProfileResult>(json);

                if (profile == null) return;

                GameplayPlayerData panel = playFabManager?.GetPlayerPanel(panelIdx);
                if (panel == null) return;

                // Use CloudScript result — more reliable than RPC payload
                string finalName = !string.IsNullOrEmpty(profile.displayName)
                    ? profile.displayName : fallbackName;
                string finalAvatar = profile.avatarUrl ?? "";

                panel.Setup(playFabId, finalName, fallbackPC,
                    seat: panelIdx, isBot: false);

                panel.LoadAvatar(finalAvatar);

                Debug.Log($"[Network] Panel {panelIdx} refreshed: {finalName}");
            },
            error =>
            {
                Debug.LogWarning($"[Network] RefreshOtherPlayerPanel failed for panel " +
                                 $"{panelIdx}: {error.GenerateErrorReport()}");
            });
    }

    // Add this private class inside GameplayNetworkManager
    // if it doesn't exist already
    [System.Serializable]
    private class CloudProfileResult
    {
        public string displayName;
        public string avatarUrl;
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[Network] Player entered: {newPlayer.NickName}");
        notificationManager.ShowWarningMessagePulse($"{newPlayer.NickName} has joined the game.");

        // Read their properties — Photon may need a moment to sync them
        // so we wait one frame before reading
        StartCoroutine(RefreshNewPlayerAfterDelay(newPlayer));
    }

    private IEnumerator RefreshNewPlayerAfterDelay(Player newPlayer)
    {
        // Wait two frames for Photon to sync custom properties
        yield return null;
        yield return null;

        object seatObj, pfIdObj, nameObj, avatarObj, pcObj, botObj;

        if (!newPlayer.CustomProperties.TryGetValue(PROP_SEAT, out seatObj)) yield break;
        if (!newPlayer.CustomProperties.TryGetValue(PROP_PLAYFAB_ID, out pfIdObj)) yield break;
        if (!newPlayer.CustomProperties.TryGetValue(PROP_DISPLAY_NAME, out nameObj)) yield break;
        if (!newPlayer.CustomProperties.TryGetValue(PROP_POKER_CHIPS, out pcObj)) yield break;

        newPlayer.CustomProperties.TryGetValue(PROP_AVATAR_URL, out avatarObj);
        newPlayer.CustomProperties.TryGetValue(PROP_IS_BOT, out botObj);

        int seat = (int)seatObj;
        string playFabId = pfIdObj?.ToString() ?? "";
        string displayName = nameObj?.ToString() ?? "Player";
        int pokerChips = pcObj is int pc ? pc : 0;
        string avatarUrl = avatarObj?.ToString() ?? "";
        bool isBot = botObj is bool b && b;

        // Skip local player
        if (newPlayer.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber) yield break;

        int panelIdx = RemapSeatToPanel(seat);

        // Immediately show name and PC
        playFabManager?.SetupOtherPlayer(
            panelIdx, playFabId, displayName, pokerChips, avatarUrl, isBot);

        // Then fetch fresh from PlayFab
        if (!isBot)
            RefreshOtherPlayerPanel(panelIdx, playFabId, displayName, pokerChips);
    }

    private void AssignLocalSeat(int seat)
    {
        _localSeat = seat;
        ExitGames.Client.Photon.Hashtable prop = new ExitGames.Client.Photon.Hashtable { { PROP_SEAT, seat } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(prop);
        Debug.Log($"[Network] Local seat: {seat}");
    }

    // ─────────────────────────────────────────────────────────────
    //  BOT FILL
    // ─────────────────────────────────────────────────────────────

    private IEnumerator BotFillCountdown()
    {
        float elapsed = 0f;
        float wait    = _config.botFillDelay;

        while (elapsed < wait)
        {
            elapsed += Time.deltaTime;
            int totalSeated = _takenSeats.Count + _botSeats.Count;
            if (totalSeated >= 4) yield break;

            // Update status with countdown
            int remaining = Mathf.CeilToInt(wait - elapsed);
            SetStatus($"Waiting for players ({totalSeated}/4) — {remaining}s");
            yield return null;
        }

        
        if (!PhotonNetwork.IsMasterClient) yield break;
        if (_takenSeats.Count + _botSeats.Count >= 4) yield break;

        FillRemainingWithBots();
    }

    private void FillRemainingWithBots()
    {
        int botIdx = 0;
        for (int seat = 1; seat <= 3; seat++)
        {
            if (_takenSeats.Contains(seat) || _botSeats.Contains(seat)) continue;

            string botName = botIdx < BOT_NAMES.Length ? BOT_NAMES[botIdx] : $"Bot {botIdx + 1}";
            int    botPC   = UnityEngine.Random.Range(2000, 8001);
            string botId   = $"BOT_SEAT_{seat}";
            botIdx++;

            _botSeats.Add(seat);
            _seatToName[seat] = botName;

            photonView.RPC(nameof(RPC_SeatConfirmed), RpcTarget.All,
                -(seat + 100),  // negative actor number = bot marker
                seat, botId, botName, "", botPC, true);
        }

        if (_takenSeats.Count + _botSeats.Count >= 4)
            photonView.RPC(nameof(RPC_StartTeamVote), RpcTarget.All);
    }

    private void ReplaceWithBot(Player leftPlayer)
    {
        object seatObj;
        if (!leftPlayer.CustomProperties.TryGetValue(PROP_SEAT, out seatObj)) return;

        int seat    = (int)seatObj;
        string name = $"Bot {seat}";
        int    pc   = UnityEngine.Random.Range(2000, 8001);

        if (!_botSeats.Contains(seat)) _botSeats.Add(seat);
        _takenSeats.Remove(seat);
        _seatToName[seat] = name;

        photonView.RPC(nameof(RPC_SeatConfirmed), RpcTarget.All,
            -(seat + 100), seat, $"BOT_SEAT_{seat}", name, "", pc, true);
    }

    // ─────────────────────────────────────────────────────────────
    //  TEAM VOTE
    // ─────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_StartTeamVote()
    {
        SetState(GameState.TeamVote);
        _teamVoteYes   = 0;
        _teamVoteNo    = 0;
        _localVoteDone = false;

        teamVoteUI?.Show(
            onYes: () => { if (!_localVoteDone) { _localVoteDone = true; CastVote(true);  } },
            onNo:  () => { if (!_localVoteDone) { _localVoteDone = true; CastVote(false); } });
    }

    private void CastVote(bool yes)
    {
        teamVoteUI?.SetVoteIcon(0, voted: true, yes: yes);
        photonView.RPC(nameof(RPC_ReceiveVote), RpcTarget.All,
            _localSeat, yes);
    }

    [PunRPC]
    private void RPC_ReceiveVote(int fromSeat, bool yes)
    {
        // Update icon for the voting player
        teamVoteUI?.SetVoteIcon(RemapSeatToPanel(fromSeat), voted: true, yes: yes);

        if (yes) _teamVoteYes++;
        else     _teamVoteNo++;

        // Bots auto-vote yes (host sends their votes)
        int humanCount = 4 - _botSeats.Count;
        if (_teamVoteYes + _teamVoteNo < humanCount) return;

        // Add bot votes
        if (PhotonNetwork.IsMasterClient)
        {
            foreach (int botSeat in _botSeats)
            {
                _teamVoteYes++;
                photonView.RPC(nameof(RPC_ReceiveBotVote), RpcTarget.All, botSeat);
            }

            bool teamMode = (_teamVoteNo == 0); // everyone must say yes
            photonView.RPC(nameof(RPC_TeamVoteResult), RpcTarget.All, teamMode);
        }
    }

    [PunRPC]
    private void RPC_ReceiveBotVote(int botSeat)
    {
        teamVoteUI?.SetVoteIcon(RemapSeatToPanel(botSeat), voted: true, yes: true);
    }

    [PunRPC]
    private void RPC_TeamVoteResult(bool teamMode)
    {
        _isTeamMode = teamMode;

        teamVoteUI?.Hide();
        playFabManager.SetTeamMode(teamMode);
        playFabManager.DeductBetOnMatchStart();

        Debug.Log($"[Network] Team mode result: {teamMode}");

        if (PhotonNetwork.IsMasterClient)
            StartNewMatch();
    }

    // ─────────────────────────────────────────────────────────────
    //  MATCH START
    // ─────────────────────────────────────────────────────────────

    private void StartNewMatch()
    {
        _team1Score = 0;
        _team2Score = 0;
        int firstDealer = UnityEngine.Random.Range(0, 4);
        photonView.RPC(nameof(RPC_StartHand), RpcTarget.All, firstDealer);
    }

    // ─────────────────────────────────────────────────────────────
    //  HAND START
    // ─────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_StartHand(int dealerSeat)
    {
        _dealerSeat      = dealerSeat;
        _team1Tricks     = 0;
        _team2Tricks     = 0;
        _trump           = ' ';
        _trumpPickerSeat = -1;
        _cycled          = 0;
        _skipSeat        = -1;
        _played.Clear();
        _hands.Clear();
        _deck.Clear();

        SetState(GameState.Dealing);
        SetStatus("Dealing cards...");

        playFabManager.SetDealer(RemapSeatToPanel(dealerSeat));
        playFabManager.ClearTrump();

        trickDisplay?.ResetTricks(_team1Score, _team2Score);


        if (PhotonNetwork.IsMasterClient)
            DealCards();
    }

    // ─────────────────────────────────────────────────────────────
    //  DEALING
    // ─────────────────────────────────────────────────────────────

    private void DealCards()
    {
        // Build deck
        foreach (string type in CARD_TYPES)
            foreach (string suit in CARD_SUITS)
                _deck.Add(type + suit);

        // Fisher-Yates shuffle
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j      = UnityEngine.Random.Range(0, i + 1);
            string tmp = _deck[i];
            _deck[i]   = _deck[j];
            _deck[j]   = tmp;
        }

        // Deal 5 cards per seat
        for (int seat = 0; seat < 4; seat++)
        {
            _hands[seat] = new List<string>();
            for (int c = 0; c < 5; c++)
            {
                _hands[seat].Add(_deck[0]);
                _deck.RemoveAt(0);
            }
        }

        string upcard = _deck[0]; // top of remaining deck

        // Send each human player their hand
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            object seatObj;
            if (!p.CustomProperties.TryGetValue(PROP_SEAT, out seatObj)) continue;

            int    seat    = (int)seatObj;
            string handStr = string.Join(",", _hands[seat]);

            photonView.RPC(nameof(RPC_ReceiveHand), p,
                handStr, upcard, _dealerSeat);
        }

        // Bot hands stay on master client — no RPC needed
        // master client already has _hands populated
    }

    [PunRPC]
    private void RPC_ReceiveHand(string handStr, string upcard, int dealerSeat)
    {
        List<string> hand = new List<string>(handStr.Split(','));
        _hands[_localSeat] = hand;
        _deck.Clear();
        _deck.Add(upcard); // store upcard at index 0

        SetState(GameState.TrumpPhase1);
        cardManager?.AnimateDeal(hand, upcard, dealerSeat, _localSeat);
        // EuchreCardManager calls BeginTrumpPhase1() after deal animation finishes
    }

    // ─────────────────────────────────────────────────────────────
    //  TRUMP PHASE 1 — Order Up / Pass
    // ─────────────────────────────────────────────────────────────

    /// <summary>Called by EuchreCardManager after deal animation completes.</summary>
    public void BeginTrumpPhase1()
    {
        _cycled = 0;
        int firstSeat = (_dealerSeat + 1) % 4;
        photonView.RPC(nameof(RPC_TrumpPhase1Turn), RpcTarget.All,
            firstSeat, _deck.Count > 0 ? _deck[0] : "9C");
    }

    [PunRPC]
    private void RPC_TrumpPhase1Turn(int activeSeat, string upcard)
    {
        SetState(GameState.TrumpPhase1);
        _turnSeat = activeSeat;

        bool isMyTurn   = activeSeat == _localSeat;
        bool isDealer   = activeSeat == _dealerSeat;
        bool forced     = _cycled > 0 && isDealer; // dealer forced on second pass

        SetStatus(isMyTurn
            ? "Your turn — Order up or Pass?"
            : $"{_seatToName.GetValueOrDefault(activeSeat, "Player")}'s turn...");

        // ── Bot ───────────────────────────────────────────────────
        if (_botSeats.Contains(activeSeat) && PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(BotTrumpPhase1(activeSeat, upcard, forced));
            return;
        }

        if (!isMyTurn) return;

        char upcardSuit = upcard.Length > 1 ? upcard[1] : 'S';

        trumpUI?.ShowPhase1(upcardSuit, canPass: !forced,
            onOrderUp: () =>
            {
                // Go-alone prompt is inside TrumpSelectionUI
                // OnGoAloneNo / OnGoAloneYes call back here
                photonView.RPC(nameof(RPC_TrumpOrdered), RpcTarget.All,
                    _localSeat, (int)upcardSuit);
            },
            onPass: () =>
            {
                AdvanceTrumpPhase1(upcard);
            });
    }

    

    private void AdvanceTrumpPhase1(string upcard)
    {
        int next = (_turnSeat + 1) % 4;

        // Check if we've completed a full cycle
        if (next == (_dealerSeat + 1) % 4)
        {
            _cycled++;

            // Dealer forced pick: _cycled == 1 means first full pass done
            // but standard Euchre: if cycled >= 1, dealer must pick in phase 1
            // Actually in standard: phase 1 is ordering up the upcard.
            // After one full cycle with no order-up → phase 2 (choose suit).
            if (_cycled >= 1)
            {
                photonView.RPC(nameof(RPC_StartTrumpPhase2), RpcTarget.All,
                    upcard.Length > 1 ? upcard[1].ToString() : "S");
                return;
            }
        }

        photonView.RPC(nameof(RPC_TrumpPhase1Turn), RpcTarget.All, next, upcard);
    }

    [PunRPC]
    private void RPC_TrumpOrdered(int pickerSeat, int suitInt)
    {
        
        turnTimer?.StopTimer();
        char suit        = (char)suitInt;
        _trump           = suit;
        _trumpPickerSeat = pickerSeat;

        trumpUI?.Hide();
        playFabManager.SetTrump(RemapSeatToPanel(pickerSeat), suit);
        cardManager?.HideUpcard();

        SetState(GameState.Playing);
        SetStatus("Trump selected — game begins!");

        if (PhotonNetwork.IsMasterClient)
            StartCoroutine(DelayThen(() =>
            {
                int first = (_dealerSeat + 1) % 4;
                photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, first);
            }, 1f));
    }

    // ─────────────────────────────────────────────────────────────
    //  TRUMP PHASE 2 — Choose Suit
    // ─────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_StartTrumpPhase2(string excludedSuit)
    {
        SetState(GameState.TrumpPhase2);
        _cycled = 0;

        int firstSeat = (_dealerSeat + 1) % 4;
        photonView.RPC(nameof(RPC_TrumpPhase2Turn), RpcTarget.All,
            firstSeat, excludedSuit);
    }

    [PunRPC]
    private void RPC_TrumpPhase2Turn(int activeSeat, string excludedSuit)
    {
        _turnSeat = activeSeat;

        turnIndicator?.SetActiveTurn(RemapSeatToPanel(activeSeat));

        bool isMyTurn = activeSeat == _localSeat;
        bool isDealer = activeSeat == _dealerSeat;
        bool forced   = isDealer; // dealer MUST pick in phase 2

        turnTimer?.StartTimer(isMyTurn, RemapSeatToPanel(activeSeat)); // ← add panel index

        SetStatus(isMyTurn
            ? "Choose trump suit"
            : $"{_seatToName.GetValueOrDefault(activeSeat, "Player")} is choosing...");

        // ── Bot ───────────────────────────────────────────────────
        if (_botSeats.Contains(activeSeat) && PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(BotTrumpPhase2(activeSeat, excludedSuit, forced));
            return;
        }

        if (!isMyTurn) return;

        trumpUI?.ShowPhase2(excludedSuit, canPass: !forced,
            onSuitPicked: (suit) =>
            {
                photonView.RPC(nameof(RPC_TrumpSuitChosen), RpcTarget.All,
                    _localSeat, suit.ToString());
            },
            onPass: () =>
            {
                int next = (_turnSeat + 1) % 4;
                photonView.RPC(nameof(RPC_TrumpPhase2Turn), RpcTarget.All,
                    next, excludedSuit);
            });
    }

    [PunRPC]
    private void RPC_TrumpSuitChosen(int pickerSeat, string suitStr)
    {
        _trump           = suitStr[0];
        _trumpPickerSeat = pickerSeat;

        trumpUI?.Hide();
        playFabManager.SetTrump(RemapSeatToPanel(pickerSeat), _trump);
        cardManager?.HideUpcard();

        SetState(GameState.Playing);
        SetStatus("Trump chosen — game begins!");

        notificationManager.ShowWarningMessagePulse($"Trump is {_trump}!");

        if (PhotonNetwork.IsMasterClient)
            StartCoroutine(DelayThen(() =>
            {
                int first = (_dealerSeat + 1) % 4;
                photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, first);
            }, 1f));
    }

    // ─────────────────────────────────────────────────────────────
    //  GO ALONE
    // ─────────────────────────────────────────────────────────────

    /// <summary>Called by TrumpSelectionUI when local player confirms go alone.</summary>
    public void LocalGoAlone()
    {
        int partnerSeat = (_localSeat + 2) % 4;
        photonView.RPC(nameof(RPC_GoAlone), RpcTarget.All, _localSeat, partnerSeat);
    }

    [PunRPC]
    private void RPC_GoAlone(int goAloneSeat, int partnerSeat)
    {
        _skipSeat = partnerSeat;
        playFabManager.SetGoAlone(RemapSeatToPanel(goAloneSeat));

        Debug.Log($"[Network] Go alone: seat {goAloneSeat}, skip seat {partnerSeat}");
    }

    // ─────────────────────────────────────────────────────────────
    //  TRICK PLAYING
    // ─────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_BeginTurn(int activeSeat)
    {
        // Skip partner if going alone
        if (_skipSeat != -1 && activeSeat == _skipSeat)
        {
            int next = (activeSeat + 1) % 4;
            if (next == _skipSeat) next = (next + 1) % 4;
            photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, next);
            return;
        }

        _turnSeat = activeSeat;

        turnIndicator?.SetActiveTurn(RemapSeatToPanel(activeSeat));

        // ADD THIS
        bool isLocalTurn = activeSeat == _localSeat;
        
        //turnTimer?.StartTimer(isLocalTurn);


        bool isMyTurn = activeSeat == _localSeat;

        turnTimer?.StartTimer(isMyTurn, RemapSeatToPanel(activeSeat)); // ← add panel index

        SetStatus(isMyTurn
            ? "Your turn — select a card"
            : $"{_seatToName.GetValueOrDefault(activeSeat, "Player")}'s turn...");

        // ── Bot ───────────────────────────────────────────────────
        if (_botSeats.Contains(activeSeat) && PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(BotPlayCard(activeSeat));
            return;
        }

        if (!isMyTurn) return;

        // Enable card selection for local player
        cardManager?.EnableCardSelection(true, _played, _trump, ALT_SUITS);
    }

    /// <summary>Called by EuchreCardManager when local player confirms a card.</summary>
    public void LocalPlayCard(string cardType)
    {
        if (_turnSeat != _localSeat) return;

        cardManager?.EnableCardSelection(false, null, ' ', null);

        if (_hands.ContainsKey(_localSeat))
            _hands[_localSeat].Remove(cardType);

        photonView.RPC(nameof(RPC_CardPlayed), RpcTarget.All, _localSeat, cardType);
    }

    [PunRPC]
    private void RPC_CardPlayed(int fromSeat, string cardType)
    {
        
        // ADD THIS at the very top
        turnTimer?.StopTimer();

        
        _played.Add(cardType);

        // Track trick order
        _trickOrder.Add(fromSeat);

        int panelIdx = RemapSeatToPanel(fromSeat);
        cardManager?.AnimateCardToTable(panelIdx, cardType);

        int numPlaying = _skipSeat != -1 ? 3 : 4;

        if (_played.Count >= numPlaying)
        {
            SetState(GameState.ScoringTrick);
            if (PhotonNetwork.IsMasterClient)
                StartCoroutine(DelayThen(ScoreTrick, 1.5f));
        }
        else
        {
            int next = (fromSeat + 1) % 4;
            if (_skipSeat != -1 && next == _skipSeat)
                next = (next + 1) % 4;

            photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, next);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  TRICK SCORING
    // ─────────────────────────────────────────────────────────────

    private void ScoreTrick()
    {
        if (_played.Count == 0) return;

        char leadSuit = _played[0][1];
        if (_played[0][0] == 'J' && ALT_SUITS.ContainsKey(_trump)
            && _played[0][1] == ALT_SUITS[_trump])
            leadSuit = _trump;

        int bestVal      = -1;
        int winnerTrickIdx = 0;

        for (int i = 0; i < _played.Count; i++)
        {
            int val = EvaluateCard(_played[i], leadSuit);
            if (val > bestVal)
            {
                bestVal        = val;
                winnerTrickIdx = i;
            }
        }

        int winnerSeat = winnerTrickIdx < _trickOrder.Count
            ? _trickOrder[winnerTrickIdx]
            : _dealerSeat;

        photonView.RPC(nameof(RPC_TrickWon), RpcTarget.All, winnerSeat);
    }

    /*[PunRPC]
    private void RPC_TrickWon(int winnerSeat)
    {
        turnIndicator?.SetActiveTurn(-1);
        
        if (winnerSeat % 2 == 0) _team1Tricks++;
        else                      _team2Tricks++;

        _played.Clear();
        _trickOrder.Clear();

        cardManager?.ClearTableCards();

        // ADD THIS
        trickDisplay?.UpdateTrickScore(
            _team1Tricks, _team2Tricks,
            _team1Score,  _team2Score,
            _isTeamMode);

        int totalTricks = _team1Tricks + _team2Tricks;

        SetStatus($"{_seatToName.GetValueOrDefault(winnerSeat, "Player")} wins the trick!");

        if (totalTricks >= 5)
        {
            if (PhotonNetwork.IsMasterClient)
                StartCoroutine(DelayThen(() =>
                    photonView.RPC(nameof(RPC_ScoreHand), RpcTarget.All,
                        _team1Tricks, _team2Tricks), 0.8f));
        }
        else
        {
            if (PhotonNetwork.IsMasterClient)
                StartCoroutine(DelayThen(() =>
                    photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, winnerSeat), 0.5f));
        }
    }*/

    [PunRPC]
    private void RPC_TrickWon(int winnerSeat)
    {
        turnTimer?.StopTimer();
        turnIndicator?.SetActiveTurn(-1);

        if (winnerSeat % 2 == 0) _team1Tricks++;
        else _team2Tricks++;

        _played.Clear();
        _trickOrder.Clear();

        // UPDATE: fly cards to winner instead of just clearing
        int winnerPanelIndex = RemapSeatToPanel(winnerSeat);
        cardManager?.FlyCardsToWinner(winnerPanelIndex, onComplete: () =>
        {
            // Everything that used to happen after ClearTableCards now happens
            // inside this callback — after the animation finishes

            trickDisplay?.UpdateTrickScore(
                _team1Tricks, _team2Tricks,
                _team1Score, _team2Score,
                _isTeamMode);

            SetStatus($"{SeatName(winnerSeat)} wins the trick!");

            notificationManager.ShowWarningMessagePulse($"{SeatName(winnerSeat)} wins the trick!");

            int totalTricks = _team1Tricks + _team2Tricks;

            if (totalTricks >= 5)
            {
                if (PhotonNetwork.IsMasterClient)
                    StartCoroutine(DelayThen(() =>
                        photonView.RPC(nameof(RPC_ScoreHand), RpcTarget.All,
                            _team1Tricks, _team2Tricks), 0.8f));
            }
            else
            {
                if (PhotonNetwork.IsMasterClient)
                    StartCoroutine(DelayThen(() =>
                        photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, winnerSeat), 0.5f));
            }
        });
    }



    // ─────────────────────────────────────────────────────────────
    //  HAND SCORING
    // ─────────────────────────────────────────────────────────────

    /*[PunRPC]
    private void RPC_ScoreHand(int t1Tricks, int t2Tricks)
    {
        _team1Tricks = t1Tricks;
        _team2Tricks = t2Tricks;

        int pts1 = 0, pts2 = 0;
        bool wentAlone = _skipSeat != -1;

        // Standard Euchre scoring
        if (_trumpPickerSeat % 2 == 0) // team 1 picked trump
        {
            if (_team1Tricks == 5)      pts1 = wentAlone ? 4 : 2;
            else if (_team1Tricks >= 3) pts1 = 1;
            else                        pts2 = 2; // euchred
        }
        else // team 2 picked trump
        {
            if (_team2Tricks == 5)      pts2 = wentAlone ? 4 : 2;
            else if (_team2Tricks >= 3) pts2 = 1;
            else                        pts1 = 2; // euchred
        }

        _team1Score += pts1;
        _team2Score += pts2;

        SetState(GameState.HandOver);

        Debug.Log($"[Network] Hand scored: T1={_team1Score} T2={_team2Score}");

        if (_team1Score >= WIN_SCORE || _team2Score >= WIN_SCORE)
        {
            EndMatch();
            return;
        }

        // Next hand — rotate dealer
        if (PhotonNetwork.IsMasterClient)
        {
            int nextDealer = (_dealerSeat + 1) % 4;
            StartCoroutine(DelayThen(() =>
                photonView.RPC(nameof(RPC_StartHand), RpcTarget.All, nextDealer), 2f));
        }
    }*/

    [PunRPC]
    private void RPC_ScoreHand(int t1Tricks, int t2Tricks)
    {
        _team1Tricks = t1Tricks;
        _team2Tricks = t2Tricks;

        int pts1 = 0, pts2 = 0;
        bool wentAlone  = _skipSeat != -1;
        bool team1Called = _trumpPickerSeat % 2 == 0;

        if (team1Called)
        {
            if (wentAlone)
            {
                // Solo player is on team 1
                if (_team1Tricks == 5)      pts1 = 4; // all 5 alone = 4 pts
                else if (_team1Tricks >= 3) pts1 = 1; // 3 or 4 tricks = 1 pt
                else                        pts2 = 2; // euchred = 2 pts to opponents
            }
            else
            {
                if (_team1Tricks == 5)      pts1 = 2; // march = 2 pts
                else if (_team1Tricks >= 3) pts1 = 1; // 3 or 4 tricks = 1 pt
                else                        pts2 = 2; // euchred = 2 pts to opponents
            }
        }
        else
        {
            if (wentAlone)
            {
                if (_team2Tricks == 5)      pts2 = 4;
                else if (_team2Tricks >= 3) pts2 = 1;
                else                        pts1 = 2;
            }
            else
            {
                if (_team2Tricks == 5)      pts2 = 2;
                else if (_team2Tricks >= 3) pts2 = 1;
                else                        pts1 = 2;
            }
        }

        _team1Score += pts1;
        _team2Score += pts2;

        // Build hand result summary for game over panel
        string handSummary = BuildHandSummary(pts1, pts2, wentAlone, team1Called);

        trickDisplay?.UpdateTrickScore(
            _team1Tricks, _team2Tricks,
            _team1Score,  _team2Score,
            _isTeamMode);

        SetState(GameState.HandOver);
        SetStatus($"Hand over! T1:{_team1Score} — T2:{_team2Score}");

        Debug.Log($"[Network] Hand scored → T1:{_team1Score} pts2:{_team2Score} " +
                $"| tricks T1:{_team1Tricks} T2:{_team2Tricks}");

        if (_team1Score >= WIN_SCORE || _team2Score >= WIN_SCORE)
        {
            EndMatch(handSummary);
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            int nextDealer = (_dealerSeat + 1) % 4;
            StartCoroutine(DelayThen(() =>
                photonView.RPC(nameof(RPC_StartHand), RpcTarget.All, nextDealer), 2f));
        }
    }

    private string BuildHandSummary(int pts1, int pts2, bool wentAlone, bool team1Called)
    {
        string caller   = team1Called ? "Team 1" : "Team 2";
        string playMode = wentAlone   ? "Going Alone" : "Team Play";
        string result   = "";

        if (team1Called)
        {
            if (pts1 > 0)
            {
                if (_team1Tricks == 5 && wentAlone) result = "March Alone! +4 pts";
                else if (_team1Tricks == 5)         result = "March! +2 pts";
                else                                result = "Made it! +1 pt";
            }
            else
                result = "Euchred! Opponents +2 pts";
        }
        else
        {
            if (pts2 > 0)
            {
                if (_team2Tricks == 5 && wentAlone) result = "March Alone! +4 pts";
                else if (_team2Tricks == 5)         result = "March! +2 pts";
                else                                result = "Made it! +1 pt";
            }
            else
                result = "Euchred! Opponents +2 pts";
        }

        return $"Trump caller: {caller} | Mode: {playMode}\n" +
            $"Tricks — T1: {_team1Tricks} | T2: {_team2Tricks}\n" +
            $"Result: {result}";
    }

    // ─────────────────────────────────────────────────────────────
    //  MATCH END
    // ─────────────────────────────────────────────────────────────

    /*private void EndMatch()
    {
        SetState(GameState.MatchOver);

        bool localWon = (_localSeat % 2 == 0 && _team1Score >= WIN_SCORE)
                     || (_localSeat % 2 == 1 && _team2Score >= WIN_SCORE);

        int pot       = _config?.totalPot    ?? 400;
        int betAmount = _config?.betAmount   ?? 100;

        if (localWon)
            playFabManager.DistributeWinnings();

        int localTricks = _localSeat % 2 == 0 ? _team1Tricks : _team2Tricks;
        playFabManager.SaveMatchStats(localWon, localTricks, 5);

        int winnerTeam = _team1Score >= WIN_SCORE ? 1 : 2;

        gameOverUI?.Show(
            winnerTeam:     winnerTeam,
            team1Score:     _team1Score,
            team2Score:     _team2Score,
            onRematch:      OnRematch,
            onMainMenu:     OnMainMenu,
            localPlayerWon: localWon,
            pot:            pot,
            betAmount:      betAmount);
    }*/

    private void EndMatch(string lastHandSummary = "")
    {
        SetState(GameState.MatchOver);
        turnTimer?.StopTimer();
        turnIndicator?.SetActiveTurn(-1);

        bool localWon = (_localSeat % 2 == 0 && _team1Score >= WIN_SCORE)
                    || (_localSeat % 2 == 1 && _team2Score >= WIN_SCORE);

        int winnerTeam   = _team1Score >= WIN_SCORE ? 1 : 2;
        int pot          = _config?.totalPot  ?? 400;
        int betAmount    = _config?.betAmount ?? 100;

        // ── Trophy calculation ─────────────────────────────────────
        int trophyChange = localWon ? 30 : -15;

        Debug.Log($"[Network] Match over — Local player {(localWon ? "WON" : "LOST")}");
        Debug.Log($"[Network] Final Score — Team 1: {_team1Score} | Team 2: {_team2Score}");
        Debug.Log($"[Network] Trophy change: {(trophyChange > 0 ? "+" : "")}{trophyChange}");

        // ── Economy ────────────────────────────────────────────────
        if (localWon)
            playFabManager?.DistributeWinnings();

        int localTricks = _localSeat % 2 == 0 ? _team1Tricks : _team2Tricks;
        playFabManager?.SaveMatchStats(localWon, localTricks, 5);

        // ── Build full match stats for game over panel ─────────────
        MatchEndData endData = new MatchEndData
        {
            localWon        = localWon,
            winnerTeam      = winnerTeam,
            team1Score      = _team1Score,
            team2Score      = _team2Score,
            team1Tricks     = _team1Tricks,
            team2Tricks     = _team2Tricks,
            pot             = pot,
            betAmount       = betAmount,
            trophyChange    = trophyChange,
            lastHandSummary = lastHandSummary,
            isTeamMode      = _isTeamMode,
            localTeam       = _localSeat % 2 == 0 ? 1 : 2
        };

        
        /*gameOverUI?.Show(
            endData:      endData,
            onRematch:    OnRematch,
            onMainMenu:   OnMainMenu);
        */

        gameOverUI?.Show(endData, OnRematch, OnMainMenu);

        
    }

    private void OnRematch()
    {
        trickDisplay?.ResetDisplay();
        playFabManager?.DeductBetOnMatchStart();
        if (PhotonNetwork.IsMasterClient) StartNewMatch();
    }

    private void OnMainMenu()
    {
        PhotonNetwork.LeaveRoom();
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    // ─────────────────────────────────────────────────────────────
    //  BOT AI
    // ─────────────────────────────────────────────────────────────

    private IEnumerator BotTrumpPhase1(int seat, string upcard, bool forced)
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(0.7f, 1.4f));

        char upcardSuit = upcard.Length > 1 ? upcard[1] : 'S';

        // Simple bot: order up ~60% of the time; always if forced
        bool orderUp = forced || UnityEngine.Random.value > 0.4f;

        if (orderUp)
            photonView.RPC(nameof(RPC_TrumpOrdered), RpcTarget.All,
                seat, (int)upcardSuit);
        else
            AdvanceTrumpPhase1(upcard);
    }

    private IEnumerator BotTrumpPhase2(int seat, string excludedSuit, bool forced)
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(0.7f, 1.4f));

        bool pick = forced || UnityEngine.Random.value > 0.4f;

        if (pick)
        {
            // Pick first available suit
            string chosen = "C";
            foreach (string s in new[] { "C", "D", "H", "S" })
            {
                if (s != excludedSuit) { chosen = s; break; }
            }
            photonView.RPC(nameof(RPC_TrumpSuitChosen), RpcTarget.All, seat, chosen);
        }
        else
        {
            int next = (seat + 1) % 4;
            photonView.RPC(nameof(RPC_TrumpPhase2Turn), RpcTarget.All, next, excludedSuit);
        }
    }

    private IEnumerator BotPlayCard(int seat)
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(0.7f, 1.4f));

        if (!_hands.ContainsKey(seat) || _hands[seat].Count == 0) yield break;

        string card = ChooseBotCard(seat);
        if (string.IsNullOrEmpty(card)) yield break;

        _hands[seat].Remove(card);
        photonView.RPC(nameof(RPC_CardPlayed), RpcTarget.All, seat, card);
    }

    private string ChooseBotCard(int seat)
    {
        List<string> hand = _hands[seat];
        if (hand == null || hand.Count == 0) return null;

        // Follow lead suit if possible
        if (_played.Count > 0)
        {
            char leadSuit = _played[0][1];
            if (_played[0][0] == 'J' && ALT_SUITS.ContainsKey(_trump)
                && _played[0][1] == ALT_SUITS[_trump])
                leadSuit = _trump;

            foreach (string c in hand)
            {
                bool isLeft = c[0] == 'J' && ALT_SUITS.ContainsKey(_trump)
                           && c[1] == ALT_SUITS[_trump];
                if ((c[1] == leadSuit && !isLeft) || (isLeft && leadSuit == _trump))
                    return c;
            }
        }

        // Play highest trump if available
        int bestVal  = -1;
        string bestCard = hand[0];
        foreach (string c in hand)
        {
            int val = EvaluateCard(c, _trump);
            if (val > bestVal) { bestVal = val; bestCard = c; }
        }

        return bestCard;
    }

    // ─────────────────────────────────────────────────────────────
    //  CARD EVALUATION
    // ─────────────────────────────────────────────────────────────

    private int EvaluateCard(string cardType, char leadSuit)
    {
        char val  = cardType[0];
        char suit = cardType[1];

        bool isRight = val == 'J' && suit == _trump;
        bool isLeft  = val == 'J' && ALT_SUITS.ContainsKey(_trump)
                    && suit == ALT_SUITS[_trump];

        if (isRight) return 15;
        if (isLeft)  return 14;

        if (suit == _trump)
            return val switch
            { 'A'=>13,'K'=>12,'Q'=>11,'T'=>10,'9'=>9,_=>0 };

        if (suit == leadSuit)
            return val switch
            { 'A'=>7,'K'=>6,'Q'=>5,'J'=>4,'T'=>3,'9'=>2,_=>0 };

        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Remaps absolute seat to UI panel index relative to local player.
    /// Local  → 0, Partner (seat+2%4) → 1,
    /// Left (seat+1%4) → 2, Right (seat+3%4) → 3.
    /// </summary>
    public int RemapSeatToPanel(int absoluteSeat)
    {
        if (absoluteSeat == _localSeat) return 0;
        int diff = (absoluteSeat - _localSeat + 4) % 4;
        return diff switch { 2=>1, 1=>2, 3=>3, _=>0 };
    }

    private void SetState(GameState state)
    {
        _state = state;
        Debug.Log($"[Network] → {state}");
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    private IEnumerator DelayThen(Action action, float delay)
    {
        yield return new WaitForSeconds(delay);
        action?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC ACCESSORS
    // ─────────────────────────────────────────────────────────────

    public int       LocalSeat   => _localSeat;
    public int       TurnSeat    => _turnSeat;
    public char      Trump       => _trump;
    public bool      IsTeamMode  => _isTeamMode;
    public int       Team1Score  => _team1Score;
    public int       Team2Score  => _team2Score;
    public GameState State       => _state;

    public List<string> GetLocalHand() =>
        _hands.ContainsKey(_localSeat)
            ? new List<string>(_hands[_localSeat])
            : new List<string>();
}
