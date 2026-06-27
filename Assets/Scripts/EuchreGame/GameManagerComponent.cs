// ─── GameManagerComponent.cs ───────────────────────────────────
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

public class GameManagerComponent : MonoBehaviourPunCallbacks
{
    private readonly List<string> Types = new List<string>() { "9","T","J","Q","K","A" };
    private readonly List<string> Suits = new List<string>() { "D","H","S","C" };
    private readonly Dictionary<char, char> AlternateSuits = new Dictionary<char, char>
        { {'S','C'}, {'C','S'}, {'H','D'}, {'D','H'} };

    private List<string> Deck        = new List<string>();
    private List<string> Hands       = new List<string>();
    private List<string> PlayedCards = new List<string>();
    private List<GameObject> DisplayedCards = new List<GameObject>();

    private Dictionary<int, List<string>> PlayerHands
        = new Dictionary<int, List<string>>();

    [Header("Prefabs")]
    public GameObject cardPrefab;

    [Header("Game UI")]
    public TextMeshProUGUI mainText;
    public TextMeshProUGUI statsText;
    public GameObject      playButton;
    public GameObject      discardButton;
    public GameObject      goAloneButton;
    public GameObject      passButton;

    [Header("Trump Popup")]
    public TrumpPopupComponent trumpPopup;

    [Header("Between-Hands Summary Panel")]
    public GameObject      summaryPanel;
    public TextMeshProUGUI summaryText;
    public Button          summaryNextButton;

    public static GameMode gameMode;

    [Header("Game Over Panel")]
    public GameObject      gameOverPanel;
    public TextMeshProUGUI gameOverText;
    public Button          rematchButton;

    // Cached lobby data
    private string cachedPlayerName  = "Player";
    private string cachedRoomID      = "";
    private int    cachedTeamAttempt = 0;

    // Player state
    private bool   isHost;
    private bool   loaded;
    private int    playerNum;
    private int    players;
    private string playerID;
    private Dictionary<int, string> playerNames  = new Dictionary<int, string>();
    private readonly List<int>      playersJoined = new List<int>();

    // Game state
    private int  turn;
    private int  dealer;
    private int  team1Score;
    private int  team2Score;
    private int  team1Tricks;
    private int  team2Tricks;
    private char trump;
    private int  trumpPickerTeam;
    private int  cycled;
    private int  playerToSkip = -1;
    private bool goingAlone   = false;

    private const int WinScore = 10;

    // Bot
    private BotComponent botComponent;

    // RPC wait queue
    private readonly Queue<List<string>> waitQueue = new Queue<List<string>>();
    private int  ticks;
    private bool skip;

    public enum GameMode
    {
        Public,
        Private
    }

    // ═══════════════════════════════════════════════════════════════
    // UNITY LIFECYCLE
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        if (LobbyManagerComponent.Instance != null)
        {
            cachedPlayerName  = LobbyManagerComponent.Instance.GetName();
            cachedRoomID      = LobbyManagerComponent.Instance.GetRoomID();
            cachedTeamAttempt = LobbyManagerComponent.Instance.GetTeamAttempt();
            Debug.Log($"[GameManager] Cached — Name:{cachedPlayerName} " +
                      $"Room:'{cachedRoomID}' Team:{cachedTeamAttempt}");
            return;
        }

