# Euchre Game — Unity Setup Instructions
# Complete step-by-step guide for all scripts

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 0 — PROJECT REQUIREMENTS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Make sure these packages are installed before touching any script:

  - Photon PUN 2 (from Asset Store)
  - PlayFab SDK (from Asset Store or GitHub)
  - DOTween (from Asset Store — free version is fine)
  - TextMeshPro (built into Unity via Package Manager)
  - Newtonsoft.Json (via Package Manager → com.unity.nuget.newtonsoft-json)
  - NativeGallery (already in your project)
  - Lean GUI (already in your project)

C# version: make sure your project uses C# 8 or higher.
  Unity → Edit → Project Settings → Player → Other Settings
  → Api Compatibility Level → .NET Standard 2.1

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 1 — ADD SCRIPTS TO PROJECT
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Copy all .cs files into your Unity project Assets folder.
Suggested folder structure:

  Assets/
  ├── Scripts/
  │   ├── Core/
  │   │   ├── MatchConfig.cs
  │   │   ├── MatchConfigLoader.cs
  │   │   └── GameplaySceneBootstrapper.cs
  │   ├── MainMenu/
  │   │   ├── PlayFabManager.cs          (your existing script)
  │   │   └── MatchConfigBuilder.cs
  │   ├── Gameplay/
  │   │   ├── GameplayPlayFabManager.cs
  │   │   ├── GameplayNetworkManager.cs
  │   │   ├── GameplayPlayerData.cs
  │   │   ├── EuchreCardManager.cs
  │   │   └── EuchreCardUI.cs
  │   └── UI/
  │       ├── TrumpSelectionUI.cs
  │       ├── TeamVoteUI.cs
  │       └── GameOverUI.cs

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 2 — EDIT PlayFabManager.cs (YOUR EXISTING SCRIPT)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Open your existing PlayFabManager.cs and make these 6 edits.
The file PlayFabManager_Additions.cs has the full details with 
comments but here is a plain summary:

  EDIT 1 — Add 3 private fields near your other cached fields:

      private string _cachedAvatarUrl    = "";
      private int    _cachedPC           = 0;
      private string _cachedDisplayName  = "";

  EDIT 2 — Add 4 public read-only properties near your accessors:

      public string CachedPlayFabId   => _playFabId;
      public string CachedDisplayName => _cachedDisplayName;
      public string CachedAvatarUrl   => _cachedAvatarUrl;
      public int    CachedPokerChips  => _cachedPC;

  EDIT 3 — In FetchCurrencies() success callback, add:

      _cachedPC = pc;

      (add this line right after you calculate the pc int value)

  EDIT 4 — In FetchProfileAndStats() inside GetPlayerProfile success, add:

      if (!string.IsNullOrEmpty(displayName))
          _cachedDisplayName = displayName;

  EDIT 5 — In SaveImageUrlToPlayFab(), inside the success callback, add:

      _cachedAvatarUrl = url;

      (url is the string parameter passed into SaveImageUrlToPlayFab)

  EDIT 6 — In FetchProfileImage() success callback, add:

      if (!string.IsNullOrEmpty(avatarUrl))
          _cachedAvatarUrl = avatarUrl;

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 3 — SCRIPT EXECUTION ORDER
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Unity → Edit → Project Settings → Script Execution Order

