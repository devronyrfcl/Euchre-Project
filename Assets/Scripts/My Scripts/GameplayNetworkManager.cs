using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Full Photon PUN2 network manager for Euchre gameplay.
/// Handles: connection, room join/create, seat assignment,
///          dealing, trump selection, trick taking, scoring,
///          team vote, go-alone, bot fill, match economy callbacks.
///
/// Communicates to GameplayPlayFabManager for UI sync.
/// Communicates to EuchreCardManager for card spawning/animation.
/// Communicates to TrumpSelectionUI for trump popup.
/// Communicates to TeamVoteUI for team vote popup.
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
    [SerializeField] private EuchreCardManager cardManager;
    [SerializeField] private TrumpSelectionUI trumpUI;
    [SerializeField] private TeamVoteUI teamVoteUI;
    [SerializeField] private GameOverUI gameOverUI;

    [Header("=== STATUS TEXT ===")]
    [SerializeField] private TMPro.TMP_Text statusText;

    // ─────────────────────────────────────────────────────────────
    //  Photon Custom Property Keys
    // ─────────────────────────────────────────────────────────────

    private const string PROP_PLAYFAB_ID = "PFId";
    private const string PROP_DISPLAY_NAME = "Name";
    private const string PROP_AVATAR_URL = "Avt";
    private const string PROP_POKER_CHIPS = "PC";
    private const string PROP_SEAT = "Seat";
    private const string PROP_IS_BOT = "Bot";

    // Room properties
    private const string ROOM_DEALER = "Dealer";
    private const string ROOM_TRUMP = "Trump";
    private const string ROOM_TRUMP_PICKER = "TrumpPicker";
    private const string ROOM_TEAM_MODE = "TeamMode";
    private const string ROOM_GAME_STATE = "State";

    // ─────────────────────────────────────────────────────────────
    //  Game State Enum
    // ─────────────────────────────────────────────────────────────

    public enum GameState
    {
        Connecting,
        WaitingForPlayers,
        TeamVote,
        Dealing,
        TrumpPhase1,
        TrumpPhase2,
        Playing,
        ScoringTrick,
        HandOver,
        MatchOver
    }

    // ─────────────────────────────────────────────────────────────
    //  Runtime State
    // ─────────────────────────────────────────────────────────────

    private MatchConfig _config;

    private GameState _state = GameState.Connecting;
    private int _localSeat = 0;
    private int _dealerSeat = 0;
    private int _turnSeat = 0;
    private char _trump = ' ';
    private int _trumpPickerSeat = -1;
    private int _cycled = 0;
    private int _skipSeat = -1;   // go-alone: partner seat to skip

    private bool _isTeamMode = false;
    private int _teamVoteYes = 0;
    private int _teamVoteNo = 0;
    private bool _localVoteDone = false;

    private int _team1Score = 0;
    private int _team2Score = 0;
    private int _team1Tricks = 0;
    private int _team2Tricks = 0;

    private const int WIN_SCORE = 10;

    // Cards
    private readonly List<string> _deck = new List<string>();
    private readonly Dictionary<int, List<string>> _hands = new Dictionary<int, List<string>>();
    private readonly List<string> _played = new List<string>();

    // Seats → Photon ActorNumber map (for non-host clients)
    private readonly Dictionary<int, int> _seatToActor = new Dictionary<int, int>();

    // Bot
    private readonly List<int> _botSeats = new List<int>();
    private Coroutine _botFillCoroutine;

    private static readonly string[] BOT_NAMES = { "Bot Alpha", "Bot Beta", "Bot Gamma" };

    private static readonly string[] TYPES = { "9", "T", "J", "Q", "K", "A" };
    private static readonly string[] SUITS = { "D", "H", "S", "C" };

    private static readonly Dictionary<char, char> ALT_SUITS = new Dictionary<char, char>
        { {'S','C'}, {'C','S'}, {'H','D'}, {'D','H'} };

    // ─────────────────────────────────────────────────────────────
    //  Init (called by GameplaySceneBootstrapper)
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Init(MatchConfig config)
    {
        _config = config;
        SetStatus("Connecting...");
        PhotonNetwork.ConnectUsingSettings();
    }

    // ─────────────────────────────────────────────────────────────
    //  PHOTON CONNECTION CALLBACKS
    // ─────────────────────────────────────────────────────────────

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Network] Connected to Master.");

        // Set local player custom properties before joining
        Hashtable props = new Hashtable
        {
            { PROP_PLAYFAB_ID,   _config.localPlayer.playFabId   },
            { PROP_DISPLAY_NAME, _config.localPlayer.displayName  },
            { PROP_AVATAR_URL,   _config.localPlayer.avatarUrl    },
            { PROP_POKER_CHIPS,  _config.localPlayer.pokerChips   },
            { PROP_IS_BOT,       false                            }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);

        if (_config.matchType == "private" && !string.IsNullOrEmpty(_config.roomCode))
            JoinPrivateRoom();
        else
            JoinPublicRoom();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[Network] Disconnected: {cause}");
        SetStatus("Disconnected. Reconnecting...");
        StartCoroutine(ReconnectDelay());
    }

    private IEnumerator ReconnectDelay()
    {
        yield return new WaitForSeconds(2f);
        PhotonNetwork.ConnectUsingSettings();
    }

    // ─────────────────────────────────────────────────────────────
    //  ROOM JOIN / CREATE
    // ─────────────────────────────────────────────────────────────

    private void JoinPublicRoom()
    {
        PhotonNetwork.JoinRandomRoom();
    }

    private void JoinPrivateRoom()
    {
        RoomOptions opts = new RoomOptions
        {
            IsVisible = false,
            MaxPlayers = 4,
            CustomRoomPropertiesForLobby = new string[]
                { ROOM_DEALER, ROOM_TRUMP, ROOM_TRUMP_PICKER, ROOM_TEAM_MODE, ROOM_GAME_STATE }
        };

        PhotonNetwork.JoinOrCreateRoom(_config.roomCode, opts, TypedLobby.Default);
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        // No public room available — create one
        string id = "";
        for (int i = 0; i < 8; i++) id += Random.Range(0, 9).ToString();

        RoomOptions opts = new RoomOptions
        {
            IsVisible = true,
            MaxPlayers = 4,
            CustomRoomPropertiesForLobby = new string[]
                { ROOM_DEALER, ROOM_TRUMP, ROOM_TRUMP_PICKER, ROOM_TEAM_MODE, ROOM_GAME_STATE }
        };

        PhotonNetwork.JoinOrCreateRoom(id, opts, TypedLobby.Default);
    }

    public override void OnCreatedRoom()
    {
        Debug.Log("[Network] Room created. I am host.");
        AssignSeat(0); // host always seat 0

        SetStatus("Waiting for players (1/4)...");
        SetState(GameState.WaitingForPlayers);

        if (_config.allowBots)
            _botFillCoroutine = StartCoroutine(BotFillCountdown());
    }

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.IsMasterClient) return; // handled in OnCreatedRoom

        Debug.Log("[Network] Joined room.");
        SetStatus("Waiting for players...");
        SetState(GameState.WaitingForPlayers);

        // Request seat assignment from host
        photonView.RPC(nameof(RPC_RequestSeat), RpcTarget.MasterClient,
            PhotonNetwork.LocalPlayer.ActorNumber,
            _config.localPlayer.playFabId,
            _config.localPlayer.displayName,
            _config.localPlayer.avatarUrl,
            _config.localPlayer.pokerChips);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.LogWarning($"[Network] Player left: {otherPlayer.NickName}");

        if (_config.allowBots && _state != GameState.MatchOver)
            FillLeftSeatWithBot(otherPlayer);
    }

    // ─────────────────────────────────────────────────────────────
    //  SEAT ASSIGNMENT
    // ─────────────────────────────────────────────────────────────

    private List<int> _takenSeats = new List<int> { 0 }; // 0 reserved for host

    [PunRPC]
    private void RPC_RequestSeat(int actorNumber, string playFabId,
                                  string displayName, string avatarUrl, int pokerChips)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // Find first free seat
        int seat = -1;
        for (int i = 1; i <= 3; i++)
        {
            if (!_takenSeats.Contains(i)) { seat = i; break; }
        }

        if (seat == -1)
        {
            Debug.LogWarning("[Network] No free seats.");
            return;
        }

        _takenSeats.Add(seat);
        _seatToActor[seat] = actorNumber;

        // Tell everyone about this player
        photonView.RPC(nameof(RPC_SeatAssigned), RpcTarget.All,
            actorNumber, seat, playFabId, displayName, avatarUrl, pokerChips, false);

        // If room is full, start game
        if (_takenSeats.Count >= 4)
        {
            if (_botFillCoroutine != null) StopCoroutine(_botFillCoroutine);
            StartTeamVoteOrGame();
        }
        else
        {
            SetStatus($"Waiting for players ({_takenSeats.Count}/4)...");
        }
    }

    [PunRPC]
    private void RPC_SeatAssigned(int actorNumber, int seat,
                                   string playFabId, string displayName,
                                   string avatarUrl, int pokerChips, bool isBot)
    {
        _seatToActor[seat] = actorNumber;

        bool isLocal = actorNumber == PhotonNetwork.LocalPlayer.ActorNumber;

        if (isLocal)
        {
            _localSeat = seat;
            AssignSeat(seat);
        }

        // Update PlayFab panel
        if (seat == 0 && isLocal) return; // local already set up in PlayFabManager.Start()

        // Remap seat to panel index relative to local seat
        int panelIndex = RemapSeatToPanel(seat);

        playFabManager.SetupOtherPlayer(
            panelIndex, playFabId, displayName, pokerChips, avatarUrl, isBot);

        int count = _takenSeats.Count + _botSeats.Count;
        SetStatus($"Waiting for players ({count}/4)...");
    }

    private void AssignSeat(int seat)
    {
        _localSeat = seat;
        Hashtable prop = new Hashtable { { PROP_SEAT, seat } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(prop);
        Debug.Log($"[Network] Local seat assigned: {seat}");
    }

    /// <summary>
    /// Remaps absolute seat to panel index relative to local player.
    /// Local player is always panel 0.
    /// Partner (seat+2) is panel 1.
    /// Left opp (seat+1) is panel 2.
    /// Right opp (seat+3) is panel 3.
    /// </summary>
    private int RemapSeatToPanel(int absoluteSeat)
    {
        if (absoluteSeat == _localSeat) return 0;

        int diff = (absoluteSeat - _localSeat + 4) % 4;
        return diff switch
        {
            2 => 1, // partner
            1 => 2, // left opponent
            3 => 3, // right opponent
            _ => 0
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  BOT FILL
    // ─────────────────────────────────────────────────────────────

    private IEnumerator BotFillCountdown()
    {
        float elapsed = 0f;
        float wait = _config.botFillDelay;

        while (elapsed < wait && _takenSeats.Count < 4)
        {
            elapsed += Time.deltaTime;
            int remaining = Mathf.CeilToInt(wait - elapsed);
            SetStatus($"Waiting for players ({_takenSeats.Count}/4) — {remaining}s");
            yield return null;
        }

        if (_takenSeats.Count < 4 && PhotonNetwork.IsMasterClient)
            FillRemainingWithBots();
    }

    private void FillRemainingWithBots()
    {
        int botIdx = 0;
        for (int seat = 1; seat <= 3; seat++)
        {
            if (_takenSeats.Contains(seat)) continue;

            string botName = botIdx < BOT_NAMES.Length ? BOT_NAMES[botIdx] : $"Bot {botIdx + 1}";
            int botPC = Random.Range(2000, 8001);
            string botId = $"BOT_{seat}";
            botIdx++;

            _takenSeats.Add(seat);
            _botSeats.Add(seat);

            photonView.RPC(nameof(RPC_SeatAssigned), RpcTarget.All,
                -seat, seat, botId, botName, "", botPC, true);
        }

        StartTeamVoteOrGame();
    }

    private void FillLeftSeatWithBot(Player leftPlayer)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        object seatObj;
        if (!leftPlayer.CustomProperties.TryGetValue(PROP_SEAT, out seatObj)) return;

        int seat = (int)seatObj;
        string botName = $"Bot {seat}";
        int botPC = Random.Range(2000, 8001);

        if (!_botSeats.Contains(seat)) _botSeats.Add(seat);

        photonView.RPC(nameof(RPC_SeatAssigned), RpcTarget.All,
            -seat, seat, $"BOT_{seat}", botName, "", botPC, true);
    }

    // ─────────────────────────────────────────────────────────────
    //  TEAM VOTE
    // ─────────────────────────────────────────────────────────────

    private void StartTeamVoteOrGame()
    {
        photonView.RPC(nameof(RPC_StartTeamVote), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_StartTeamVote()
    {
        SetState(GameState.TeamVote);
        _teamVoteYes = 0;
        _teamVoteNo = 0;
        _localVoteDone = false;

        if (teamVoteUI != null)
            teamVoteUI.Show(OnTeamVoteYes, OnTeamVoteNo);
    }

    private void OnTeamVoteYes()
    {
        if (_localVoteDone) return;
        _localVoteDone = true;
        photonView.RPC(nameof(RPC_TeamVote), RpcTarget.All, true);
    }

    private void OnTeamVoteNo()
    {
        if (_localVoteDone) return;
        _localVoteDone = true;
        photonView.RPC(nameof(RPC_TeamVote), RpcTarget.All, false);
    }

    [PunRPC]
    private void RPC_TeamVote(bool yes)
    {
        if (yes) _teamVoteYes++;
        else _teamVoteNo++;

        int totalHumans = 4 - _botSeats.Count;
        int totalVotes = _teamVoteYes + _teamVoteNo;

        if (totalVotes < totalHumans) return; // still waiting

        // Bots always vote yes (they follow whatever mode)
        _teamVoteYes += _botSeats.Count;

        // ALL must vote yes for team mode
        _isTeamMode = (_teamVoteYes == 4);

        photonView.RPC(nameof(RPC_TeamVoteResult), RpcTarget.All, _isTeamMode);
    }

    [PunRPC]
    private void RPC_TeamVoteResult(bool teamMode)
    {
        _isTeamMode = teamMode;

        if (teamVoteUI != null) teamVoteUI.Hide();

        playFabManager.SetTeamMode(teamMode);

        Debug.Log($"[Network] Team mode: {teamMode}");

        // Deduct bet from local player
        playFabManager.DeductBetOnMatchStart();

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
        _dealerSeat = Random.Range(0, 4);
        photonView.RPC(nameof(RPC_StartHand), RpcTarget.All, _dealerSeat);
    }

    [PunRPC]
    private void RPC_StartHand(int dealerSeat)
    {
        _dealerSeat = dealerSeat;
        _team1Tricks = 0;
        _team2Tricks = 0;
        _trump = ' ';
        _trumpPickerSeat = -1;
        _cycled = 0;
        _skipSeat = -1;
        _played.Clear();
        _hands.Clear();

        playFabManager.SetDealer(RemapSeatToPanel(dealerSeat));
        playFabManager.ClearTrump();

        SetState(GameState.Dealing);
        SetStatus("Dealing cards...");

        if (PhotonNetwork.IsMasterClient)
            DealCards();
    }

    // ─────────────────────────────────────────────────────────────
    //  DEALING
    // ─────────────────────────────────────────────────────────────

    private void DealCards()
    {
        // Build and shuffle deck
        _deck.Clear();
        foreach (string type in TYPES)
            foreach (string suit in SUITS)
                _deck.Add(type + suit);

        // Shuffle
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            string tmp = _deck[i];
            _deck[i] = _deck[j];
            _deck[j] = tmp;
        }

        // Deal 5 cards to each seat
        for (int seat = 0; seat < 4; seat++)
        {
            _hands[seat] = new List<string>();
            for (int c = 0; c < 5; c++)
            {
                _hands[seat].Add(_deck[0]);
                _deck.RemoveAt(0);
            }
        }

        // Upcard
        string upcard = _deck[0];

        // Send each player their hand + upcard
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            object seatObj;
            if (!player.CustomProperties.TryGetValue(PROP_SEAT, out seatObj)) continue;

            int seat = (int)seatObj;
            string handStr = string.Join(",", _hands[seat]);

            photonView.RPC(nameof(RPC_ReceiveHand), player, handStr, upcard, _dealerSeat);
        }

        // Send bot hands to all (master handles bots)
        foreach (int botSeat in _botSeats)
        {
            string handStr = string.Join(",", _hands[botSeat]);
            photonView.RPC(nameof(RPC_ReceiveBotHand), RpcTarget.MasterClient,
                botSeat, handStr, upcard);
        }
    }

    [PunRPC]
    private void RPC_ReceiveHand(string handStr, string upcard, int dealerSeat)
    {
        List<string> hand = new List<string>(handStr.Split(','));
        _hands[_localSeat] = hand;

        cardManager.AnimateDeal(hand, upcard, dealerSeat, _localSeat);

        SetState(GameState.TrumpPhase1);
    }

    [PunRPC]
    private void RPC_ReceiveBotHand(int botSeat, string handStr, string upcard)
    {
        _hands[botSeat] = new List<string>(handStr.Split(','));
    }

    // ─────────────────────────────────────────────────────────────
    //  TRUMP PHASE 1 — Order Up / Pass
    // ─────────────────────────────────────────────────────────────

    public void BeginTrumpPhase1()
    {
        _cycled = 0;
        int firstSeat = (_dealerSeat + 1) % 4;
        photonView.RPC(nameof(RPC_TrumpPhase1Turn), RpcTarget.All, firstSeat, _deck[0]);
    }

    [PunRPC]
    private void RPC_TrumpPhase1Turn(int activeSeat, string upcard)
    {
        SetState(GameState.TrumpPhase1);
        _turnSeat = activeSeat;

        bool isDealer = activeSeat == _dealerSeat;
        bool isMyTurn = activeSeat == _localSeat;
        bool isDealerForced = _cycled == 0 && isDealer;

        SetStatus(isMyTurn
            ? "Your turn — Order up or Pass?"
            : $"{GetPlayerName(activeSeat)}'s turn...");

        // Bot
        if (_botSeats.Contains(activeSeat) && PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(BotTrumpPhase1Decision(activeSeat, upcard, isDealerForced));
            return;
        }

        if (!isMyTurn) return;

        // Show trump popup phase 1
        char upcardSuit = upcard[1];
        trumpUI.ShowPhase1(upcardSuit, canPass: !isDealerForced,
            onOrderUp: () => LocalOrderUp(),
            onPass: () => LocalPassPhase1(upcard));
    }

    private void LocalOrderUp()
    {
        trumpUI.Hide();
        photonView.RPC(nameof(RPC_TrumpOrdered), RpcTarget.All, _localSeat, _deck[0][1]);
    }

    private void LocalPassPhase1(string upcard)
    {
        trumpUI.Hide();
        NextTrumpPhase1Turn(upcard);
    }

    private void NextTrumpPhase1Turn(string upcard)
    {
        int next = (_turnSeat + 1) % 4;
        if (next == (_dealerSeat + 1) % 4) _cycled++;

        if (_cycled >= 1)
        {
            // Everyone passed phase 1 — move to phase 2
            photonView.RPC(nameof(RPC_BeginTrumpPhase2), RpcTarget.All);
            return;
        }

        photonView.RPC(nameof(RPC_TrumpPhase1Turn), RpcTarget.All, next, upcard);
    }

    [PunRPC]
    private void RPC_TrumpOrdered(int pickerSeat, char suit)
    {
        _trump = suit;
        _trumpPickerSeat = pickerSeat;

        playFabManager.SetTrump(RemapSeatToPanel(pickerSeat), suit);
        SetState(GameState.Playing);

        // Dealer picks up upcard
        if (_localSeat == _dealerSeat)
            cardManager.ShowDealerPickup(_deck[0]);

        if (PhotonNetwork.IsMasterClient)
            StartCoroutine(DelayThenBeginRounds());
    }

    // ─────────────────────────────────────────────────────────────
    //  TRUMP PHASE 2 — Choose Suit
    // ─────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_BeginTrumpPhase2()
    {
        SetState(GameState.TrumpPhase2);
        _cycled = 0;
        int firstSeat = (_dealerSeat + 1) % 4;
        photonView.RPC(nameof(RPC_TrumpPhase2Turn), RpcTarget.All,
            firstSeat, _deck[0][1].ToString());
    }

    [PunRPC]
    private void RPC_TrumpPhase2Turn(int activeSeat, string excludedSuit)
    {
        _turnSeat = activeSeat;

        bool isDealer = activeSeat == _dealerSeat;
        bool isMyTurn = activeSeat == _localSeat;
        bool isDealerForced = isDealer; // dealer must pick in phase 2

        SetStatus(isMyTurn
            ? "Choose trump suit or Pass"
            : $"{GetPlayerName(activeSeat)} is choosing...");

        // Bot
        if (_botSeats.Contains(activeSeat) && PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(BotTrumpPhase2Decision(activeSeat, excludedSuit, isDealerForced));
            return;
        }

        if (!isMyTurn) return;

        trumpUI.ShowPhase2(excludedSuit, canPass: !isDealerForced,
            onSuitPicked: (suit) =>
            {
                trumpUI.Hide();
                photonView.RPC(nameof(RPC_TrumpPicked), RpcTarget.All, _localSeat, suit.ToString());
            },
            onPass: () =>
            {
                trumpUI.Hide();
                int next = (_turnSeat + 1) % 4;
                photonView.RPC(nameof(RPC_TrumpPhase2Turn), RpcTarget.All,
                    next, excludedSuit);
            });
    }

    [PunRPC]
    private void RPC_TrumpPicked(int pickerSeat, string suitStr)
    {
        _trump = suitStr[0];
        _trumpPickerSeat = pickerSeat;

        playFabManager.SetTrump(RemapSeatToPanel(pickerSeat), _trump);
        SetState(GameState.Playing);

        if (PhotonNetwork.IsMasterClient)
            StartCoroutine(DelayThenBeginRounds());
    }

    // ─────────────────────────────────────────────────────────────
    //  GO ALONE
    // ─────────────────────────────────────────────────────────────

    public void LocalGoAlone()
    {
        // Partner is seat opposite (seat+2)%4
        int partnerSeat = (_localSeat + 2) % 4;
        photonView.RPC(nameof(RPC_GoAlone), RpcTarget.All, _localSeat, partnerSeat);
    }

    [PunRPC]
    private void RPC_GoAlone(int goAloneSeat, int skipSeat)
    {
        _skipSeat = skipSeat;
        playFabManager.SetGoAlone(RemapSeatToPanel(goAloneSeat));
        Debug.Log($"[Network] Go alone: seat {goAloneSeat}, skip seat {skipSeat}");
    }

    // ─────────────────────────────────────────────────────────────
    //  TRICK PLAYING
    // ─────────────────────────────────────────────────────────────

    private IEnumerator DelayThenBeginRounds()
    {
        yield return new WaitForSeconds(1f);
        int firstTurn = (_dealerSeat + 1) % 4;
        photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, firstTurn);
    }

    [PunRPC]
    private void RPC_BeginTurn(int activeSeat)
    {
        _turnSeat = activeSeat;

        // Skip partner if going alone
        if (_skipSeat != -1 && activeSeat == _skipSeat)
        {
            int next = (_turnSeat + 1) % 4;
            if (next == _skipSeat) next = (_next + 1) % 4; // edge case
            photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, next);
            return;
        }

        SetStatus(activeSeat == _localSeat
            ? "Your turn — play a card"
            : $"{GetPlayerName(activeSeat)}'s turn...");

        // Bot
        if (_botSeats.Contains(activeSeat) && PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(BotPlayCard(activeSeat));
            return;
        }

        if (activeSeat != _localSeat) return;

        // Enable cards for local player
        cardManager.EnableCardSelection(true, _played, _trump, ALT_SUITS);
    }

    /// <summary>Called by EuchreCardManager when local player selects and confirms a card.</summary>
    public void LocalPlayCard(string cardType)
    {
        if (_turnSeat != _localSeat) return;

        cardManager.EnableCardSelection(false, null, ' ', null);

        if (_hands.ContainsKey(_localSeat))
            _hands[_localSeat].Remove(cardType);

        photonView.RPC(nameof(RPC_CardPlayed), RpcTarget.All, _localSeat, cardType);
    }

    [PunRPC]
    private void RPC_CardPlayed(int fromSeat, string cardType)
    {
        _played.Add(cardType);

        // Animate card to table
        int panelIndex = RemapSeatToPanel(fromSeat);
        cardManager.AnimateCardToTable(panelIndex, cardType);

        int numPlayers = _skipSeat != -1 ? 3 : 4;

        if (_played.Count >= numPlayers)
        {
            SetState(GameState.ScoringTrick);
            StartCoroutine(DelayThenScoreTrick());
        }
        else
        {
            int next = (fromSeat + 1) % 4;
            if (next == _skipSeat) next = (next + 1) % 4;
            photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, next);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  TRICK SCORING
    // ─────────────────────────────────────────────────────────────

    private IEnumerator DelayThenScoreTrick()
    {
        yield return new WaitForSeconds(1.5f);
        ScoreTrick();
    }

    private void ScoreTrick()
    {
        char leadSuit = _played[0][1];
        if (_played[0][0] == 'J' && ALT_SUITS.ContainsKey(_trump) &&
            _played[0][1] == ALT_SUITS[_trump])
            leadSuit = _trump;

        int winnerIdx = 0;
        int bestVal = 0;

        // Reconstruct turn order for this trick
        List<int> trickOrder = GetTrickOrder();

        for (int i = 0; i < _played.Count; i++)
        {
            int val = EvaluateCard(_played[i], leadSuit);
            if (val > bestVal)
            {
                bestVal = val;
                winnerIdx = i;
            }
        }

        int winnerSeat = trickOrder[winnerIdx];

        if (_isTeamMode)
        {
            if (winnerSeat % 2 == 0) _team1Tricks++;
            else _team2Tricks++;
        }
        else
        {
            // Free-for-all: track per-seat
            // For simplicity, team1 = even seats, team2 = odd seats
            if (winnerSeat % 2 == 0) _team1Tricks++;
            else _team2Tricks++;
        }

        _played.Clear();
        cardManager.ClearTableCards();

        int totalTricks = _team1Tricks + _team2Tricks;
        if (totalTricks >= 5)
            photonView.RPC(nameof(RPC_ScoreHand), RpcTarget.All,
                _team1Tricks, _team2Tricks, winnerSeat);
        else
            photonView.RPC(nameof(RPC_BeginTurn), RpcTarget.All, winnerSeat);
    }

    private List<int> GetTrickOrder()
    {
        List<int> order = new List<int>();
        int startSeat = (_turnSeat - _played.Count + 1 + 4) % 4;

        for (int i = 0; i < _played.Count; i++)
        {
            int s = (startSeat + i) % 4;
            if (s != _skipSeat) order.Add(s);
        }
        return order;
    }

    [PunRPC]
    private void RPC_ScoreHand(int t1Tricks, int t2Tricks, int trickWinnerSeat)
    {
        _team1Tricks = t1Tricks;
        _team2Tricks = t2Tricks;

        int pts1 = 0, pts2 = 0;
        bool wentAlone = _skipSeat != -1;

        if (_trumpPickerSeat % 2 == 0)
        {
            if (_team1Tricks == 5) pts1 = wentAlone ? 4 : 2;
            else if (_team1Tricks >= 3) pts1 = 1;
            else pts2 = 2;
        }
        else
        {
            if (_team2Tricks == 5) pts2 = wentAlone ? 4 : 2;
            else if (_team2Tricks >= 3) pts2 = 1;
            else pts1 = 2;
        }

        _team1Score += pts1;
        _team2Score += pts2;

        SetState(GameState.HandOver);

        if (_team1Score >= WIN_SCORE || _team2Score >= WIN_SCORE)
        {
            EndMatch();
            return;
        }

        // New hand — rotate dealer
        if (PhotonNetwork.IsMasterClient)
        {
            int newDealer = (_dealerSeat + 1) % 4;
            photonView.RPC(nameof(RPC_StartHand), RpcTarget.All, newDealer);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  MATCH END
    // ─────────────────────────────────────────────────────────────

    private void EndMatch()
    {
        SetState(GameState.MatchOver);

        bool localWon = (_localSeat % 2 == 0 && _team1Score >= WIN_SCORE)
                     || (_localSeat % 2 == 1 && _team2Score >= WIN_SCORE);

        // PlayFab: distribute winnings and save stats
        if (localWon)
            playFabManager.DistributeWinnings();

        playFabManager.SaveMatchStats(
            won: localWon,
            tricksWon: _localSeat % 2 == 0 ? _team1Tricks : _team2Tricks,
            totalTricks: 5);

        int winnerTeam = _team1Score >= WIN_SCORE ? 1 : 2;

        if (gameOverUI != null)
            gameOverUI.Show(winnerTeam, _team1Score, _team2Score,
                onRematch: OnRematch,
                onMainMenu: OnMainMenu);
    }

    private void OnRematch()
    {
        // Deduct bet again
        playFabManager.DeductBetOnMatchStart();

        if (PhotonNetwork.IsMasterClient)
            StartNewMatch();
    }

    private void OnMainMenu()
    {
        PhotonNetwork.LeaveRoom();
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    // ─────────────────────────────────────────────────────────────
    //  BOT AI
    // ─────────────────────────────────────────────────────────────

    private IEnumerator BotTrumpPhase1Decision(int seat, string upcard, bool forced)
    {
        yield return new WaitForSeconds(Random.Range(0.8f, 1.5f));

        bool orderUp = forced || Random.value > 0.4f;

        if (orderUp)
            photonView.RPC(nameof(RPC_TrumpOrdered), RpcTarget.All, seat, upcard[1]);
        else
            NextTrumpPhase1Turn(upcard);
    }

    private IEnumerator BotTrumpPhase2Decision(int seat, string excludedSuit, bool forced)
    {
        yield return new WaitForSeconds(Random.Range(0.8f, 1.5f));

        bool pick = forced || Random.value > 0.4f;

        if (pick)
        {
            // Pick random suit except excluded
            string[] options = { "C", "D", "H", "S" };
            string chosen = "C";
            foreach (string s in options)
            {
                if (s != excludedSuit) { chosen = s; break; }
            }
            photonView.RPC(nameof(RPC_TrumpPicked), RpcTarget.All, seat, chosen);
        }
        else
        {
            int next = (seat + 1) % 4;
            photonView.RPC(nameof(RPC_TrumpPhase2Turn), RpcTarget.All, next, excludedSuit);
        }
    }

    private IEnumerator BotPlayCard(int seat)
    {
        yield return new WaitForSeconds(Random.Range(0.8f, 1.5f));

        if (!_hands.ContainsKey(seat) || _hands[seat].Count == 0) yield break;

        // Simple bot: play first valid card
        string card = _hands[seat][0];

        // Try to follow suit
        if (_played.Count > 0)
        {
            char leadSuit = _played[0][1];
            foreach (string c in _hands[seat])
            {
                if (c[1] == leadSuit) { card = c; break; }
            }
        }

        _hands[seat].Remove(card);

        photonView.RPC(nameof(RPC_CardPlayed), RpcTarget.All, seat, card);
    }

    // ─────────────────────────────────────────────────────────────
    //  CARD EVALUATION
    // ─────────────────────────────────────────────────────────────

    private int EvaluateCard(string cardType, char leadSuit)
    {
        char val = cardType[0];
        char suit = cardType[1];

        bool isRight = val == 'J' && suit == _trump;
        bool isLeft = val == 'J' && ALT_SUITS.ContainsKey(_trump) && suit == ALT_SUITS[_trump];

        if (isRight) return 15;
        if (isLeft) return 14;

        if (suit == _trump)
            return val switch { 'A' => 13, 'K' => 12, 'Q' => 11, 'T' => 10, '9' => 9, _ => 0 };

        if (suit == leadSuit)
            return val switch { 'A' => 7, 'K' => 6, 'Q' => 5, 'J' => 4, 'T' => 3, '9' => 2, _ => 0 };

        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    private string GetPlayerName(int seat)
    {
        GameplayPlayerData panel = playFabManager.GetPlayerPanel(RemapSeatToPanel(seat));
        return panel != null ? panel.DisplayName : $"Player {seat + 1}";
    }

    private void SetState(GameState state)
    {
        _state = state;
        Debug.Log($"[Network] State → {state}");
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log($"[Network] Status: {msg}");
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC ACCESSORS (for UI scripts)
    // ─────────────────────────────────────────────────────────────

    public int LocalSeat => _localSeat;
    public int TurnSeat => _turnSeat;
    public char Trump => _trump;
    public bool IsTeamMode => _isTeamMode;
    public int Team1Score => _team1Score;
    public int Team2Score => _team2Score;
    public GameState State => _state;

    public List<string> GetLocalHand()
    {
        return _hands.ContainsKey(_localSeat)
            ? new List<string>(_hands[_localSeat])
            : new List<string>();
    }
}