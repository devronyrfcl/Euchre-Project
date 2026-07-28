using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

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
    [Tooltip("Used instead of trumpUI when MatchConfig.matchType == \"rated\" — no go-alone option.")]
    [SerializeField] private TrumpSelectionUIForRatedGame trumpUIForRatedGame;
    [SerializeField] private TeamVoteUI             teamVoteUI;
    [SerializeField] private GameOverUI             gameOverUI;
    [SerializeField] private NotificationManagerInGame notificationManager;

    [Header("=== STATUS ===")]
    [SerializeField] private TMPro.TMP_Text statusText;

    [Header("=== TURN INDICATOR ===")]
    [SerializeField] private TurnIndicatorController turnIndicator;

    // Add field at top
    [Header("=== TIMER ===")]
    [SerializeField] private TurnTimerController turnTimer;

    [Header("=== SCORE DISPLAY ===")]
    [SerializeField] private TrickScoreDisplay trickDisplay;

    [Header("=== INTRO LOADING PANEL ===")]
    [SerializeField] private GameObject    allPlayerUICards;
    [SerializeField] private GameObject    firstLoadingPanel;
    [SerializeField] private Image         firstLoadingPanelBg;
    [SerializeField] private RectTransform player1Card;
    [SerializeField] private RectTransform player2Card;
    [SerializeField] private RectTransform player3Card;
    [SerializeField] private RectTransform player4Card;
    [SerializeField] private RectTransform vsObject;
    [SerializeField] private TMP_Text[]    loadingNameTexts   = new TMP_Text[4];
    [SerializeField] private TMP_Text[]    loadingGPTexts     = new TMP_Text[4];
    [SerializeField] private Image[]       loadingAvatarImages = new Image[4];
    [SerializeField] private TMP_Text      matchTitleText;
    [SerializeField] private TMP_Text      roomCodeDisplayText;
    [SerializeField] private GameObject roomCodeDisplayPanel;
    [Header("=== GAME OVER (loading panel) ===")]
    [SerializeField] private GameObject    team1PointObj;
    [SerializeField] private TMP_Text      team1FinalScoreText;
    [SerializeField] private GameObject    team2PointObj;
    [SerializeField] private TMP_Text      team2FinalScoreText;
    [SerializeField] private GameObject    winnerTagTeam1;
    [SerializeField] private GameObject    winnerTagTeam2;
    [SerializeField] private GameObject    spreadFallParticle;
    [SerializeField] private GameObject    mainMenuButtonObj;

    [Header("=== TOURNAMENT GAME OVER ===")]
    [SerializeField] private GameObject           tournamentGameOverPanel;
    [SerializeField] private TournamentGameOverUI tournamentGameOverUI;

    [Header("=== BOTS ===")]
    [Tooltip("Pool of named bots with profile pictures. Add as many as you like — a random, non-repeating set is picked each time bots are needed.")]
    [SerializeField] private BotProfile[] botProfiles;

    [Header("=== DEALER SELECTION ===")]
    [SerializeField] private DealerSelectionUI dealerSelectionUI;

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
        Connecting, WaitingForPlayers, TeamVote, DealerSelection,
        Dealing, TrumpPhase1, TrumpPhase2, DealerDiscard,
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
    private Dictionary<int, int> _seatTricks = new Dictionary<int, int>();
    private const int   WIN_SCORE       = 10;

    // Tournament scoring (only populated when _config.matchType == "tournament")
    private List<ScoreBreakdownEntry> _tournamentBreakdown = new List<ScoreBreakdownEntry>();
    private bool IsTournament => _config != null && _config.matchType == "tournament";

    // Rated matches: always team mode (no vote), and trump selection has no go-alone option.
    private bool IsRatedMatch => _config != null && _config.matchType == "rated";
    private ITrumpSelectionUI ActiveTrumpUI =>
        IsRatedMatch ? (ITrumpSelectionUI)trumpUIForRatedGame : trumpUI;

    // Cached positions for game-over return animation
    private Vector2 _p1InitPos, _p2InitPos, _p3InitPos, _p4InitPos;
    private Vector2 _vsInitPos;

    // Cards
    private Dictionary<int, List<string>> _hands   = new Dictionary<int, List<string>>();
    // Prevents multiple bot coroutines running simultaneously
    private readonly HashSet<int> _botBusySeats = new HashSet<int>();
    private List<string>                  _played  = new List<string>();
    private List<string>                  _deck    = new List<string>();
    // Every card played so far this hand — rejects a duplicate RPC_CardPlayed
    // for a card that's already down (defensive, doesn't require hand visibility).
    private readonly HashSet<string> _cardsPlayedThisHand = new HashSet<string>();

    // Trick turn order (built at start of each trick)
    private List<int>   _trickOrder     = new List<int>();

    // Bots
    private List<int>   _botSeats       = new List<int>();
    private Coroutine   _botFillRoutine;

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

        AudioManager.Instance.StopMusic("Music_1");
        //AudioManager.Instance.PlayMusic("Music_2");
    }

    /// <summary>Called by GameplaySceneBootstrapper on Awake.</summary>
    public void Init(MatchConfig config)
    {
        _config = config;

        // Loading panel — disable game UI, show intro panel
        if (allPlayerUICards  != null) allPlayerUICards.SetActive(false);
        if (firstLoadingPanel != null) firstLoadingPanel.SetActive(true);

        if (firstLoadingPanelBg != null)
        {
            Color c = firstLoadingPanelBg.color;
            c.a = 170f / 255f;
            firstLoadingPanelBg.color = c;
        }

        // Cache card positions before intro animation moves them
        _p1InitPos = player1Card != null ? player1Card.anchoredPosition : Vector2.zero;
        _p2InitPos = player2Card != null ? player2Card.anchoredPosition : Vector2.zero;
        _p3InitPos = player3Card != null ? player3Card.anchoredPosition : Vector2.zero;
        _p4InitPos = player4Card != null ? player4Card.anchoredPosition : Vector2.zero;
        _vsInitPos = vsObject    != null ? vsObject.anchoredPosition    : Vector2.zero;

        // Match title
        if (matchTitleText != null)
            matchTitleText.text = GetMatchTitle(config);

        // Room code — only shown for private/custom matches
        if (roomCodeDisplayText != null)
        {
            bool isCustom = config.matchType == "private" && !string.IsNullOrEmpty(config.roomCode);
            roomCodeDisplayText.gameObject.SetActive(isCustom);
            roomCodeDisplayPanel.SetActive(isCustom);
            if (isCustom)
                roomCodeDisplayText.text = "Room Code:" + config.roomCode;
        }

        // Disable game-over objects until match ends
        if (team1PointObj      != null) team1PointObj.SetActive(false);
        if (team2PointObj      != null) team2PointObj.SetActive(false);
        if (winnerTagTeam1     != null) winnerTagTeam1.SetActive(false);
        if (winnerTagTeam2     != null) winnerTagTeam2.SetActive(false);
        if (spreadFallParticle != null) spreadFallParticle.SetActive(false);
        if (mainMenuButtonObj  != null)
        {
            mainMenuButtonObj.SetActive(false);
            Button btn = mainMenuButtonObj.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(OnMainMenu);
        }
        if (tournamentGameOverPanel != null) tournamentGameOverPanel.SetActive(false);
        _tournamentBreakdown.Clear();

        // Slot 0 = me — fill instantly from config, no need to wait for Photon
        if (loadingNameTexts.Length > 0 && loadingNameTexts[0] != null)
            loadingNameTexts[0].text = config.localPlayer.displayName;
        if (loadingGPTexts.Length > 0 && loadingGPTexts[0] != null)
            loadingGPTexts[0].text = config.localPlayer.pokerChips.ToString("N0") + " PC";
        StartCoroutine(FetchLoadingAvatar(0, config.localPlayer.avatarUrl));

        // Slots 1-3 = others, show placeholder until they join
        for (int i = 1; i < 4; i++)
        {
            if (i < loadingNameTexts.Length && loadingNameTexts[i] != null)
                loadingNameTexts[i].text = "Joining...";
            if (i < loadingGPTexts.Length && loadingGPTexts[i] != null)
                loadingGPTexts[i].text = "0 PC";
        }

        // Tournament matches are already connected & in a Photon room by the time this
        // scene loads (TournamentMatchmaking handled it in the main menu) — reuse the
        // existing room-ready flow instead of connecting/joining again.
        if (_config.matchType == "tournament" && PhotonNetwork.InRoom)
        {
            SetStatus("Starting tournament match...");
            if (PhotonNetwork.IsMasterClient) OnCreatedRoom();
            else                              OnJoinedRoom();
        }
        else
        {
            SetStatus("Connecting...");
            PhotonNetwork.ConnectUsingSettings();
        }
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
        {
            if (_config.isRoomCreator)
                CreatePrivateRoom();
            else
                JoinPrivateRoom();
        }
        else
        {
            JoinPublicRoom();
        }
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

    private void CreatePrivateRoom()
    {
        RoomOptions opts = BuildRoomOptions(visible: false);
        PhotonNetwork.CreateRoom(_config.roomCode, opts, TypedLobby.Default);
        SetStatus($"Creating room {_config.roomCode}...");
    }

    private void JoinPrivateRoom()
    {
        PhotonNetwork.JoinRoom(_config.roomCode);
        SetStatus($"Joining room {_config.roomCode}...");
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[Network] Create room failed: {message}");
        SetStatus("Room already exists — try a different code.");
        notificationManager?.ShowWarningMessage("Room already exists. Ask the host for the correct code.");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[Network] Join room failed: {message}");
        SetStatus("Room not found — check the code and try again.");
        notificationManager?.ShowWarningMessage("Room not found. Check the code and try again.");
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

            int loadingSlot = RemapSeatToPanel(seat);
            UpdateLoadingPanelSlot(loadingSlot, displayName, pokerChips);
            StartCoroutine(FetchLoadingAvatar(loadingSlot, avatarUrl));

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

            // Slot relative to local player — never overwrites slot 0 (me)
            int loadingSlot = RemapSeatToPanel(seat);
            UpdateLoadingPanelSlot(loadingSlot, displayName, pokerChips);
            SetLoadingAvatarSprite(loadingSlot, ResolveBotAvatarSprite(avatarUrl));
        }
        else
        {
            if (!_takenSeats.Contains(seat)) _takenSeats.Add(seat);

            int loadingSlot = RemapSeatToPanel(seat);
            UpdateLoadingPanelSlot(loadingSlot, displayName, pokerChips);
            StartCoroutine(FetchLoadingAvatar(loadingSlot, avatarUrl));
        }

        if (!isLocalPlayer)
        {
            int panelIdx = RemapSeatToPanel(seat);

            playFabManager?.SetupOtherPlayer(
                panelIdx, playFabId, displayName, pokerChips, avatarUrl, isBot);

            if (isBot)
                playFabManager?.GetPlayerPanel(panelIdx)?.SetAvatarSprite(ResolveBotAvatarSprite(avatarUrl));
            else
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
        List<int> pool = ShuffledBotProfileIndices();
        int poolCursor = 0;

        for (int seat = 1; seat <= 3; seat++)
        {
            if (_takenSeats.Contains(seat) || _botSeats.Contains(seat)) continue;

            string botName;
            string avatarUrl = "";

            if (poolCursor < pool.Count)
            {
                int profileIdx = pool[poolCursor++];
                botName   = botProfiles[profileIdx].name;
                avatarUrl = $"bot:{profileIdx}";
            }
            else
            {
                botName = $"Bot {seat}";
            }

            int    botPC = UnityEngine.Random.Range(2000, 8001);
            string botId = $"BOT_SEAT_{seat}";

            _botSeats.Add(seat);
            _seatToName[seat] = botName;

            photonView.RPC(nameof(RPC_SeatConfirmed), RpcTarget.All,
                -(seat + 100),  // negative actor number = bot marker
                seat, botId, botName, avatarUrl, botPC, true);
        }

        if (_takenSeats.Count + _botSeats.Count >= 4)
            StartCoroutine(DelayThen(() =>
                photonView.RPC(nameof(RPC_StartTeamVote), RpcTarget.All), 1f));
    }

    private void ReplaceWithBot(Player leftPlayer)
    {
        object seatObj;
        if (!leftPlayer.CustomProperties.TryGetValue(PROP_SEAT, out seatObj)) return;

        int seat = (int)seatObj;
        int pc   = UnityEngine.Random.Range(2000, 8001);

        string name      = $"Bot {seat}";
        string avatarUrl = "";

        if (botProfiles != null && botProfiles.Length > 0)
        {
            int profileIdx = UnityEngine.Random.Range(0, botProfiles.Length);
            name      = botProfiles[profileIdx].name;
            avatarUrl = $"bot:{profileIdx}";
        }

        if (!_botSeats.Contains(seat)) _botSeats.Add(seat);
        _takenSeats.Remove(seat);
        _seatToName[seat] = name;

        photonView.RPC(nameof(RPC_SeatConfirmed), RpcTarget.All,
            -(seat + 100), seat, $"BOT_SEAT_{seat}", name, avatarUrl, pc, true);
    }

    /// <summary>Returns a shuffled list of botProfiles indices — picking from the front
    /// gives non-repeating bots for as many seats as the pool can cover.</summary>
    private List<int> ShuffledBotProfileIndices()
    {
        List<int> indices = new List<int>();
        if (botProfiles == null) return indices;

        for (int i = 0; i < botProfiles.Length; i++) indices.Add(i);

        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        return indices;
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

        // Rated matches are always team mode — no TeamVoteUI popup, no vote.
        if (IsRatedMatch)
            StartCoroutine(IntroThenAutoTeamMode());
        else
            StartCoroutine(IntroThenVote());
    }

    /// <summary>Rated-match variant of IntroThenVote — plays the same intro slide
    /// animation but skips TeamVoteUI entirely and locks the match to team mode.</summary>
    private IEnumerator IntroThenAutoTeamMode()
    {
        yield return StartCoroutine(PlayIntroAnimation());

        if (PhotonNetwork.IsMasterClient)
            photonView.RPC(nameof(RPC_TeamVoteResult), RpcTarget.All, true);
    }

    private IEnumerator IntroThenVote()
    {
        yield return StartCoroutine(PlayIntroAnimation());

        teamVoteUI?.Show(
            onYes: () => { if (!_localVoteDone) { _localVoteDone = true; CastVote(true);  } },
            onNo:  () => { if (!_localVoteDone) { _localVoteDone = true; CastVote(false); } });
    }

    private IEnumerator PlayIntroAnimation()
    {
        const float slideDuration = 0.45f;
        const float slideDelay    = 0.12f;
        const float slideDistance = 700f;
        const float vsMoveUp      = 250f;
        const float fadeDuration  = 0.45f;

        // Player_1 & Player_3 slide out simultaneously
        player1Card?.DOAnchorPosX(player1Card.anchoredPosition.x - slideDistance, slideDuration)
            .SetEase(Ease.InCubic);
        player3Card?.DOAnchorPosX(player3Card.anchoredPosition.x + slideDistance, slideDuration)
            .SetEase(Ease.InCubic);

        yield return new WaitForSeconds(slideDelay);

        // Player_2 & Player_4 follow
        player2Card?.DOAnchorPosX(player2Card.anchoredPosition.x - slideDistance, slideDuration)
            .SetEase(Ease.InCubic);
        player4Card?.DOAnchorPosX(player4Card.anchoredPosition.x + slideDistance, slideDuration)
            .SetEase(Ease.InCubic);

        yield return new WaitForSeconds(slideDelay);

        // VS shoots upward
        vsObject?.DOAnchorPosY(vsObject.anchoredPosition.y + vsMoveUp, 0.4f)
            .SetEase(Ease.InCubic);

        yield return new WaitForSeconds(slideDuration);

        // Fade panel bg to transparent
        if (firstLoadingPanelBg != null)
            firstLoadingPanelBg.DOFade(0f, fadeDuration);

        yield return new WaitForSeconds(fadeDuration);

        // Swap UI
        if (firstLoadingPanel != null) firstLoadingPanel.SetActive(false);
        if (allPlayerUICards  != null) allPlayerUICards.SetActive(true);
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

        AudioManager.Instance.PlaySFX("Intro_1");

        // Daily task — count what kind of match was started
        DailyTaskManager.Instance?.IncrementTask(TaskType.PlayMatches);
        if      (_config?.matchType == "private")    DailyTaskManager.Instance?.IncrementTask(TaskType.PlayCustomMatch);
        else if (_config?.matchType == "solo")        DailyTaskManager.Instance?.IncrementTask(TaskType.PlaySoloMatch);
        else if (_config?.gameMode  == "freeforall")  DailyTaskManager.Instance?.IncrementTask(TaskType.PlayTournament);

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
        photonView.RPC(nameof(RPC_ResetMatchTotals), RpcTarget.All);
        StartCoroutine(RunDealerSelectionThenStartHand());
    }

    // ─────────────────────────────────────────────────────────────
    //  DEALER SELECTION — deal face-up round robin until Jack of Clubs
    // ─────────────────────────────────────────────────────────────

    private IEnumerator RunDealerSelectionThenStartHand()
    {
        List<string> tempDeck = new List<string>();
        foreach (string type in CARD_TYPES)
            foreach (string suit in CARD_SUITS)
                tempDeck.Add(type + suit);

        for (int i = tempDeck.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (tempDeck[i], tempDeck[j]) = (tempDeck[j], tempDeck[i]);
        }

        List<string> revealSequence = new List<string>();
        int seat        = 0;
        int firstDealer = 0;

        foreach (string card in tempDeck)
        {
            revealSequence.Add(card);
            if (card == "JC") { firstDealer = seat; break; }
            seat = (seat + 1) % 4;
        }

        string sequenceCsv = string.Join(",", revealSequence);
        photonView.RPC(nameof(RPC_DealerSelectionReveal), RpcTarget.All, sequenceCsv, firstDealer);

        // Let the reveal animation finish playing before the first hand deals
        yield return new WaitForSeconds(revealSequence.Count * 0.85f + 1.5f);

        photonView.RPC(nameof(RPC_StartHand), RpcTarget.All, firstDealer);
    }

    [PunRPC]
    private void RPC_DealerSelectionReveal(string sequenceCsv, int firstDealerSeat)
    {
        SetState(GameState.DealerSelection);
        SetStatus("Determining first dealer...");

        List<string> sequence = new List<string>(sequenceCsv.Split(','));
        dealerSelectionUI?.PlayReveal(sequence, firstDealerSeat);
    }

    [PunRPC]
    private void RPC_ResetMatchTotals()
    {
        _seatTricks.Clear();
        for (int i = 0; i < 4; i++) _seatTricks[i] = 0;
        playFabManager?.ResetAllTrickScores();
    }

    // ─────────────────────────────────────────────────────────────
    //  HAND START
    // ─────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_StartHand(int dealerSeat)
    {
        
        // ADD THIS at the top
        _botBusySeats.Clear();
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
        _cardsPlayedThisHand.Clear();

        SetState(GameState.Dealing);
        SetStatus("Dealing cards...");

        playFabManager.SetDealer(RemapSeatToPanel(dealerSeat));
        playFabManager.ClearTrump();
        playFabManager?.ClearSelectingTrump();

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

        // Traditional dealing pattern: first pass 3-2-3-2, second pass 2-3-2-3,
        // starting from the seat left of the dealer. Every seat still ends up
        // with exactly 5 cards — this only changes the order cards are handed out.
        for (int seat = 0; seat < 4; seat++)
            _hands[seat] = new List<string>();

        int[][] passCounts = { new[] { 3, 2, 3, 2 }, new[] { 2, 3, 2, 3 } };
        int startSeat = (_dealerSeat + 1) % 4;
        int deckCursor = 0;

        foreach (int[] counts in passCounts)
        {
            for (int i = 0; i < 4; i++)
            {
                int seat  = (startSeat + i) % 4;
                int count = counts[i];
                for (int c = 0; c < count; c++)
                {
                    _hands[seat].Add(_deck[deckCursor]);
                    deckCursor++;
                }
            }
        }

        string upcard = _deck[deckCursor]; // next card after all 20 dealt

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
        playFabManager?.SetSelectingTrump(RemapSeatToPanel(activeSeat));

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
        bool canOrderUp = isDealer || HasNaturalTrump(_localSeat, upcardSuit);

        ActiveTrumpUI?.ShowPhase1(upcardSuit, canPass: !forced, canOrderUp: canOrderUp,
            onOrderUp: () =>
            {
                // Defensive re-check — the button is disabled when this is false,
                // so this only fires if something bypassed the UI gate.
                if (!isDealer && !HasNaturalTrump(_localSeat, upcardSuit))
                {
                    photonView.RPC(nameof(RPC_IllegalTrumpCall), RpcTarget.All, _localSeat);
                    return;
                }

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

        playFabManager?.ClearSelectingTrump();
        AudioManager.Instance.PlaySFX("TrumpSelected");
        ActiveTrumpUI?.Hide();
        playFabManager.SetTrump(RemapSeatToPanel(pickerSeat), suit);

        if (pickerSeat == _localSeat)
            DailyTaskManager.Instance?.IncrementTask(TaskType.OrderTrump);

        // Round 1 (order-up) means the dealer must pick up the turned-up card
        // and discard one — this differs from Round 2, where no card is picked up.
        string upcard = _deck.Count > 0 ? _deck[0] : "";
        BeginDealerDiscardPhase(upcard);
    }

    // ─────────────────────────────────────────────────────────────
    //  DEALER PICK-UP & DISCARD (Round 1 order-up only)
    // ─────────────────────────────────────────────────────────────

    private void BeginDealerDiscardPhase(string upcard)
    {
        SetState(GameState.DealerDiscard);

        if (_dealerSeat == _localSeat)
        {
            // I'm the dealer — add the upcard to my own hand and pick a discard.
            if (_hands.ContainsKey(_localSeat)) _hands[_localSeat].Add(upcard);
            SetStatus("Pick a card to discard");
            cardManager?.BeginDealerDiscard(upcard);
        }
        else
        {
            cardManager?.HideUpcard();
            SetStatus($"{SeatName(_dealerSeat)} is picking up...");

            if (_botSeats.Contains(_dealerSeat) && PhotonNetwork.IsMasterClient)
                StartCoroutine(BotDealerDiscard(_dealerSeat, upcard));
        }
    }

    /// <summary>Called by EuchreCardManager once the local dealer taps a card to discard.</summary>
    public void LocalDiscardCard(string cardType)
    {
        if (_hands.ContainsKey(_localSeat))
            _hands[_localSeat].Remove(cardType);

        photonView.RPC(nameof(RPC_DealerDiscardComplete), RpcTarget.All);
    }

    private IEnumerator BotDealerDiscard(int dealerSeat, string upcard)
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(1f, 2f));

        if (!_hands.ContainsKey(dealerSeat)) _hands[dealerSeat] = new List<string>();
        _hands[dealerSeat].Add(upcard);

        // Discard the lowest-value card — trump cards always score higher than
        // off-suit junk, so this naturally keeps trump and throws away the rest.
        string worst    = _hands[dealerSeat][0];
        int    worstVal = int.MaxValue;
        foreach (string c in _hands[dealerSeat])
        {
            int val = EvaluateCard(c, _trump);
            if (val < worstVal) { worstVal = val; worst = c; }
        }

        _hands[dealerSeat].Remove(worst);

        photonView.RPC(nameof(RPC_DealerDiscardComplete), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_DealerDiscardComplete()
    {
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
        playFabManager?.SetSelectingTrump(RemapSeatToPanel(activeSeat));

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

        System.Func<char, bool> canCallSuit = suit => isDealer || HasNaturalTrump(_localSeat, suit);

        ActiveTrumpUI?.ShowPhase2(excludedSuit, canPass: !forced, canCallSuit: canCallSuit,
            onSuitPicked: (suit) =>
            {
                // Defensive re-check — the button is disabled when this is false,
                // so this only fires if something bypassed the UI gate.
                if (!isDealer && !HasNaturalTrump(_localSeat, suit))
                {
                    photonView.RPC(nameof(RPC_IllegalTrumpCall), RpcTarget.All, _localSeat);
                    return;
                }

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

        playFabManager?.ClearSelectingTrump();
        AudioManager.Instance.PlaySFX("TrumpSelected");
        ActiveTrumpUI?.Hide();
        playFabManager.SetTrump(RemapSeatToPanel(pickerSeat), _trump);
        cardManager?.HideUpcard();

        if (pickerSeat == _localSeat)
            DailyTaskManager.Instance?.IncrementTask(TaskType.OrderTrump);

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

        if (goAloneSeat == _localSeat)
            DailyTaskManager.Instance?.IncrementTask(TaskType.GoAlone);

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

        if (isMyTurn)
            AudioManager.Instance.PlaySFX("MyTrun");

        turnTimer?.StartTimer(isMyTurn, RemapSeatToPanel(activeSeat));

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
        // Defensive — reject a card that's already been played this hand. Every
        // client can verify this independently since played cards are public
        // information, unlike the sender's remaining hand.
        if (!_cardsPlayedThisHand.Add(cardType))
        {
            Debug.LogWarning($"[Network] Rejected duplicate card play: {cardType} (already played this hand).");
            return;
        }

        turnTimer?.StopTimer();
        AudioManager.Instance.PlaySFX("PlayCard");

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

        string playedCardsStr = string.Join(", ", _played.ConvertAll(CardToReadable));
        string winningCardStr = CardToReadable(_played[winnerTrickIdx]);
        string trumpStr       = SuitName(_trump);
        Debug.Log($"[Trick] {playedCardsStr}\nWin card is: {winningCardStr}. trump was {trumpStr}.");

        photonView.RPC(nameof(RPC_TrickWon), RpcTarget.All, winnerSeat);
    }

    // ─────────────────────────────────────────────────────────────
    //  READABLE CARD NAMES (for debug logging)
    // ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<char, string> RANK_NAMES = new Dictionary<char, string>
        { {'9',"9"}, {'T',"10"}, {'J',"J"}, {'Q',"Q"}, {'K',"K"}, {'A',"A"} };

    private static readonly Dictionary<char, string> SUIT_NAMES = new Dictionary<char, string>
        { {'C',"Club"}, {'D',"Diamond"}, {'H',"Heart"}, {'S',"Spade"} };

    private string CardToReadable(string cardType)
    {
        if (string.IsNullOrEmpty(cardType) || cardType.Length < 2) return cardType;
        string rank = RANK_NAMES.TryGetValue(cardType[0], out string r) ? r : cardType[0].ToString();
        return $"{rank}({SuitName(cardType[1])})";
    }

    private string SuitName(char suit) =>
        SUIT_NAMES.TryGetValue(suit, out string name) ? name : suit.ToString();

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
        _botBusySeats.Clear();
        
        turnTimer?.StopTimer();
        turnIndicator?.SetActiveTurn(-1);

        if (winnerSeat % 2 == 0) _team1Tricks++;
        else _team2Tricks++;

        if (!_seatTricks.ContainsKey(winnerSeat)) _seatTricks[winnerSeat] = 0;
        _seatTricks[winnerSeat]++;

        if (winnerSeat == _localSeat)
        {
            DailyTaskManager.Instance?.IncrementTask(TaskType.WinTricks);
            AddTournamentScore("Trick Won", 10);
        }

        _played.Clear();
        _trickOrder.Clear();

        int winnerPanelIndex = RemapSeatToPanel(winnerSeat);
        int winnerSeatTricks = _seatTricks[winnerSeat];

        cardManager?.FlyCardsToWinner(winnerPanelIndex, onComplete: () =>
        {
            trickDisplay?.UpdateTrickScore(
                _team1Tricks, _team2Tricks,
                _team1Score, _team2Score,
                _isTeamMode);

            playFabManager?.SetPlayerTrickScore(winnerPanelIndex, winnerSeatTricks);

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
                if (_team1Tricks == 5)      pts1 = 5; // all 5 alone = 5 pts (house rule)
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
                if (_team2Tricks == 5)      pts2 = 5; // all 5 alone = 5 pts (house rule)
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

        // Daily task — march win (local team swept all 5 tricks)
        bool localTeamMarch = (_localSeat % 2 == 0 && _team1Tricks == 5)
                           || (_localSeat % 2 == 1 && _team2Tricks == 5);
        if (localTeamMarch)
            DailyTaskManager.Instance?.IncrementTask(TaskType.MarchWin);

        // Tournament scoring — march / go-alone bonuses for the local team's hand result
        bool localTeamCalledTrump = team1Called == (_localSeat % 2 == 0);
        bool localTeamWentAloneAndWon = wentAlone && localTeamCalledTrump &&
            ((_localSeat % 2 == 0 && _team1Tricks == 5) || (_localSeat % 2 == 1 && _team2Tricks == 5));

        if (localTeamWentAloneAndWon)
            AddTournamentScore("Went Alone Bonus", 30);
        else if (localTeamMarch)
            AddTournamentScore("March Bonus", 20);

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
                if (_team1Tricks == 5 && wentAlone) result = "March Alone! +5 pts";
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

        if (localWon)
            DailyTaskManager.Instance?.IncrementTask(TaskType.WinMatches);

        if (localWon)
            AddTournamentScore("Match Win Bonus", 50);

        // ── Economy ────────────────────────────────────────────────
        if (localWon)
            playFabManager?.DistributeWinnings();

        int localTricks = _localSeat % 2 == 0 ? _team1Tricks : _team2Tricks;
        playFabManager?.SaveMatchStats(localWon, localTricks, 5, IsTournament);

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

        if (IsTournament)
            ShowTournamentGameOverPanel();
        else
            ShowMatchOverOnLoadingPanel(winnerTeam);
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
    //  TOURNAMENT SCORING
    // ─────────────────────────────────────────────────────────────

    private void AddTournamentScore(string title, int value)
    {
        if (!IsTournament || value == 0) return;
        _tournamentBreakdown.Add(new ScoreBreakdownEntry(title, value));
    }

    private int SumTournamentBreakdown()
    {
        int sum = 0;
        foreach (ScoreBreakdownEntry entry in _tournamentBreakdown) sum += entry.value;
        return sum;
    }

    /// <summary>Client-side rank estimate shown on the tournament game-over panel,
    /// before the score is actually submitted to PlayFab.</summary>
    private int ComputeOptimisticRank(int myNewTotal)
    {
        TournamentBoardData board = TournamentManager.Instance?.Board;
        string myId = TournamentManager.Instance?.MyEntry?.playFabId;
        if (board?.players == null || string.IsNullOrEmpty(myId)) return 0;

        int rank = 1;
        foreach (TournamentPlayerEntry p in board.players)
        {
            if (p.playFabId == myId) continue;
            if (p.score > myNewTotal) rank++;
        }
        return rank;
    }

    private void ShowTournamentGameOverPanel()
    {
        if (allPlayerUICards != null) allPlayerUICards.SetActive(false);
        if (tournamentGameOverPanel != null) tournamentGameOverPanel.SetActive(true);

        int previousTotal = TournamentManager.Instance?.MyEntry?.score ?? 0;
        int matchScoreSum = SumTournamentBreakdown();
        int newRank = ComputeOptimisticRank(previousTotal + matchScoreSum);

        tournamentGameOverUI?.Populate(_tournamentBreakdown, previousTotal, newRank);
    }

    /// <summary>Wire to the tournament game-over panel's "Continue" / "Main Menu" button.
    /// Pushes the accumulated match score to PlayFab, then returns to the main menu.</summary>
    public void GoToMainMenuFromTournament()
    {
        int matchScoreSum = SumTournamentBreakdown();

        if (TournamentManager.Instance == null)
        {
            PhotonNetwork.LeaveRoom();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            return;
        }

        TournamentManager.Instance.SubmitMatchScore(matchScoreSum, result =>
        {
            if (result == null || !result.success)
                Debug.LogWarning("[Network] Tournament score submission failed — continuing to menu anyway.");

            _tournamentBreakdown.Clear();
            PhotonNetwork.LeaveRoom();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  BOT AI
    // ─────────────────────────────────────────────────────────────

    /*private IEnumerator BotTrumpPhase1(int seat, string upcard, bool forced)
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(2f, 8f));

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
        yield return new WaitForSeconds(UnityEngine.Random.Range(2f, 8f));

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
    */

    private IEnumerator BotTrumpPhase1(int seat, string upcard, bool forced)
    {
        if (_botBusySeats.Contains(seat)) yield break;
        _botBusySeats.Add(seat);

        yield return new WaitForSeconds(UnityEngine.Random.Range(2f, 8f));

        // Validate still this bot's trump turn
        if (_turnSeat != seat)
        {
            _botBusySeats.Remove(seat);
            yield break;
        }

        _botBusySeats.Remove(seat);

        char upcardSuit = upcard.Length > 1 ? upcard[1] : 'S';
        bool isDealer   = seat == _dealerSeat;
        bool hasNatural = isDealer || HasNaturalTrump(seat, upcardSuit);
        bool orderUp    = hasNatural && (forced || UnityEngine.Random.value > 0.4f);

        if (orderUp)
            photonView.RPC(nameof(RPC_TrumpOrdered), RpcTarget.All,
                seat, (int)upcardSuit);
        else
            AdvanceTrumpPhase1(upcard);
    }

    private IEnumerator BotTrumpPhase2(int seat, string excludedSuit, bool forced)
    {
        if (_botBusySeats.Contains(seat)) yield break;
        _botBusySeats.Add(seat);

        yield return new WaitForSeconds(UnityEngine.Random.Range(2f, 8f));

        if (_turnSeat != seat)
        {
            _botBusySeats.Remove(seat);
            yield break;
        }

        _botBusySeats.Remove(seat);

        bool isDealer = seat == _dealerSeat;

        // Only consider suits the bot legally can call — dealer is exempt.
        string chosen = null;
        foreach (string s in new[] { "C", "D", "H", "S" })
        {
            if (s == excludedSuit) continue;
            if (isDealer || HasNaturalTrump(seat, s[0])) { chosen = s; break; }
        }

        bool pick = chosen != null && (forced || UnityEngine.Random.value > 0.4f);

        if (pick)
        {
            photonView.RPC(nameof(RPC_TrumpSuitChosen), RpcTarget.All, seat, chosen);
        }
        else
        {
            int next = (seat + 1) % 4;
            photonView.RPC(nameof(RPC_TrumpPhase2Turn), RpcTarget.All, next, excludedSuit);
        }
    }

    /// <summary>House rule: a natural trump card has a printed suit matching trump —
    /// this includes the right bower (Jack of that suit) but excludes the left bower
    /// (whose printed suit is the same color, not trump).</summary>
    private bool HasNaturalTrump(int seat, char suit)
    {
        if (!_hands.ContainsKey(seat)) return false;
        foreach (string card in _hands[seat])
            if (card.Length > 1 && card[1] == suit) return true;
        return false;
    }

    [PunRPC]
    private void RPC_IllegalTrumpCall(int callerSeat)
    {
        ActiveTrumpUI?.Hide();
        playFabManager?.ClearSelectingTrump();
        turnTimer?.StopTimer();

        int penalizedTeam = callerSeat % 2 == 0 ? 1 : 2;
        int awardedTeam   = penalizedTeam == 1 ? 2 : 1;

        if (awardedTeam == 1) _team1Score += 4;
        else                  _team2Score += 4;

        string summary = $"Illegal trump call by {SeatName(callerSeat)} — opponents awarded 4 points.";
        Debug.LogWarning($"[Network] {summary}");
        notificationManager?.ShowWarningMessagePulse(summary);

        trickDisplay?.UpdateTrickScore(_team1Tricks, _team2Tricks, _team1Score, _team2Score, _isTeamMode);

        if (_team1Score >= WIN_SCORE || _team2Score >= WIN_SCORE)
        {
            EndMatch(summary);
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            int nextDealer = (_dealerSeat + 1) % 4;
            StartCoroutine(DelayThen(() =>
                photonView.RPC(nameof(RPC_StartHand), RpcTarget.All, nextDealer), 2f));
        }
    }

    

    /*private IEnumerator BotPlayCard(int seat)
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(3f, 10f));

        if (!_hands.ContainsKey(seat) || _hands[seat].Count == 0) yield break;

        string card = ChooseBotCard(seat);
        if (string.IsNullOrEmpty(card)) yield break;

        _hands[seat].Remove(card);
        photonView.RPC(nameof(RPC_CardPlayed), RpcTarget.All, seat, card);
    }*/

    private IEnumerator BotPlayCard(int seat)
    {
        // Guard — if this bot seat is already processing, skip
        if (_botBusySeats.Contains(seat))
        {
            Debug.LogWarning($"[Network] Bot seat {seat} already busy — skipping duplicate call.");
            yield break;
        }

        _botBusySeats.Add(seat);

        float delay = UnityEngine.Random.Range(2f, 8f);
        Debug.Log($"[Network] Bot seat {seat} thinking for {delay:F1}s...");
        yield return new WaitForSeconds(delay);

        // Validate it is still this bot's turn after the delay
        if (_turnSeat != seat)
        {
            Debug.LogWarning($"[Network] Bot seat {seat} — turn changed during delay. Aborting.");
            _botBusySeats.Remove(seat);
            yield break;
        }

        // Validate bot still has cards
        if (!_hands.ContainsKey(seat) || _hands[seat].Count == 0)
        {
            Debug.LogWarning($"[Network] Bot seat {seat} has no cards.");
            _botBusySeats.Remove(seat);
            yield break;
        }

        string card = ChooseBotCard(seat);
        if (string.IsNullOrEmpty(card))
        {
            _botBusySeats.Remove(seat);
            yield break;
        }

        _hands[seat].Remove(card);
        _botBusySeats.Remove(seat);

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
    /// Called by GameplayPlayFabManager after it receives fresh local player
    /// data from PlayFab, so the loading panel shows accurate info.
    /// </summary>
    public void UpdateLocalPlayerLoadingSlot(string displayName, string avatarUrl, int pokerChips)
    {
        UpdateLoadingPanelSlot(0, displayName, pokerChips);
        StartCoroutine(FetchLoadingAvatar(0, avatarUrl));
    }

    private void UpdateLoadingPanelSlot(int seat, string displayName, int pokerChips)
    {
        if (seat < 0 || seat >= 4) return;
        if (seat < loadingNameTexts.Length && loadingNameTexts[seat] != null)
            loadingNameTexts[seat].text = displayName;
        if (seat < loadingGPTexts.Length && loadingGPTexts[seat] != null)
            loadingGPTexts[seat].text = pokerChips.ToString("N0") + " PC";
    }

    /// <summary>Bots encode their avatar as "bot:{profileIndex}" instead of a real URL —
    /// this resolves that index to a local Sprite from botProfiles (no network fetch).</summary>
    private Sprite ResolveBotAvatarSprite(string avatarUrl)
    {
        if (string.IsNullOrEmpty(avatarUrl) || !avatarUrl.StartsWith("bot:")) return null;
        if (botProfiles == null) return null;

        if (int.TryParse(avatarUrl.Substring(4), out int idx) && idx >= 0 && idx < botProfiles.Length)
            return botProfiles[idx].avatar;

        return null;
    }

    private void SetLoadingAvatarSprite(int seat, Sprite sprite)
    {
        if (sprite == null) return;
        if (seat < 0 || seat >= loadingAvatarImages.Length) return;
        if (loadingAvatarImages[seat] != null)
            loadingAvatarImages[seat].sprite = sprite;
    }

    private IEnumerator FetchLoadingAvatar(int seat, string url)
    {
        if (seat < 0 || seat >= loadingAvatarImages.Length) yield break;
        if (string.IsNullOrEmpty(url)) yield break;

        Image target = loadingAvatarImages[seat];
        if (target == null) yield break;

        using UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Texture2D tex = DownloadHandlerTexture.GetContent(www);
            target.sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));
        }
    }

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

    //Exit To Main Menu
    public void ExitToMainMenu()
    {
        PhotonNetwork.LeaveRoom();
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
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

    // ─────────────────────────────────────────────────────────────
    //  MATCH OVER — loading panel game over screen
    // ─────────────────────────────────────────────────────────────

    private void ShowMatchOverOnLoadingPanel(int winnerTeam)
    {
        if (allPlayerUICards  != null) allPlayerUICards.SetActive(false);
        if (firstLoadingPanel != null) firstLoadingPanel.SetActive(true);

        // Start bg fully transparent so it fades in
        if (firstLoadingPanelBg != null)
        {
            Color c = firstLoadingPanelBg.color;
            c.a = 0f;
            firstLoadingPanelBg.color = c;
        }

        StartCoroutine(ShowMatchOverCoroutine(winnerTeam));
    }

    private IEnumerator ShowMatchOverCoroutine(int winnerTeam)
    {
        const float slideDuration = 0.45f;
        const float slideDelay    = 0.12f;

        // Fade panel bg back in
        firstLoadingPanelBg?.DOFade(170f / 255f, 0.35f);

        // Slide player cards back to initial positions
        player1Card?.DOAnchorPosX(_p1InitPos.x, slideDuration).SetEase(Ease.OutCubic);
        player3Card?.DOAnchorPosX(_p3InitPos.x, slideDuration).SetEase(Ease.OutCubic);

        yield return new WaitForSeconds(slideDelay);

        player2Card?.DOAnchorPosX(_p2InitPos.x, slideDuration).SetEase(Ease.OutCubic);
        player4Card?.DOAnchorPosX(_p4InitPos.x, slideDuration).SetEase(Ease.OutCubic);

        // VS drops back to initial position
        vsObject?.DOAnchorPosY(_vsInitPos.y, 0.4f).SetEase(Ease.OutCubic);

        yield return new WaitForSeconds(slideDuration + 0.2f);

        // Winner tag (pulsing scale animation)
        GameObject winnerTag = winnerTeam == 1 ? winnerTagTeam1 : winnerTagTeam2;
        if (winnerTag != null)
        {
            winnerTag.SetActive(true);
            winnerTag.transform.localScale = Vector3.one;
            winnerTag.transform.DOScale(1.1f, 0.4f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        // Final scores
        if (team1FinalScoreText != null) team1FinalScoreText.text = _team1Score.ToString();
        if (team2FinalScoreText != null) team2FinalScoreText.text = _team2Score.ToString();
        if (team1PointObj != null) team1PointObj.SetActive(true);
        if (team2PointObj != null) team2PointObj.SetActive(true);

        // Particle effect and main menu button
        if (spreadFallParticle != null) spreadFallParticle.SetActive(true);
        if (mainMenuButtonObj  != null) mainMenuButtonObj.SetActive(true);
    }

    private string GetMatchTitle(MatchConfig config)
    {
        if (config.matchType == "private")    return "Custom Match";
        if (config.matchType == "solo")       return "Casual Game";
        if (config.matchType == "tournament") return "Tournament";
        if (config.gameMode  == "freeforall") return "Tournament";
        return "Rated Match";
    }
}