Set these values (lower number = runs first):

  GameplaySceneBootstrapper   →  -200
  GameplayPlayFabManager      →  -100
  GameplayNetworkManager      →  -50
  EuchreCardManager           →   0   (default)
  TrumpSelectionUI            →   0   (default)
  TeamVoteUI                  →   0   (default)
  GameOverUI                  →   0   (default)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 4 — UPLOAD CLOUDSCRIPT TO PLAYFAB
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  1. Go to playfab.com → your title → Automation → CloudScript
  2. Make sure you are on the "Revisions (Legacy)" tab
     NOT the "Functions" tab (that is the new Azure Functions system)
  3. Paste the contents of CloudScript_GetGameplayPlayerProfile.js
     into the editor. Add it alongside your existing GetOtherPlayerProfile
     function — do not replace the whole file.
  4. Click Save and Deploy
  5. Verify the function name is exactly: GetGameplayPlayerProfile

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 5 — MAIN MENU SCENE SETUP
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Open your Main Menu scene.

  5A — ADD MatchConfigBuilder TO YOUR EXISTING GAMEOBJECT

    - Find the GameObject that holds your PlayFabManager component
    - Add Component → MatchConfigBuilder
    - In the Inspector, drag the same GameObject into the
      "Play Fab Manager" field of MatchConfigBuilder

  5B — HOOK UP YOUR PLAY BUTTONS

    Your play buttons currently call methods on PlayFabManager.
    Now they should also call MatchConfigBuilder. Two options:

    OPTION A (recommended): Let the button call MatchConfigBuilder directly
      - Quick Match button → OnClick → MatchConfigBuilder.BuildPublicMatch()
      - Private Match button → OnClick → MatchConfigBuilder.BuildPrivateMatch(roomCode)
        (pass the room code string from your TMP_InputField using a wrapper method)

    OPTION B: Call from PlayFabManager
      Add to PlayFabManager.PublicMatch():

          GetComponent<MatchConfigBuilder>().BuildPublicMatch();

      And remove the SceneManager.LoadScene call from PublicMatch()
      since MatchConfigBuilder now handles scene loading.

  5C — SCENE NAME CHECK
    Make sure your gameplay scene is named exactly "EuchreGame"
    in Build Settings. MatchConfigBuilder loads it by that name.
    Unity → File → Build Settings → check scene list

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 6 — GAMEPLAY SCENE SETUP
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Open your EuchreGame scene.

━━━━━━━━━━━━━━━━━
  6A — HIERARCHY STRUCTURE TO CREATE
━━━━━━━━━━━━━━━━━

  Create this GameObject hierarchy in the scene:

  [Canvas] (your main UI canvas, Screen Space - Overlay)
  ├── [Managers]
  │   ├── GameManager          ← empty GameObject
  │   ├── PlayFabManager       ← empty GameObject
  │   └── Bootstrapper         ← empty GameObject
  │
  ├── [PlayerPanels]
  │   ├── PlayerPanel_Me       ← local player UI
  │   ├── PlayerPanel_Partner  ← partner / player 1
  │   ├── PlayerPanel_Left     ← left opponent
  │   └── PlayerPanel_Right    ← right opponent
  │
  ├── [CardHolder]
  │   └── PlayerCardHolder     ← empty RectTransform, anchored bottom-center
  │
  └── [UI Panels]
      ├── TrumpSelectionPanel
      ├── TeamVotePanel
      └── GameOverPanel

━━━━━━━━━━━━━━━━━
  6B — GAMEMANAGER GAMEOBJECT