        GameObject lobbyObj = GameObject.Find("LobbyManager");
        if (lobbyObj != null)
        {
            LobbyManagerComponent lobby = lobbyObj.GetComponent<LobbyManagerComponent>();
            if (lobby != null)
            {
                cachedPlayerName  = lobby.GetName();
                cachedRoomID      = lobby.GetRoomID();
                cachedTeamAttempt = lobby.GetTeamAttempt();
                return;
            }
        }
        Debug.LogWarning("[GameManager] LobbyManager not found — using defaults");
    }

    void Start()
    {
        botComponent = GetComponent<BotComponent>();

        //SetPlayButton(false);
        SetDiscardButton(false);
        SetGoAloneButton(false);
        SetPassButton(false);
        summaryPanel .SetActive(false);
        gameOverPanel.SetActive(false);

        summaryNextButton.onClick.AddListener(OnSummaryNextClicked);
        rematchButton    .onClick.AddListener(OnRematchClicked);

        isHost     = false;
        loaded     = false;
        dealer     = -1;
        trump      = 'N';
        team1Score = 0;
        team2Score = 0;

        mainText.text = "Connecting...";
        PhotonNetwork.ConnectUsingSettings();
    }

    void FixedUpdate()
    {
        if (waitQueue.Count == 0) return;
        ticks++;
        if (ticks <= 120) return;

        skip = true;
        List<string> next = waitQueue.Peek();

        switch (next[0])
        {
            case "Score":    ScoreTrick();                                                   break;
            case "Play":     PlayCard(next[1]);                                              break;
            case "Start":    GameStart(next[1],next[2],next[3],next[4]);                    break;
            case "Picked":   PickedUp(int.Parse(next[1]),int.Parse(next[2]));               break;
            case "Phase":    PhaseOne();                                                     break;
            case "Begin":    BeginRounds(next[1],int.Parse(next[2]),int.Parse(next[3]));    break;
            case "Turnover": TurnoverTrump(int.Parse(next[1]));                             break;
            case "Join":     PlayerJoin(next[1],next[2],int.Parse(next[3]));                break;
            case "Sync":     SyncOtherPlayer(next[1],next[2],int.Parse(next[3]),
                                 next[4],int.Parse(next[5]),int.Parse(next[6]));            break;
            case "JoinTimeout":
                if (!loaded && PhotonNetwork.Server == ServerConnection.MasterServer)
                {
                    RoomOptions o = new RoomOptions{IsVisible=true,MaxPlayers=4};
                    string rid = "";
                    for (int i=0;i<10;i++) rid += Random.Range(0,9).ToString();
                    PhotonNetwork.JoinOrCreateRoom(rid,o,TypedLobby.Default);
                }
                else if (!loaded)
                { ticks=0; skip=false; return; }
                break;
        }

        waitQueue.Dequeue();
        ticks = 0;
        skip  = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // PHOTON CALLBACKS
    // ═══════════════════════════════════════════════════════════════

    public override void OnConnectedToMaster()
    {
        if (cachedRoomID.Length == 0)
        {
            PhotonNetwork.JoinRandomRoom();
            waitQueue.Enqueue(new List<string>{"JoinTimeout"});
        }
        else
        {
            RoomOptions opts = new RoomOptions{IsVisible=false,MaxPlayers=4};
            PhotonNetwork.JoinOrCreateRoom(cachedRoomID,opts,TypedLobby.Default);
        }
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        RoomOptions opts = new RoomOptions{IsVisible=true,MaxPlayers=4};
        string id = "";
        for (int i=0;i<10;i++) id += Random.Range(0,9).ToString();
        PhotonNetwork.JoinOrCreateRoom(id,opts,TypedLobby.Default);
    }

    public override void OnCreatedRoom()
    {
        isHost  = true;
        loaded  = true;
        players = 1;

        playerNum              = cachedTeamAttempt;
        playerNames[playerNum] = cachedPlayerName;
        playersJoined.Add(playerNum);
        dealer = Random.Range(0, 4);

        mainText.text = "Waiting for players (1/4)";

        if (gameMode == GameMode.Public)
            StartCoroutine(BotFillCountdown());
    }

    private IEnumerator BotFillCountdown()
    {
        float wait    = 5f;
        float elapsed = 0f;

        while (elapsed < wait && players < 4)
        {
            elapsed += Time.deltaTime;
            int remaining = Mathf.CeilToInt(wait - elapsed);
            mainText.text = $"Waiting for players ({players}/4) — starting in {remaining}s";
            yield return null;
        }

        if (players < 4)
            FillWithBots();
    }

    public override void OnJoinedRoom()
    {
        if (!isHost)
        {
            mainText.text = "Waiting for players...";
            playerID = "";
            for (int i=0;i<10;i++) playerID += Random.Range(0,9).ToString();

            Debug.Log($"[GameManager] Joining as '{cachedPlayerName}' team {cachedTeamAttempt}");

            PhotonView.Get(this).RPC("PlayerJoin", RpcTarget.Others,
                cachedPlayerName, playerID, cachedTeamAttempt);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PLAYER JOINING
    // ═══════════════════════════════════════════════════════════════

    [PunRPC]
    public void PlayerJoin(string newName, string joiningPlayerID, int joiningTeamAttempt)
    {
        if (waitQueue.Count > 0 && !skip)
        {
            waitQueue.Enqueue(new List<string>
                {"Join",newName,joiningPlayerID,joiningTeamAttempt.ToString()});
            return;
        }

        if (players >= 4) return;

        players++;
        mainText.text = $"Waiting for players ({players}/4)";

        if (!isHost) return;

        int seat = joiningTeamAttempt;
        if (playersJoined.Contains(seat))
        {
            int alt = seat+2; if (alt>3) alt-=4;
            if (playersJoined.Contains(alt))
            {
                int nxt = seat+1; if (nxt>3) nxt-=4;
                seat = playersJoined.Contains(nxt)
                    ? (seat-1<0 ? seat+3 : seat-1) : nxt;
            }
            else seat = alt;
        }

        playerNames[seat] = newName;
        playersJoined.Add(seat);

        PhotonView.Get(this).RPC("SyncOtherPlayer", RpcTarget.Others,
            string.Join("",Hands), string.Join("",Deck),
            players, joiningPlayerID, seat, dealer);

        if (players == 4)
            PhotonView.Get(this).RPC("GameStart", RpcTarget.All,
                playerNames[0],playerNames[1],playerNames[2],playerNames[3]);
    }

    // ═══════════════════════════════════════════════════════════════
    // GAME START
    // ═══════════════════════════════════════════════════════════════

    [PunRPC]
    public void GameStart(string p0, string p1, string p2, string p3)
    {
        if (waitQueue.Count > 0 && !skip)
        {
            waitQueue.Enqueue(new List<string>{"Start",p0,p1,p2,p3});
            return;
        }

        playerNames = new Dictionary<int,string>{{0,p0},{1,p1},{2,p2},{3,p3}};

        team1Tricks  = 0;
        team2Tricks  = 0;
        playerToSkip = -1;
        goingAlone   = false;
        trump        = 'N';
        cycled       = 0;

        dealer++;
        if (dealer > 3) dealer = 0;
        turn = dealer;

        RebuildPlayerHands();

        mainText.text = "Game starting!";
        PhaseOne();
    }

    [PunRPC]
    public void SyncOtherPlayer(string newHands, string newDeck,
        int newPlayers, string joiningPlayerID,
        int newPlayerNum, int newDealer)
    {
        if (waitQueue.Count>0 && waitQueue.Peek()[0]!="JoinTimeout" && !skip)
        {
            waitQueue.Enqueue(new List<string>
                {"Sync",newHands,newDeck,newPlayers.ToString(),
                 joiningPlayerID,newPlayerNum.ToString(),newDealer.ToString()});
            return;
        }

        if (newPlayers != -1) players = newPlayers;
        mainText.text = $"Waiting for players ({players}/4)";

        bool isMySync = joiningPlayerID==playerID || joiningPlayerID.Length==0;

        if (!loaded && isMySync)
        {
            Hands = ParseCardString(newHands);
            Deck  = ParseCardString(newDeck);

            if (newPlayers != -1) playerNum = newPlayerNum;
            if (newDealer  != -2) dealer    = newDealer;

            ClearDisplayedCards();
            CreateHand(playerNum);
            PlaceCard(Deck[0],
                new Vector3(-15f,0f,0f),
                new Vector3(-2f,1f,0f));
            loaded = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TRUMP SELECTION
    // ═══════════════════════════════════════════════════════════════

    [PunRPC]
    public void PhaseOne()
    {
        if (waitQueue.Count > 0 && !skip)
        {
            waitQueue.Enqueue(new List<string>{"Phase"});
            return;
        }

        turn++;
        if (turn > 3) turn = 0;

        if (turn == (dealer+1) % 4)
        {
            cycled++;

            if (cycled >= 2)
            {
                Debug.Log("[GameManager] Everyone passed — redealing");
                mainText.text = "Everyone passed — redealing...";

                if (isHost)
                    StartCoroutine(ReDeal());
                return;
            }
        }

        DisplayStatsText();
        DisplayTurnText();

        // ── Bot turn ──────────────────────────────────────────────
        if (isHost && botComponent != null && botComponent.IsBotTurn(turn))
        {
            char upcardSuit = Deck.Count > 0 ? Deck[0][1] : 'S';

            botComponent.BotDecideTrump(turn, upcardSuit, cycled, dealer,
                onPick: (suit) =>
                {
                    HideAllActionButtons();
                    if (cycled == 0)
                    {
                        PhotonView.Get(this).RPC("PickedUp", RpcTarget.All,
                            turn%2, playerToSkip);
                    }
                    else
                    {
                        trump           = suit[0];
                        trumpPickerTeam = turn % 2;
                        PhotonView.Get(this).RPC("BeginRounds", RpcTarget.All,
                            suit, trumpPickerTeam, playerToSkip);
                    }
                },
                onPass: () =>
                {
                    PhotonView.Get(this).RPC("PhaseOne", RpcTarget.All);
                });
            return;
        }

        // ── Human turn ────────────────────────────────────────────
        if (turn != playerNum) return;

        bool dealerForcedPick = cycled==1 && turn==dealer;

        if (cycled == 1)
        {
            string excludedSuit = Deck.Count > 0 ? Deck[0][1].ToString() : "";
            trumpPopup.Show(excludedSuit, canPass: !dealerForcedPick);
        }
        else
        {
            ShowPickPhaseButtons(true);
        }
    }

    IEnumerator ReDeal()
    {
        yield return new WaitForSeconds(2f);
        PhotonView.Get(this).RPC("ResetAndStartNewHand", RpcTarget.All);
    }

    public void Pass()
    {
        HideAllActionButtons();
        PhotonView.Get(this).RPC("PhaseOne", RpcTarget.All);
    }

    public void PickButtonClicked()
    {
        SetPassButton(false);
        playButton.SetActive(false);

        mainText.text = "Go alone or play with partner?";

        PlayButtonComponent pb = playButton.GetComponent<PlayButtonComponent>();
        if (pb != null) pb.SetMode(PlayButtonComponent.ButtonMode.ConfirmWithPartner);
        playButton.SetActive(true);

        SetGoAloneButton(true);
    }

    public void ConfirmWithPartner()
    {
        HideAllActionButtons();
        playerToSkip = -1;
        goingAlone   = false;
        PhotonView.Get(this).RPC("PickedUp", RpcTarget.All,
            playerNum % 2, -1);
    }

    public void ConfirmGoAlone()
    {
        HideAllActionButtons();
        playerToSkip = playerNum + 2;
        if (playerToSkip > 3) playerToSkip -= 4;
        goingAlone   = true;
        PhotonView.Get(this).RPC("PickedUp", RpcTarget.All,
            playerNum % 2, playerToSkip);
    }

    public void GoAloneButtonClicked()
    {
        HideAllActionButtons();

        playerToSkip = playerNum+2;
        if (playerToSkip > 3) playerToSkip -= 4;
        goingAlone = true;

        if (cycled == 1)
        {
            string excludedSuit = Deck.Count > 0 ? Deck[0][1].ToString() : "";
            trumpPopup.Show(excludedSuit, canPass: false);
        }
        else
        {
            PhotonView.Get(this).RPC("PickedUp", RpcTarget.All,
                playerNum%2, playerToSkip);
        }
    }

    public void PickTrumpSuit(string suit)
    {
        trump           = suit[0];
        trumpPickerTeam = playerNum % 2;
        PhotonView.Get(this).RPC("BeginRounds", RpcTarget.All,
            suit, trumpPickerTeam, playerToSkip);
    }

    [PunRPC]
    public void PickedUp(int newTrumpPickerTeam, int newPlayerToSkip)
    {
        if (waitQueue.Count > 0 && !skip)
        {
            waitQueue.Enqueue(new List<string>
                {"Picked",newTrumpPickerTeam.ToString(),newPlayerToSkip.ToString()});
            return;
        }

        trumpPickerTeam = newTrumpPickerTeam;
        playerToSkip    = newPlayerToSkip;
        trump           = Deck[0][1];

        if (playerNum == dealer)
        {
            mainText.text = "Pick a card to discard from your hand";

            int insertIdx = (playerNum+1) * (Hands.Count/4) - 1;
            Hands.Insert(insertIdx, Deck[0]);
            RebuildPlayerHands();

            ClearDisplayedCards();
            CreateHand(playerNum);
            PlaceCard(Deck[0],
                new Vector3(-15f,0f,0f),
                new Vector3(-2f,1f,0f));

            foreach (GameObject card in DisplayedCards)
            {
                var cc = card.GetComponent<CardComponent>();
                if (cc.GetRestPosition().y <= -3f)
                    cc.SetUnlocked(new List<bool>{false,true});
            }

            SetDiscardButton(true);
            PhotonView.Get(this).RPC("TurnoverTrump",RpcTarget.Others,newTrumpPickerTeam);
        }
        else if (isHost && botComponent != null && botComponent.IsBotTurn(dealer))
        {
            mainText.text = "Dealer is discarding...";

            int handSize  = Hands.Count / 4;
            int handStart = dealer * handSize;
            Hands.Insert(handStart + handSize, Deck[0]);
            RebuildPlayerHands();

            List<string> botHand = new List<string>(PlayerHands[dealer]);

            botComponent.BotDiscard(botHand, trump, AlternateSuits,
                onDiscard: (card) =>
                {
                    Hands.Remove(card);
                    if (PlayerHands.ContainsKey(dealer))
                        PlayerHands[dealer].Remove(card);

                    PhotonView.Get(this).RPC("BeginRounds", RpcTarget.All,
                        trump.ToString(), trumpPickerTeam, playerToSkip);
                });

            PhotonView.Get(this).RPC("TurnoverTrump",RpcTarget.Others,newTrumpPickerTeam);
        }
        else
        {
            mainText.text = "Dealer is discarding a card...";
        }

        DisplayStatsText();
    }

    public void OnDiscardButtonPressed()
    {
        foreach (GameObject cardObj in DisplayedCards)
        {
            CardComponent cc = cardObj.GetComponent<CardComponent>();
            string discard = cc.GetCardTypeIfSelectedForDiscard();
            if (discard != null)
            {
                Discarded(discard);
                return;
            }
        }
        mainText.text = "Tap a card to select it, then tap Discard";
    }

    public void Discarded(string cardType)
    {
        Hands.Remove(cardType);
        if (PlayerHands.ContainsKey(playerNum))
            PlayerHands[playerNum].Remove(cardType);

        SetDiscardButton(false);
        ClearDisplayedCards();
        CreateHand(playerNum);

        PhotonView.Get(this).RPC("BeginRounds", RpcTarget.All,
            trump.ToString(), trumpPickerTeam, playerToSkip);
    }

    [PunRPC]
    public void TurnoverTrump(int newTrumpPickerTeam)
    {
        if (waitQueue.Count > 0 && !skip)
        {
            waitQueue.Enqueue(new List<string>
                {"Turnover",newTrumpPickerTeam.ToString()});
            return;
        }

        trumpPickerTeam = newTrumpPickerTeam;

        if (DisplayedCards.Count > 0)
        {
            int last = DisplayedCards.Count-1;
            Destroy(DisplayedCards[last]);
            DisplayedCards.RemoveAt(last);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TRICK TAKING
    // ═══════════════════════════════════════════════════════════════

    [PunRPC]
    public void BeginRounds(string trumpSuit, int newTrumpPickerTeam, int newPlayerToSkip)
    {
        if (waitQueue.Count > 0 && !skip)
        {
            waitQueue.Enqueue(new List<string>
                {"Begin",trumpSuit,newTrumpPickerTeam.ToString(),newPlayerToSkip.ToString()});
            return;
        }

        trump           = char.Parse(trumpSuit);
        trumpPickerTeam = newTrumpPickerTeam;
        playerToSkip    = newPlayerToSkip;
        turn            = dealer;
        DoRound();
    }

    public void DoRound()
    {
        turn++;
        if (turn > 3) turn = 0;
        if (turn == playerToSkip)
        {
            turn++;
            if (turn > 3) turn = 0;
        }

        DisplayStatsText();
        DisplayTurnText();

        // ── Bot turn ──────────────────────────────────────────────
        if (isHost && botComponent != null && botComponent.IsBotTurn(turn))
        {
            List<string> botHand = PlayerHands.ContainsKey(turn)
                ? new List<string>(PlayerHands[turn])
                : new List<string>();

            botComponent.BotPlayCard(turn, botHand, PlayedCards,
                trump, AlternateSuits,
                onPlay: (card) =>
                {
                    if (card == null || string.IsNullOrEmpty(card)) return;

                    if (PlayerHands.ContainsKey(turn))
                        PlayerHands[turn].Remove(card);
                    Hands.Remove(card);

                    PhotonView.Get(this).RPC("PlayCard", RpcTarget.All, card);
                });
            return;
        }

        // ── Human turn ────────────────────────────────────────────
        if (turn != playerNum) return;

        foreach (GameObject cardObj in DisplayedCards)
        {
            CardComponent cc = cardObj.GetComponent<CardComponent>();
            if (cc.GetRestPosition().y <= -3f)
                cc.SetUnlocked(new List<bool>{true,false});
        }

        PlayButtonComponent pb = playButton.GetComponent<PlayButtonComponent>();
        if (pb != null) pb.SetMode(PlayButtonComponent.ButtonMode.PlayCard);

        SetPlayButton(true);
    }

    public void OnPlayButtonPressed()
    {
        foreach (GameObject cardObj in DisplayedCards)
        {
            CardComponent cc = cardObj.GetComponent<CardComponent>();
            string cardType = cc.GetCardTypeIfSelectedForPlay();
            if (cardType != null)
            {
                if (!CheckIfValid(cardType))
                    mainText.text = "You must follow suit if you can!";
                return;
            }
        }
        mainText.text = "Tap a card to select it first";
    }

    public bool CheckIfValid(string cardType)
    {
        char startingSuit = 'N';
        if (PlayedCards.Count > 0)
        {
            if (PlayedCards[0][0]=='J' && PlayedCards[0][1]==AlternateSuits[trump])
                startingSuit = trump;
            else
                startingSuit = PlayedCards[0][1];
        }

        bool isLeftBower = cardType[0]=='J' && cardType[1]==AlternateSuits[trump];

        bool followsSuit = PlayedCards.Count==0
            || (cardType[1]==startingSuit && !isLeftBower)
            || (isLeftBower && startingSuit==trump);

        if (!followsSuit)
        {
            foreach (GameObject cardObj in DisplayedCards)
            {
                CardComponent cc = cardObj.GetComponent<CardComponent>();
                if (cc.GetRestPosition().y > -3f) continue;

                string ct = cc.GetCardType();
                bool ctIsLeft = ct[0]=='J' && ct[1]==AlternateSuits[trump];
                bool hasLead  = (ct[1]==startingSuit && !ctIsLeft)
                             || (ctIsLeft && startingSuit==trump);
                if (hasLead) return false;
            }
        }

        SetPlayButton(false);
        DeselectAllCards();

        if (PlayerHands.ContainsKey(playerNum))
            PlayerHands[playerNum].Remove(cardType);
        Hands.Remove(cardType);

        PhotonView.Get(this).RPC("PlayCard", RpcTarget.Others, cardType);
        PlayedCards.Add(cardType);

        int idx = PlayedCards.Count;
        foreach (GameObject cardObj in DisplayedCards)
        {
            CardComponent cc = cardObj.GetComponent<CardComponent>();
            if (cc.GetCardType()==cardType && cc.GetRestPosition().y<=-3f)
            {
                cc.SetUnlocked(new List<bool>{false,false});
                cc.SetTargetPosition(new Vector3(
                    -0.6f+idx*0.4f, 0.5f, idx*-0.1f-1f));
                break;
            }
        }

        int numPlayers = playerToSkip != -1 ? 3 : 4;
        if (PlayedCards.Count == numPlayers)
        { ticks=0; waitQueue.Enqueue(new List<string>{"Score"}); }
        else
            DoRound();

        return true;
    }

    [PunRPC]
    public void PlayCard(string cardType)
    {
        if (waitQueue.Count > 0 && !skip)
        {
            waitQueue.Enqueue(new List<string>{"Play",cardType});
            return;
        }

        PlayedCards.Add(cardType);

        Vector3 startPos = GetOpponentStartPosition(turn);
        PlaceCard(cardType, startPos,
            new Vector3(-0.6f+PlayedCards.Count*0.4f, 0.5f,
                        PlayedCards.Count*-0.1f-1f));

        int numPlayers = playerToSkip != -1 ? 3 : 4;
        if (PlayedCards.Count == numPlayers)
        { ticks=0; waitQueue.Enqueue(new List<string>{"Score"}); }
        else
            DoRound();
    }

    // ═══════════════════════════════════════════════════════════════
    // SCORING
    // ═══════════════════════════════════════════════════════════════

    public void ScoreTrick()
    {
        char startingSuit = PlayedCards[0][1];
        if (PlayedCards[0][0]=='J' && PlayedCards[0][1]==AlternateSuits[trump])
            startingSuit = trump;

        int bestPlayer = turn;
        int bestValue  = 0;

        List<string> reversed = new List<string>(PlayedCards);
        reversed.Reverse();

        int checkTurn = turn;
        for (int i=0; i<reversed.Count; i++)
        {
            int val = EvaluateCard(reversed[i], startingSuit);
            if (val > bestValue)
            {
                bestValue  = val;
                bestPlayer = checkTurn;
            }
            checkTurn--;
            if (checkTurn < 0) checkTurn += 4;
            if (checkTurn == playerToSkip)
            {
                checkTurn--;
                if (checkTurn < 0) checkTurn += 4;
            }
        }

        turn = bestPlayer;

        if (bestPlayer%2 == 0) team1Tricks++;
        else                   team2Tricks++;

        List<GameObject> toRemove = new List<GameObject>();
        foreach (GameObject cardObj in DisplayedCards)
        {
            CardComponent cc = cardObj.GetComponent<CardComponent>();
            if (cc.GetTargetPosition().y >= 0.3f)
                toRemove.Add(cardObj);
        }
        foreach (var c in toRemove)
        {
            DisplayedCards.Remove(c);
            Destroy(c);
        }

        PlayedCards.Clear();

        if (team1Tricks+team2Tricks == 5)
            ScoreHand();
        else
            DoRound();
    }

    void ScoreHand()
    {
        int  pointsTeam1 = 0;
        int  pointsTeam2 = 0;
        bool wentAlone   = playerToSkip != -1;

        if (trumpPickerTeam == 0)
        {
            if (team1Tricks == 5)      pointsTeam1 = wentAlone ? 4 : 2;
            else if (team1Tricks >= 3) pointsTeam1 = 1;
            else                       pointsTeam2 = 2;
        }
        else
        {
            if (team2Tricks == 5)      pointsTeam2 = wentAlone ? 4 : 2;
            else if (team2Tricks >= 3) pointsTeam2 = 1;
            else                       pointsTeam1 = 2;
        }

        team1Score += pointsTeam1;
        team2Score += pointsTeam2;

        string trumpName = TrumpName(trump);
        summaryText.text =
            $"<b>Hand Over!</b>\n\n" +
            $"Trump: <b>{trumpName}</b>  |  " +
            $"Picked by: <b>Team {trumpPickerTeam+1}</b>\n\n" +
            $"Tricks  —  Team 1: <b>{team1Tricks}</b>  " +
            $"|  Team 2: <b>{team2Tricks}</b>\n\n" +
            $"Points this hand:\n" +
            $"  Team 1: <b>+{pointsTeam1}</b>\n" +
            $"  Team 2: <b>+{pointsTeam2}</b>\n\n" +
            $"<b>Total Score</b>\n" +
            $"  Team 1: {team1Score}  |  Team 2: {team2Score}";

        summaryPanel.SetActive(true);
    }

    void OnSummaryNextClicked()
    {
        summaryPanel.SetActive(false);

        if (team1Score >= WinScore || team2Score >= WinScore)
        {
            ShowGameOver();
            return;
        }

        if (isHost)
            PhotonView.Get(this).RPC("ResetAndStartNewHand", RpcTarget.All);
    }

    [PunRPC]
    public void ResetAndStartNewHand()
    {
        ResetHand();

        if (isHost)
        {
            Initialise();
            PhotonView.Get(this).RPC("SyncOtherPlayer", RpcTarget.Others,
                string.Join("",Hands), string.Join("",Deck),
                -1, "", -1, -2);
            PhotonView.Get(this).RPC("GameStart", RpcTarget.All,
                playerNames[0],playerNames[1],
                playerNames[2],playerNames[3]);
        }
    }

    void ShowGameOver()
    {
        int winner = team1Score >= WinScore ? 1 : 2;
        gameOverText.text =
            $"<b>Team {winner} Wins!</b>\n\n" +
            $"Final Score\n" +
            $"Team 1: {team1Score}\n" +
            $"Team 2: {team2Score}";
        gameOverPanel.SetActive(true);
    }

    void OnRematchClicked()
    {
        gameOverPanel.SetActive(false);
        team1Score = 0;
        team2Score = 0;

        if (isHost)
            PhotonView.Get(this).RPC("ResetAndStartNewHand", RpcTarget.All);
    }

    // ═══════════════════════════════════════════════════════════════
    // CARD EVALUATION
    // ═══════════════════════════════════════════════════════════════

    public int EvaluateCard(string cardType, char startSuit)
    {
        char val  = cardType[0];
        char suit = cardType[1];

        bool isRight = val=='J' && suit==trump;
        bool isLeft  = val=='J' && suit==AlternateSuits[trump];

        if (isRight) return 15;
        if (isLeft)  return 14;

        if (suit == trump)
            return val switch {'A'=>13,'K'=>12,'Q'=>11,'T'=>10,'9'=>9,_=>0};

        if (suit == startSuit)
            return val switch {'A'=>7,'K'=>6,'Q'=>5,'J'=>4,'T'=>3,'9'=>2,_=>0};

        return 0;
    }

    // ═══════════════════════════════════════════════════════════════
    // CARD / HAND HELPERS
    // ═══════════════════════════════════════════════════════════════

    public List<string> GetPlayerHand(int seat)
    {
        return PlayerHands.ContainsKey(seat)
            ? new List<string>(PlayerHands[seat])
            : new List<string>();
    }

    public void Initialise()
    {
        Deck.Clear();
        Hands.Clear();

        foreach (string type in Types)
            foreach (string suit in Suits)
                Deck.Add(type+suit);

        for (int p=0; p<4; p++)
            for (int c=0; c<5; c++)
            {
                int idx = Random.Range(0,Deck.Count);
                Hands.Add(Deck[idx]);
                Deck.RemoveAt(idx);
            }

        RebuildPlayerHands();
        ClearDisplayedCards();
        CreateHand(playerNum);

        PlaceCard(Deck[0],
            new Vector3(-15f,0f,0f),
            new Vector3(-2f,1f,0f));
    }

    void RebuildPlayerHands()
    {
        PlayerHands.Clear();
        if (Hands.Count < 20) return;

        int handSize = Hands.Count / 4;
        for (int seat=0; seat<4; seat++)
        {
            PlayerHands[seat] = new List<string>();
            for (int i=0; i<handSize; i++)
            {
                int idx = seat*handSize + i;
                if (idx < Hands.Count)
                    PlayerHands[seat].Add(Hands[idx]);
            }
        }
    }

    void ResetHand()
    {
        team1Tricks  = 0;
        team2Tricks  = 0;
        playerToSkip = -1;
        goingAlone   = false;
        trump        = 'N';
        cycled       = 0;
        turn         = 0;
        PlayedCards.Clear();
        PlayerHands.Clear();
        ClearDisplayedCards();
        Deck.Clear();
        Hands.Clear();
        loaded = false;
        HideAllActionButtons();
    }

    void FillWithBots()
    {
        if (botComponent == null) return;

        string[] botNames = {"Bot Alpha","Bot Beta","Bot Gamma"};
        int botIdx = 0;
        List<int> botSeatList = new List<int>();

        for (int seat=0; seat<4; seat++)
        {
            if (!playersJoined.Contains(seat))
            {
                playerNames[seat] = botNames[botIdx++];
                playersJoined.Add(seat);
                players++;
                botSeatList.Add(seat);
            }
        }

        botComponent.SetBotSeats(botSeatList);
        Debug.Log($"[GameManager] Bots filled seats: {string.Join(",", botSeatList)}");

        loaded = true;
        Initialise();

        PhotonView.Get(this).RPC("GameStart", RpcTarget.All,
            playerNames[0], playerNames[1],
            playerNames[2], playerNames[3]);
    }

    public void PlaceCard(string cardType, Vector3 startPos, Vector3 targetPos)
    {
        GameObject cardObj = Instantiate(cardPrefab, startPos, Quaternion.identity);
        CardComponent cc   = cardObj.GetComponent<CardComponent>();
        cc.SetCard(cardType);
        cc.SetRestPosition(targetPos);
        cc.SetUnlocked(new List<bool>{false,false});
        DisplayedCards.Add(cardObj);
    }

    public void CreateHand(int pNum)
    {
        int   handSize = Hands.Count / 4;
        float startX   = -3.5f;
        float spacingX =  1.75f;
        float handY    = -3.8f;

        for (int i=0; i<handSize; i++)
        {
            int idx = pNum*handSize + i;
            if (idx >= Hands.Count) break;
            string card = Hands[idx];
            PlaceCard(card,
                new Vector3(0f,-8f,0f),
                new Vector3(startX+spacingX*i, handY, -1f*i));
        }
    }

    void ClearDisplayedCards()
    {
        foreach (GameObject c in DisplayedCards) Destroy(c);
        DisplayedCards.Clear();
    }

    public void DeselectAllCards()
    {
        foreach (GameObject c in DisplayedCards)
            c.GetComponent<CardComponent>().Deselect();
    }

    // ═══════════════════════════════════════════════════════════════
    // UI HELPERS
    // ═══════════════════════════════════════════════════════════════

    public int GetPlayedCardsCount() => PlayedCards.Count;

    void DisplayTurnText()
    {
        if (!playerNames.ContainsKey(turn)) return;
        mainText.text = turn==playerNum
            ? "Your turn"
            : $"{playerNames[turn]}'s turn";
    }

    void DisplayStatsText()
    {
        if (!playerNames.ContainsKey(playerNum)) return;
        statsText.text =
            $"You: {playerNames[playerNum]}  |  " +
            $"Team {(playerNum%2==0?1:2)}\n" +
            $"Dealer: {(playerNames.ContainsKey(dealer)?playerNames[dealer]:"?")}\n" +
            $"Trump: {TrumpName(trump)}  |  " +
            $"Picked by: Team {trumpPickerTeam+1}\n\n" +
            $"Tricks  —  T1: {team1Tricks}  |  T2: {team2Tricks}\n" +
            $"Score   —  T1: {team1Score}   |  T2: {team2Score}";
    }

    void ShowPickPhaseButtons(bool visible)
    {
        SetPassButton(visible);
        SetGoAloneButton(false);

        PlayButtonComponent pb = playButton.GetComponent<PlayButtonComponent>();
        if (pb != null) pb.SetMode(PlayButtonComponent.ButtonMode.PickUpTrump);

        playButton.SetActive(visible);
    }

    void HideAllActionButtons()
    {
        SetPlayButton(false);
        SetDiscardButton(false);
        SetGoAloneButton(false);
        SetPassButton(false);
    }

    void SetPlayButton(bool v)
    {
        Debug.Log($"SetPlayButton: {v} | Turn: {turn} | Me: {playerNum}");
        playButton.SetActive(v);
    }

    void SetDiscardButton(bool v)
    {
        Debug.Log("SetDiscardButton: " + v);
        discardButton.SetActive(v);
    }

    void SetGoAloneButton(bool v)
    {
        Debug.Log("SetGoAloneButton: " + v);
        goAloneButton.SetActive(v);
    }

    void SetPassButton(bool v)
    {
        Debug.Log("SetPassButton: " + v);
        passButton.SetActive(v);
    }

    List<string> ParseCardString(string raw)
    {
        var r = new List<string>();
        for (int i=0; i<raw.Length/2; i++)
            r.Add(""+raw[i*2]+raw[i*2+1]);
        return r;
    }

    Vector3 GetOpponentStartPosition(int fromPlayer)
    {
        int rel = fromPlayer-playerNum;
        if (rel < 0) rel += 4;
        return rel switch
        {
            1 => new Vector3( 12f,0f,0f),
            2 => new Vector3(  0f,8f,0f),
            3 => new Vector3(-12f,0f,0f),
            _ => new Vector3(  0f,8f,0f)
        };
    }

    string TrumpName(char t) => t switch
    {
        'C'=>"Clubs",'H'=>"Hearts",
        'D'=>"Diamonds",'S'=>"Spades",
        _=>"Undecided"
    };
}