━━━━━━━━━━━━━━━━━

  Select the GameManager GameObject and add these components:

    1. PhotonView           (required by PUN — Add Component → Photon → PhotonView)
    2. GameplayNetworkManager
    3. EuchreCardManager

  On PhotonView:
    - Observed Components: leave empty (we don't observe transforms)
    - View ID: leave as 1 (default for scene objects)

  On GameplayNetworkManager, assign in Inspector:
    - Play Fab Manager    → drag PlayFabManager GameObject
    - Card Manager        → drag GameManager GameObject (same object, has EuchreCardManager)
    - Trump UI            → drag TrumpSelectionPanel
    - Team Vote UI        → drag TeamVotePanel
    - Game Over UI        → drag GameOverPanel
    - Status Text         → drag your TMP_Text that shows game status messages

  On EuchreCardManager, assign in Inspector:
    - Game Canvas         → drag your Canvas
    - Card Holder         → drag PlayerCardHolder RectTransform
    - Shuffle Card Prefab → drag your backside card prefab (see Step 8)
    - Playable Card Prefab → drag your playable card prefab (see Step 8)
    - Card Sprites        → assign all 24 card sprites in order (see Step 8)
    - Card Back Sprite    → assign back face sprite

━━━━━━━━━━━━━━━━━
  6C — PLAYFABMANAGER GAMEOBJECT
━━━━━━━━━━━━━━━━━

  Select the PlayFabManager GameObject and add:

    1. GameplayPlayFabManager

  On GameplayPlayFabManager, assign in Inspector:
    - Player Panels [0]   → drag PlayerPanel_Me
    - Player Panels [1]   → drag PlayerPanel_Partner
    - Player Panels [2]   → drag PlayerPanel_Left
    - Player Panels [3]   → drag PlayerPanel_Right

━━━━━━━━━━━━━━━━━
  6D — BOOTSTRAPPER GAMEOBJECT
━━━━━━━━━━━━━━━━━

  Select the Bootstrapper GameObject and add:

    1. GameplaySceneBootstrapper

  On GameplaySceneBootstrapper, assign in Inspector:
    - Play Fab Manager    → drag PlayFabManager GameObject
    - Network Manager     → drag GameManager GameObject

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 7 — PLAYER PANEL SETUP (x4 panels)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Repeat this for all 4 player panels:
  PlayerPanel_Me, PlayerPanel_Partner, PlayerPanel_Left, PlayerPanel_Right

  Each panel needs this child structure:

  [PlayerPanel_X]
  ├── NameText              TMP_Text
  ├── PCText                TMP_Text
  ├── AvatarImage           Image
  ├── DealerIcon            Image (child GameObject, inactive by default)
  ├── TrumpDisplay
  │   ├── TrumpFrame        Image (the frame border, inactive by default)
  │   ├── ClubsIcon         Image
  │   ├── DiamondsIcon      Image
  │   ├── HeartsIcon        Image
  │   └── SpadesIcon        Image
  ├── TeamBadge             GameObject with TMP_Text child (inactive by default)
  └── GoAloneBadge          GameObject (inactive by default)

  Select [PlayerPanel_X] root GameObject and add component:
    → GameplayPlayerData

  Assign all fields in Inspector:
    - Name Text             → NameText
    - Pc Text               → PCText
    - Avatar Image          → AvatarImage
    - Default Avatar        → assign a default sprite (placeholder face etc.)
    - Dealer Icon           → DealerIcon GameObject
    - Trump Frame           → TrumpFrame Image
    - Trump Clubs Icon      → ClubsIcon
    - Trump Diamonds Icon   → DiamondsIcon
    - Trump Hearts Icon     → HeartsIcon
    - Trump Spades Icon     → SpadesIcon
    - Team Badge            → TeamBadge GameObject
    - Team Badge Text       → TeamBadge/TMP_Text child
    - Go Alone Badge        → GoAloneBadge GameObject

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 8 — CARD PREFABS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

You need two prefabs:

━━━━━━━━━━━━━━━━━
  PREFAB A — ShuffleCard (backside / dealing animation card)
━━━━━━━━━━━━━━━━━

  - Create a UI Image GameObject
  - Set sprite to your card back image
  - Size: any (it will be overridden per context)
  - Add component: Image (already there)
  - Do NOT add Button
  - Save as prefab: Assets/Prefabs/ShuffleCard.prefab

━━━━━━━━━━━━━━━━━
  PREFAB B — PlayableCard (front-face card with button)
━━━━━━━━━━━━━━━━━

  - Create a UI Image GameObject
  - Size: Width 165, Height 120 (set in RectTransform)
  - Add component: Button
  - Add component: EuchreCardUI
  - The Image component is already on it (EuchreCardUI uses it for sprite and tint)
  - Save as prefab: Assets/Prefabs/PlayableCard.prefab

━━━━━━━━━━━━━━━━━
  CARD SPRITES (assign to EuchreCardManager)
━━━━━━━━━━━━━━━━━

  EuchreCardManager.cardSprites expects 24 sprites in this exact order:
  (for each type, then each suit within that type)

  Index  0: 9C   (Nine of Clubs)
  Index  1: 9D   (Nine of Diamonds)
  Index  2: 9H   (Nine of Hearts)
  Index  3: 9S   (Nine of Spades)
  Index  4: TC   (Ten of Clubs)
  Index  5: TD
  Index  6: TH
  Index  7: TS
  Index  8: JC   (Jack of Clubs)
  Index  9: JD
  Index 10: JH
  Index 11: JS
  Index 12: QC
  Index 13: QD
  Index 14: QH
  Index 15: QS
  Index 16: KC
  Index 17: KD
  Index 18: KH
  Index 19: KS
  Index 20: AC   (Ace of Clubs)
  Index 21: AD
  Index 22: AH
  Index 23: AS

  Assign them in the Inspector list in that exact order.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 9 — TRUMP SELECTION UI PANEL
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Create child structure under TrumpSelectionPanel:

  [TrumpSelectionPanel]           ← root, add TrumpSelectionUI component
  ├── PanelRect                   ← inner RectTransform (this gets DOTween scaled)
  │   ├── TitleText               TMP_Text
  │   ├── SubtitleText            TMP_Text
  │   ├── UpcardDisplay           GameObject
  │   │   └── UpcardSuitIcon      Image (shows the upcard suit)
  │   ├── SuitButtonGroup         GameObject (phase 2 only)
  │   │   ├── ClubsButton         Button
  │   │   ├── DiamondsButton      Button
  │   │   ├── HeartsButton        Button
  │   │   └── SpadesButton        Button
  │   ├── OrderUpButton           Button  ("Order Up")
  │   ├── PassButton              Button  ("Pass")
  │   ├── GoAloneButton           Button  ("Go Alone?")
  │   └── GoAloneGroup            GameObject
  │       ├── GoAloneYesButton    Button  ("Yes — Go Alone")
  │       └── GoAloneNoButton     Button  ("No — With Partner")

  Select TrumpSelectionPanel and add component: TrumpSelectionUI

  Assign all Inspector fields to the children listed above.
  Also assign 4 suit sprites (clubs, diamonds, hearts, spades icons)
  to the Clubs Icon / Diamonds Icon / Hearts Icon / Spades Icon fields.

  Set TrumpSelectionPanel inactive by default (uncheck in Inspector).

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 10 — TEAM VOTE UI PANEL
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Create child structure under TeamVotePanel:

  [TeamVotePanel]                 ← root, add TeamVoteUI component
  ├── PanelRect                   ← inner RectTransform (DOTween scaled)
  │   ├── TitleText               TMP_Text
  │   ├── BodyText                TMP_Text
  │   ├── WaitingText             TMP_Text
  │   ├── VoteIcons               GameObject
  │   │   ├── VoteIcon_0          Image  (local player)
  │   │   ├── VoteIcon_1          Image  (partner)
  │   │   ├── VoteIcon_2          Image  (left)
  │   │   └── VoteIcon_3          Image  (right)
  │   ├── YesButton               Button  ("Play as Team!")
  │   └── NoButton                Button  ("Free For All")

  Select TeamVotePanel and add component: TeamVoteUI

  Assign Inspector fields.
  Vote Status Icons array → assign VoteIcon_0 through VoteIcon_3 in order.
  Assign 3 sprites: Vote Pending Sprite, Vote Yes Sprite, Vote No Sprite
  (make simple colored circle sprites or use your existing icon set).

  Set TeamVotePanel inactive by default.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 11 — GAME OVER UI PANEL
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Create child structure under GameOverPanel:

  [GameOverPanel]                 ← root, add GameOverUI component
  ├── PanelRect                   ← inner RectTransform (DOTween scaled)
  │   ├── WinnerText              TMP_Text
  │   ├── ScoreText               TMP_Text
  │   ├── WinningsText            TMP_Text
  │   ├── RematchCostText         TMP_Text
  │   ├── RematchButton           Button
  │   ├── MainMenuButton          Button
  │   ├── WinFX                   GameObject (particle system, optional)
  │   └── LoseFX                  GameObject (particle system, optional)

  Select GameOverPanel and add component: GameOverUI
  Assign all Inspector fields.
  Set GameOverPanel inactive by default.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 12 — STATUS TEXT
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Create a TMP_Text somewhere visible on the canvas
  (e.g. top-center of screen) — this shows status messages like
  "Waiting for players (2/4)" and "Your turn — select a card".

  Assign it to GameplayNetworkManager → Status Text field.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 13 — PLAYERCARDHOLDER RECTTRANSFORM
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Create an empty GameObject named PlayerCardHolder under Canvas.
  This is the parent for all playable card prefabs spawned in hand.

  RectTransform settings:
    Anchor: bottom-center
    Pivot: (0.5, 0)
    AnchoredPosition: (0, 0)
    Width: 300, Height: 200

  Assign to EuchreCardManager → Card Holder field.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 14 — PHOTON SETTINGS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Window → Photon Unity Networking → PUN Wizard
    → paste your Photon App ID if not already done

  PhotonServerSettings (in Assets/Photon/PhotonUnityNetworking/Resources):
    - App Id PUN: your app ID
    - Fixed Region: leave blank (auto) or set to your region
    - Protocol: UDP

  Make sure both your scenes (MainMenu and EuchreGame) are added to
  Unity → File → Build Settings → Scenes In Build.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 15 — PLAY BUTTON CONFIRM CARD
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  You need a "Play Card" button visible during local player's turn.
  This calls EuchreCardManager.ConfirmPlaySelectedCard().

  Create a Button in the Canvas:
    Name: PlayCardButton
    Label: "Play Card"
    OnClick → drag GameManager GameObject
             → EuchreCardManager.ConfirmPlaySelectedCard()

  Position it somewhere accessible (e.g. bottom-right).
  You can hide/show it as needed — EuchreCardManager.EnableCardSelection()
  controls when cards are interactive but does not auto-show this button.
  You can hook EnableCardSelection's bool into a
  simple ButtonVisibilityController if you want auto show/hide.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## STEP 16 — FINAL CHECKLIST BEFORE FIRST RUN
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Go through this list before pressing Play:

  [ ] PlayFabManager.cs has all 6 edits from Step 2
  [ ] CloudScript deployed and named GetGameplayPlayerProfile
  [ ] Both scenes in Build Settings
  [ ] Photon App ID entered in PhotonServerSettings
  [ ] PlayFab Title ID entered in PlayFab SDK settings
  [ ] GameplayNetworkManager has PhotonView component on same GameObject
  [ ] All 4 PlayerPanel GameObjects have GameplayPlayerData component
  [ ] All 4 panels assigned in GameplayPlayFabManager.playerPanels array
  [ ] ShuffleCard prefab has Image component (no Button)
  [ ] PlayableCard prefab has Image + Button + EuchreCardUI components
  [ ] 24 card sprites assigned in correct order in EuchreCardManager
  [ ] Card back sprite assigned in EuchreCardManager
  [ ] TrumpSelectionPanel, TeamVotePanel, GameOverPanel all inactive by default
  [ ] Script Execution Order set as in Step 3
  [ ] MatchConfigBuilder on same GameObject as PlayFabManager in Main Menu scene
  [ ] Play buttons in Main Menu call MatchConfigBuilder methods

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## COMMON ERRORS AND FIXES
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  ERROR: "GetValueOrDefault does not exist"
  FIX:   Replace all instances of:
           _seatToName.GetValueOrDefault(seat, "Player")
         with:
           (_seatToName.ContainsKey(seat) ? _seatToName[seat] : "Player")

  ERROR: "PhotonView is not found on this GameObject"
  FIX:   Add PhotonView component to the GameManager GameObject
         (the one holding GameplayNetworkManager)

  ERROR: "NullReferenceException on playFabManager"
  FIX:   Make sure GameplaySceneBootstrapper has both references assigned
         and Script Execution Order is set correctly (Step 3)

  ERROR: "CloudScript function not found"
  FIX:   Check PlayFab dashboard → Automation → CloudScript (Revisions)
         Make sure you clicked Deploy after saving

  ERROR: Cards not showing correct sprites
  FIX:   Check the sprite order in EuchreCardManager.cardSprites list
         Must be in exact order: 9C, 9D, 9H, 9S, TC, TD... (see Step 8)

  ERROR: "No config found — using defaults" in console
  FIX:   MatchConfigBuilder.BuildPublicMatch() was not called before
         scene load. Check your play button connections in Main Menu.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## DATA FLOW SUMMARY (for reference)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Main Menu scene:
    PlayFabManager (login, fetch profile) 
      → MatchConfigBuilder.BuildPublicMatch/BuildPrivateMatch()
      → MatchConfigLoader.Save() writes JSON to PlayerPrefs
      → SceneManager.LoadScene("EuchreGame")

  Gameplay scene startup:
    GameplaySceneBootstrapper.Awake()
      → MatchConfigLoader.Load() reads JSON
      → GameplayNetworkManager.Init(config)
      → GameplayPlayFabManager reads config in its own Start()

  Player joins:
    GameplayNetworkManager → Photon connect → room join/create
      → seat assignment RPC
      → GameplayPlayFabManager.SetupOtherPlayer() for each other player
      → CloudScript fetches other player name + avatar

  Match flow:
    Team vote → DeductBet → Deal cards → Trump phase 1/2
      → Tricks loop → Hand scoring → repeat until 10 pts
      → EndMatch → DistributeWinnings + SaveMatchStats

  UI sync:
    GameplayNetworkManager calls:
      playFabManager.SetDealer(panelIndex)
      playFabManager.SetTrump(panelIndex, suit)
      playFabManager.SetGoAlone(panelIndex)
    These update GameplayPlayerData on each panel.
