using DG.Tweening;
using Lean.Gui;
using PlayFab;
using PlayFab.ClientModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using static NativeGallery;

[Serializable]
public class PlayerStatsData
{
    public int playerLevel;
    public int playerXP;
    public int playerWin;

    public int totalWinCoins;
    public int totalGamePoints;
    public int totalWinGP;

    public int totalGamesPlayed;
    public int totalGamesWon;

    public int totalTournPlayed;
    public int totalTournWon;

    public int totalTrophy;
}

/// <summary>
/// PlayFabManager — Eurchre Project
/// Handles: Splash/Loading, Signup, Login, Error states,
///          Display Name check/set, Currency display,
///          Player Stats (Level/XP/Wins + extended stats) with local cache + manual save.
/// </summary>
public class PlayFabManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────
    
    [SerializeField] TMP_InputField roomCodeInput;
    public GameObject teamSelectionPanel;

    [Header("=== ROOT PANELS ===")]
    [SerializeField] private GameObject splashAndLoading;
    [SerializeField] private GameObject mainSplashPanel;
    [SerializeField] private GameObject signupPanel;
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject mainMenuPanel;
    //[SerializeField] private GameObject errorPanel;
    [SerializeField] private LeanWindow errorPanel;

    [Header("=== SET NAME PANEL ===")]
    //[SerializeField] private GameObject setNamePanel;
    [SerializeField] private LeanWindow setNamePanel;
    [SerializeField] private TMP_InputField setNameInputField;
    //profile pic raw image array
    [SerializeField] private Image[] profileImages;

    [Header("=== SPLASH / LOADING ===")]
    [SerializeField] private Transform splashScreenLogo;
    [SerializeField] private RectTransform firstLoadingParent;
    [SerializeField] private Image firstLoadingFill;
    [SerializeField] private TMP_Text loadingText;

    [Header("=== SIGNUP ===")]
    [SerializeField] private TMP_InputField signupEmailInput;
    [SerializeField] private TMP_InputField signupPassField;

    [Header("=== ERROR ===")]
    [SerializeField] private TMP_Text errorTitle;

    [Header(" Texts Arrays ")]
    [SerializeField] private TMP_Text[] displayNameTexts;
    [SerializeField] private TMP_Text[] gpTexts;
    [SerializeField] private TMP_Text[] pcTexts;
    // ── Core progression ──────────────────────────────────────────
    [SerializeField] private TMP_Text[] playerLevelTexts;
    [SerializeField] private TMP_Text[] playerXPTexts;
    [SerializeField] private TMP_Text[] playerWinTexts;
    // ── Extended stats ────────────────────────────────────────────
    [SerializeField] private TMP_Text[] totalWinningCoinTexts;
    [SerializeField] private TMP_Text[] totalGamePointsTexts;
    [SerializeField] private TMP_Text[] totalWinningGoldenPassTexts;
    [SerializeField] private TMP_Text[] totalGamesPlayedTexts;
    [SerializeField] private TMP_Text[] totalGamesWonTexts;
    [SerializeField] private TMP_Text[] totalTournamentsPlayedTexts;
    [SerializeField] private TMP_Text[] totalTournamentsWonTexts;
    [SerializeField] private TMP_Text[] totalTrophyTexts;
    [SerializeField] private TMP_Text[] emailTexts;
    [SerializeField] private TMP_Text[] playFabIdTexts;

    [Header("=== XP FILL IMAGE ===")]
    [SerializeField] private Image xpFillImage;

    [Header("=== LOGIN / SIGNUP PANELS ===")]
    [SerializeField] private GameObject middlePanelForLogin;
    [SerializeField] private GameObject middlePanelForSignUp;

    [Header("=== LEADERBOARD ===")]
    [SerializeField] private GameObject leaderboardPanel;

    [SerializeField] private RectTransform leaderboardContent;

    [SerializeField] private GameObject myLeaderboardCardPrefab;
    [SerializeField] private GameObject otherLeaderboardCardPrefab;


    // ─────────────────────────────────────────────────────────────
    //  Constants
    // ─────────────────────────────────────────────────────────────

    private const string CACHED_EMAIL_KEY = "PlayFab_CachedEmail";
    private const string CACHED_PASS_KEY = "PlayFab_CachedPass";
    private const float MIN_LOAD_SECONDS = 3f;

    // PlayFab data keys
    private const string KEY_PLAYER_STATS = "PlayerStats";

    // ─────────────────────────────────────────────────────────────
    //  XP → Level table
    //  Index = level (1-based), value = XP required to REACH that level.
    //  Level 1 = 0 XP (start), Level 2 = 10 XP, Level 3 = 25 XP, etc.
    // ─────────────────────────────────────────────────────────────

    // XP thresholds: index i → minimum XP to be at level (i+1)
    // Extend this array to add more levels.
    private static readonly int[] LevelThresholds = new int[]
    {
        0,   // Level 1  (0  XP)
        10,  // Level 2  (10 XP)
        25,  // Level 3  (25 XP)
        55,  // Level 4  (55 XP)
        100, // Level 5
        160, // Level 6
        240, // Level 7
        340, // Level 8
        460, // Level 9
        600, // Level 10
    };

    // ─────────────────────────────────────────────────────────────
    //  Local stat cache  (all writes go here; server save is manual)
    // ─────────────────────────────────────────────────────────────

    private int _playerLevel = 1;
    private int _playerXP = 0;
    private int _playerWin = 0;
    private int _totalWinCoins = 0;
    private int _totalGamePoints = 0;
    private int _totalWinGP = 0;
    private int _totalGamesPlayed = 0;
    private int _totalGamesWon = 0;
    private int _totalTournPlayed = 0;
    private int _totalTournWon = 0;
    private int _totalTrophy = 0;

    // ─────────────────────────────────────────────────────────────
    //  PlayFab flow state
    // ─────────────────────────────────────────────────────────────

    private bool _playfabReady = false;
    private bool _playfabFailed = false;
    private string _playfabError = "";

    private string _playFabId = "";
    private string _playerEmail = "";

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake() => DOTween.Init();
    public int TotalTrophy => _totalTrophy;

    [System.Serializable]
    public class CloudinaryResponse { public string secure_url; }

    [Header("=== FRIEND PANEL ===")]
    [SerializeField] private GameObject friendPanel;
    [SerializeField] private TMP_InputField searchInput;
    [SerializeField] private RectTransform scrollContent;

    [Header("=== PREFABS ===")]
    [SerializeField] private GameObject friendCardPrefab;

    // Cached friend ID list fetched from PlayFab
    private List<string> _friendIds = new List<string>();

    [Header("=== OTHER PLAYER PANEL ===")]
    [SerializeField] private OtherPlayerInfoPanel otherPlayerInfoPanel;

    [Header("=== DEFAULT PROFILE IMAGE ===")]
    [SerializeField] private Sprite defaultProfileSprite;

    private void Start()
    {
        SetAllPanelsOff();

        if (middlePanelForLogin)
            middlePanelForLogin.transform.localScale = Vector3.zero;

        if (middlePanelForSignUp)
            middlePanelForSignUp.transform.localScale = Vector3.zero;

        splashAndLoading.SetActive(true);
        mainSplashPanel.SetActive(true);
        StartCoroutine(InitialLoadingFlow());


    }

    // ─────────────────────────────────────────────────────────────
    //  LOADING COROUTINE A — Timer Only
    // ─────────────────────────────────────────────────────────────

    private IEnumerator InitialLoadingFlow()
    {
        yield return StartCoroutine(PlaySplashAnimation());

        firstLoadingFill.fillAmount = 0f;

        float elapsed = 0f;
        bool errorShown = false;

        // Loading text animation
        float loadingTextTimer = 0f;
        int loadingDotState = 0;

        if (loadingText != null)
            loadingText.text = "Loading.";

        ResetPlayFabState();
        CheckInternetConnection();

        while (true)
        {
            elapsed += Time.deltaTime;

            // Loading. -> Loading.. -> Loading... -> Loading....
            loadingTextTimer += Time.deltaTime;

            if (loadingTextTimer >= 0.3f)
            {
                loadingTextTimer = 0f;

                loadingDotState++;

                if (loadingDotState > 4)
                    loadingDotState = 1;

                if (loadingText != null)
                    loadingText.text = "Loading" + new string('.', loadingDotState);
            }

            if (_playfabFailed && !errorShown)
            {
                errorShown = true;

                yield return firstLoadingFill
                    .DOFillAmount(0.3f, 0.4f)
                    .SetEase(Ease.OutQuad)
                    .WaitForCompletion();

                yield return new WaitForSeconds(0.5f);

                ShowError(_playfabError);
                yield break;
            }

            if (_playfabReady && elapsed >= MIN_LOAD_SECONDS)
            {
                /*if (loadingText != null)
                    loadingText.text = "Loading Complete";*/

                yield return firstLoadingFill
                    .DOFillAmount(1f, 0.35f)
                    .SetEase(Ease.OutQuad)
                    .WaitForCompletion();

                yield return new WaitForSeconds(0.2f);

                DecideLoginPath();
                yield break;
            }

            float naturalTarget = Mathf.Clamp01(elapsed / MIN_LOAD_SECONDS) * 0.9f;

            if (firstLoadingFill.fillAmount < naturalTarget)
            {
                firstLoadingFill.fillAmount = Mathf.Lerp(
                    firstLoadingFill.fillAmount,
                    naturalTarget,
                    Time.deltaTime * 3f);
            }

            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  LOADING COROUTINE B — PlayFab-Aware
    // ─────────────────────────────────────────────────────────────

    private IEnumerator PlayFabAwareLoadingFlow(Action onComplete)
    {
        yield return StartCoroutine(PlaySplashAnimation());

        firstLoadingFill.fillAmount = 0f;
        float elapsed = 0f;
        bool errorShown = false;

        while (true)
        {
            elapsed += Time.deltaTime;

            if (_playfabFailed && !errorShown)
            {
                errorShown = true;
                yield return firstLoadingFill.DOFillAmount(0.3f, 0.4f).SetEase(Ease.OutQuad).WaitForCompletion();
                yield return new WaitForSeconds(0.5f);
                ShowError(_playfabError);
                yield break;
            }

            if (_playfabReady && elapsed >= MIN_LOAD_SECONDS)
            {
                yield return firstLoadingFill.DOFillAmount(1f, 0.35f).SetEase(Ease.OutQuad).WaitForCompletion();
                yield return new WaitForSeconds(0.2f);
                onComplete?.Invoke();
                yield break;
            }

            float naturalTarget = Mathf.Clamp01(elapsed / MIN_LOAD_SECONDS) * 0.9f;
            if (firstLoadingFill.fillAmount < naturalTarget)
                firstLoadingFill.fillAmount = Mathf.Lerp(firstLoadingFill.fillAmount, naturalTarget, Time.deltaTime * 3f);

            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Splash Animation
    // ─────────────────────────────────────────────────────────────

    private IEnumerator PlaySplashAnimation()
    {
        firstLoadingFill.fillAmount = 0f;
        splashScreenLogo.localScale = Vector3.zero;
        splashScreenLogo.DOScale(new Vector3(2.4f, 2f, 2f), 0.8f).SetEase(Ease.OutBack);
        firstLoadingParent.anchoredPosition = new Vector2(firstLoadingParent.anchoredPosition.x, -250f);
        firstLoadingParent.DOAnchorPosY(-126.2f, 0.6f).SetEase(Ease.OutCubic);
        yield return new WaitForSeconds(0.65f);
    }

    // ─────────────────────────────────────────────────────────────
    //  Login Path Decision
    // ─────────────────────────────────────────────────────────────

    private void DecideLoginPath()
    {
        if (HasCachedCredentials())
        {
            string email = PlayerPrefs.GetString(CACHED_EMAIL_KEY);
            string pass = PlayerPrefs.GetString(CACHED_PASS_KEY);
            ResetPlayFabState();
            LoginWithPlayFab(email, pass, onSuccess: null);
            StartCoroutine(PlayFabAwareLoadingFlow(onComplete: GoToMainMenu));
        }
        else
        {
            splashAndLoading.SetActive(false);
            TurnOnSignUpPanel();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  FLOW 2 — Signup
    // ─────────────────────────────────────────────────────────────

    public void OnSignupButtonPressed()
    {
        string email = signupEmailInput.text.Trim();
        string pass = signupPassField.text;

        if (!IsValidEmail(email)) { ShowInlineError("Please enter a valid email address."); return; }
        if (pass.Length < 6) { ShowInlineError("Password must be at least 6 characters."); return; }

        signupPanel.SetActive(false);
        errorPanel.TurnOff();
        splashAndLoading.SetActive(true);
        mainSplashPanel.SetActive(true);
        ResetPlayFabState();

        RegisterWithPlayFab(email, pass, onSuccess: () =>
        {
            CacheCredentials(email, pass);
            LoginWithPlayFab(email, pass, onSuccess: null);
        });

        StartCoroutine(PlayFabAwareLoadingFlow(onComplete: GoToMainMenu));
    }

    // ─────────────────────────────────────────────────────────────
    //  FLOW 3 — Go To Main Menu
    // ─────────────────────────────────────────────────────────────

    private void GoToMainMenu()
    {
        splashAndLoading.SetActive(false);
        mainSplashPanel.SetActive(false);
        mainMenuPanel.SetActive(true);

        // Chain: profile → stats → currencies
        FetchProfileAndStats();
    }

    // ─────────────────────────────────────────────────────────────
    //  POST-LOGIN FETCH CHAIN
    //  Step 1: Display Name
    //  Step 2: Player Data (stats)  — inside step 1 callback
    //  Step 3: Currencies           — inside step 2 callback
    // ─────────────────────────────────────────────────────────────

    // Step 1: Fetch Display Name (and show set-name panel if missing)
    private void FetchProfileAndStats()
    {
        PlayFabClientAPI.GetPlayerProfile(
            new GetPlayerProfileRequest
            {
                ProfileConstraints = new PlayerProfileViewConstraints { ShowDisplayName = true }
            },
            profileResult =>
            {
                string displayName = profileResult.PlayerProfile?.DisplayName;
                if (string.IsNullOrEmpty(displayName))
                    setNamePanel.TurnOn();
                else
                    SetTextArray(displayNameTexts, displayName);

                // Step 2
                FetchPlayerStats();
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] GetPlayerProfile: " + error.GenerateErrorReport());
                FetchPlayerStats(); // still proceed
            });
    }

    



    // ─────────────────────────────────────────────────────────────
    //  Fetch Player Data (all stat keys in one call)
    // ─────────────────────────────────────────────────────────────

    private void FetchPlayerStats()
    {
        FetchProfileImage();

        var keys = new List<string>
        {
            KEY_PLAYER_STATS
        };

        PlayFabClientAPI.GetUserData(
            new GetUserDataRequest
            {
                Keys = keys
            },
            result =>
            {
                var data = result.Data ?? new Dictionary<string, UserDataRecord>();

                if (data.ContainsKey(KEY_PLAYER_STATS))
                {
                    string json = data[KEY_PLAYER_STATS].Value;

                    if (!string.IsNullOrEmpty(json))
                    {
                        PlayerStatsData stats =
                            JsonUtility.FromJson<PlayerStatsData>(json);

                        if (stats != null)
                        {
                            _playerLevel = stats.playerLevel;
                            _playerXP = stats.playerXP;
                            _playerWin = stats.playerWin;

                            _totalWinCoins = stats.totalWinCoins;
                            _totalGamePoints = stats.totalGamePoints;
                            _totalWinGP = stats.totalWinGP;

                            _totalGamesPlayed = stats.totalGamesPlayed;
                            _totalGamesWon = stats.totalGamesWon;

                            _totalTournPlayed = stats.totalTournPlayed;
                            _totalTournWon = stats.totalTournWon;

                            _totalTrophy = stats.totalTrophy;
                        }
                    }
                }
                else
                {
                    // First time player defaults
                    _playerLevel = 1;
                    _playerXP = 0;
                    _playerWin = 0;

                    _totalWinCoins = 0;
                    _totalGamePoints = 0;
                    _totalWinGP = 0;

                    _totalGamesPlayed = 0;
                    _totalGamesWon = 0;

                    _totalTournPlayed = 0;
                    _totalTournWon = 0;

                    _totalTrophy = 0;

                    SaveStatsToServer();
                }

                // Load account info
                _playerEmail = PlayerPrefs.GetString(CACHED_EMAIL_KEY, "N/A");

                SetTextArray(emailTexts, _playerEmail);

                // Refresh stats UI
                RefreshAllStatTexts();

                // Fetch currencies
                FetchCurrencies();

                // Optional: If you still want PlayFab to provide the ID
                LoadAccountInfo();
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] GetUserData Error:\n" +
                               error.GenerateErrorReport());

                // Safe fallback values
                _playerLevel = 1;
                _playerXP = 0;
                _playerWin = 0;

                _totalWinCoins = 0;
                _totalGamePoints = 0;
                _totalWinGP = 0;

                _totalGamesPlayed = 0;
                _totalGamesWon = 0;

                _totalTournPlayed = 0;
                _totalTournWon = 0;

                _totalTrophy = 0;

                _playerEmail = PlayerPrefs.GetString(CACHED_EMAIL_KEY, "N/A");

                SetTextArray(emailTexts, _playerEmail);

                RefreshAllStatTexts();
                FetchCurrencies();

                // Still try to get PlayFab ID
                LoadAccountInfo();
            });
    }
    
    private void LoadAccountInfo()
    {
        PlayFabClientAPI.GetAccountInfo(
            new GetAccountInfoRequest(),
            result =>
            {
                _playFabId = result.AccountInfo.PlayFabId;
                _playerEmail = result.AccountInfo.PrivateInfo?.Email ?? "N/A";

                SetTextArray(playFabIdTexts, _playFabId);
                SetTextArray(emailTexts, _playerEmail);

                RefreshAllStatTexts();
                FetchCurrencies();
            },
            error =>
            {
                SetTextArray(playFabIdTexts, "--");
                SetTextArray(emailTexts, "--");

                RefreshAllStatTexts();
                FetchCurrencies();
            });
    }

    

    // ─────────────────────────────────────────────────────────────
    //  Fetch Virtual Currencies
    // ─────────────────────────────────────────────────────────────

    private void FetchCurrencies()
    {
        PlayFabClientAPI.GetUserInventory(
            new GetUserInventoryRequest(),
            result =>
            {
                SetTextArray(gpTexts, result.VirtualCurrency.TryGetValue("GP", out int gp) ? gp.ToString() : "0");
                SetTextArray(pcTexts, result.VirtualCurrency.TryGetValue("PC", out int pc) ? pc.ToString() : "0");
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] GetUserInventory: " + error.GenerateErrorReport());
                SetTextArray(gpTexts, "--");
                SetTextArray(pcTexts, "--");
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  SET DISPLAY NAME
    // ─────────────────────────────────────────────────────────────

    public void OnSetNameButtonPressed()
    {
        string name = setNameInputField.text.Trim();
        if (string.IsNullOrEmpty(name)) { ShowInlineError("Please enter a display name."); return; }

        PlayFabClientAPI.UpdateUserTitleDisplayName(
            new UpdateUserTitleDisplayNameRequest { DisplayName = name },
            result =>
            {
                SetTextArray(displayNameTexts, result.DisplayName);
                setNamePanel.TurnOff();
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] SetDisplayName: " + error.GenerateErrorReport());
                ShowInlineError("Couldn't set name: " + error.ErrorMessage);
            });
    }

    // ═════════════════════════════════════════════════════════════
    //
    //  STAT MANIPULATION — all operate on LOCAL cache only.
    //  Call SaveStatsToServer() when you want to persist.
    //
    // ═════════════════════════════════════════════════════════════

    // ─── XP & Level ──────────────────────────────────────────────

    /// <summary>Add XP, auto-recalculate level, refresh UI.</summary>
    public void AddXP(int amount)
    {
        _playerXP = Mathf.Max(0, _playerXP + amount);
        RecalculateLevel();
        RefreshAllStatTexts();
    }

    /// <summary>Remove XP (floors at 0), auto-recalculate level, refresh UI.</summary>
    public void RemoveXP(int amount)
    {
        _playerXP = Mathf.Max(0, _playerXP - amount);
        RecalculateLevel();
        RefreshAllStatTexts();
    }

    /// <summary>
    /// Derives _playerLevel from _playerXP using LevelThresholds.
    /// Level is read-only outside this method — only XP drives it.
    /// </summary>
    private void RecalculateLevel()
    {
        int level = 1;
        for (int i = LevelThresholds.Length - 1; i >= 0; i--)
        {
            if (_playerXP >= LevelThresholds[i])
            {
                level = i + 1;
                break;
            }
        }
        _playerLevel = level;
    }

    // ─── Win Count ───────────────────────────────────────────────

    public void AddWin(int amount = 1)
    {
        _playerWin += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    public void RemoveWin(int amount = 1)
    {
        _playerWin = Mathf.Max(0, _playerWin - Mathf.Abs(amount));
        RefreshAllStatTexts();
    }

    // ─── Total Winning Coins (add only) ──────────────────────────

    public void AddWinningCoins(int amount)
    {
        _totalWinCoins += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    // ─── Total Game Points (add & remove) ────────────────────────

    public void AddGamePoints(int amount)
    {
        _totalGamePoints += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    public void RemoveGamePoints(int amount)
    {
        _totalGamePoints = Mathf.Max(0, _totalGamePoints - Mathf.Abs(amount));
        RefreshAllStatTexts();
    }

    // ─── Total Winning Golden Pass (add only) ────────────────────

    public void AddWinningGoldenPass(int amount = 1)
    {
        _totalWinGP += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    // ─── Total Games Played (add only) ───────────────────────────

    public void AddGamePlayed(int amount = 1)
    {
        _totalGamesPlayed += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    // ─── Total Games Won (add only) ──────────────────────────────

    public void AddGameWon(int amount = 1)
    {
        _totalGamesWon += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    // ─── Total Tournaments Played (add only) ─────────────────────

    public void AddTournamentPlayed(int amount = 1)
    {
        _totalTournPlayed += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    // ─── Total Tournaments Won (add only) ────────────────────────

    public void AddTournamentWon(int amount = 1)
    {
        _totalTournWon += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    // ─────────────────────────────────────────────────────────────
    //  READ-ONLY ACCESSORS  (useful for other scripts)
    // ─────────────────────────────────────────────────────────────

    public int PlayerLevel => _playerLevel;
    public int PlayerXP => _playerXP;
    public int PlayerWin => _playerWin;
    public int TotalWinCoins => _totalWinCoins;
    public int TotalGamePoints => _totalGamePoints;
    public int TotalWinGoldenPass => _totalWinGP;
    public int TotalGamesPlayed => _totalGamesPlayed;
    public int TotalGamesWon => _totalGamesWon;
    public int TotalTournPlayed => _totalTournPlayed;
    public int TotalTournWon => _totalTournWon;

    // ─────────────────────────────────────────────────────────────
    //  XP → Level utility (static, for UI previews etc.)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Returns XP needed to reach the NEXT level from current XP.</summary>
    public int XPToNextLevel()
    {
        if (_playerLevel >= LevelThresholds.Length) return 0; // max level
        return LevelThresholds[_playerLevel] - _playerXP;
    }

    // ─────────────────────────────────────────────────────────────
    //  SAVE STATS TO SERVER  (call this manually to batch-save)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends all local stat values to PlayFab in a single UpdateUserData call.
    /// Call at natural save points (end of game, scene transition, etc.)
    /// to minimise API requests.
    /// </summary>
    public void SaveStatsToServer()
    {
        PlayerStatsData stats = new PlayerStatsData
        {
            playerLevel = _playerLevel,
            playerXP = _playerXP,
            playerWin = _playerWin,

            totalWinCoins = _totalWinCoins,
            totalGamePoints = _totalGamePoints,
            totalWinGP = _totalWinGP,

            totalGamesPlayed = _totalGamesPlayed,
            totalGamesWon = _totalGamesWon,

            totalTournPlayed = _totalTournPlayed,
            totalTournWon = _totalTournWon,

            totalTrophy = _totalTrophy
        };

        string json = JsonUtility.ToJson(stats);

        PlayFabClientAPI.UpdateUserData(
            new UpdateUserDataRequest
            {
                Data = new Dictionary<string, string>
                {
                    { KEY_PLAYER_STATS, json }
                }
            },
            result =>
            {
                Debug.Log("[PlayFabManager] Stats saved successfully.");
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] SaveStats error: " + error.GenerateErrorReport());
                ShowError("Failed to save stats to server. Please check your connection.");
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  UI REFRESH
    // ─────────────────────────────────────────────────────────────

    private void RefreshAllStatTexts()
    {
        SetTextArray(playerLevelTexts, _playerLevel.ToString());
        SetTextArray(playerXPTexts, _playerXP.ToString());
        SetTextArray(playerWinTexts, _playerWin.ToString());
        SetTextArray(totalWinningCoinTexts, _totalWinCoins.ToString());
        SetTextArray(totalGamePointsTexts, _totalGamePoints.ToString());
        SetTextArray(totalWinningGoldenPassTexts, _totalWinGP.ToString());
        SetTextArray(totalGamesPlayedTexts, _totalGamesPlayed.ToString());
        SetTextArray(totalGamesWonTexts, _totalGamesWon.ToString());
        SetTextArray(totalTournamentsPlayedTexts, _totalTournPlayed.ToString());
        SetTextArray(totalTournamentsWonTexts, _totalTournWon.ToString());
        SetTextArray(totalTrophyTexts, _totalTrophy.ToString());


        RefreshXPFill();
    }
    private void RefreshXPFill()
    {
        if (xpFillImage == null) return;

        // XP range for current level
        int xpMin = LevelThresholds[_playerLevel - 1];
        int xpMax = _playerLevel < LevelThresholds.Length
            ? LevelThresholds[_playerLevel]
            : LevelThresholds[LevelThresholds.Length - 1]; // max level cap

        float fill = Mathf.InverseLerp(xpMin, xpMax, _playerXP);
        xpFillImage.fillAmount = fill;
    }

    private void SetTextArray(TMP_Text[] targets, string value)
    {
        if (targets == null) return;
        foreach (var t in targets)
            if (t != null) t.text = value;
    }

    // ─────────────────────────────────────────────────────────────
    //  PlayFab — Register / Login
    // ─────────────────────────────────────────────────────────────

    private void RegisterWithPlayFab(string email, string password, Action onSuccess)
    {
        PlayFabClientAPI.RegisterPlayFabUser(
            new RegisterPlayFabUserRequest
            {
                Email = email,
                Password = password,
                RequireBothUsernameAndEmail = false
            },
            result =>
            {
                Debug.Log("[PlayFabManager] Registered: " + result.PlayFabId);
                onSuccess?.Invoke();
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] Register error: " + error.GenerateErrorReport());
                ShowError(FriendlyError(error));
                if (error.Error == PlayFabErrorCode.EmailAddressNotAvailable ||
                    error.Error == PlayFabErrorCode.UsernameNotAvailable)
                {
                    CacheCredentials(email, password);
                    LoginWithPlayFab(email, password, onSuccess: null);
                }
                else
                {
                    _playfabFailed = true;
                    _playfabError = FriendlyError(error);
                }
            });
    }

    private void LoginWithPlayFab(string email, string password, Action onSuccess)
    {
        PlayFabClientAPI.LoginWithEmailAddress(
            new LoginWithEmailAddressRequest { Email = email, Password = password },
            result =>
            {
                Debug.Log("[PlayFabManager] Logged in: " + result.PlayFabId);
                _playfabReady = true;
                onSuccess?.Invoke();
                

            },
            error =>
            {
                Debug.LogError("[PlayFabManager] Login error: " + error.GenerateErrorReport());
                ShowError(FriendlyError(error));
                _playfabFailed = true;
                _playfabError = FriendlyError(error);
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Error Helpers
    // ─────────────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        errorPanel.TurnOn();
        errorTitle.text = message;
    }

    private void ShowInlineError(string message)
    {
        errorPanel.TurnOn();
        errorTitle.text = message;
    }

    private string FriendlyError(PlayFabError error) => error.Error switch
    {
        PlayFabErrorCode.InvalidEmailAddress => "Invalid email address.",
        PlayFabErrorCode.InvalidPassword => "Incorrect password. Please try again.",
        PlayFabErrorCode.AccountNotFound => "No account found with that email.",
        PlayFabErrorCode.EmailAddressNotAvailable => "That email is already registered.",
        PlayFabErrorCode.ConnectionError => "No internet connection. Please check your network and try again.",
        _ => "Something went wrong. Please try again.\n(" + error.ErrorMessage + ")"
    };

    // ─────────────────────────────────────────────────────────────
    //  Credential Cache
    // ─────────────────────────────────────────────────────────────

    private bool HasCachedCredentials() =>
        PlayerPrefs.HasKey(CACHED_EMAIL_KEY) &&
        PlayerPrefs.HasKey(CACHED_PASS_KEY) &&
        !string.IsNullOrEmpty(PlayerPrefs.GetString(CACHED_EMAIL_KEY));

    private void CacheCredentials(string email, string password)
    {
        PlayerPrefs.SetString(CACHED_EMAIL_KEY, email);
        PlayerPrefs.SetString(CACHED_PASS_KEY, password);
        PlayerPrefs.Save();
    }

    public void ClearCachedCredentials()
    {
        PlayerPrefs.DeleteKey(CACHED_EMAIL_KEY);
        PlayerPrefs.DeleteKey(CACHED_PASS_KEY);
        PlayerPrefs.Save();
    }

    // ─────────────────────────────────────────────────────────────
    //  Misc Helpers
    // ─────────────────────────────────────────────────────────────

    private void ResetPlayFabState()
    {
        _playfabReady = false;
        _playfabFailed = false;
        _playfabError = "";
    }

    private bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) &&
        Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);

    private void SetAllPanelsOff()
    {
        if (splashAndLoading) splashAndLoading.SetActive(false);
        if (mainSplashPanel) mainSplashPanel.SetActive(false);
        if (signupPanel) signupPanel.SetActive(false);
        if (mainMenuPanel) mainMenuPanel.SetActive(false);
        if (errorPanel) errorPanel.TurnOff();
        if (setNamePanel) setNamePanel.TurnOff();
    }

    private void CheckInternetConnection()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            _playfabFailed = true;
            _playfabError = "No internet connection.\nPlease check your network and try again.";
        }
        else
        {
            _playfabReady = true;
        }
    }

    public void RetryConnection()
    {
        errorPanel.TurnOn();
        ResetPlayFabState();
        StartCoroutine(InitialLoadingFlow());
    }

    public void TurnOnSignUpPanel()
    {
        // If login is currently active
        if (loginPanel.activeSelf)
        {
            middlePanelForLogin.transform
                .DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack)
                .OnComplete(() =>
                {
                    loginPanel.SetActive(false);

                    signupPanel.SetActive(true);

                    middlePanelForSignUp.transform.localScale = Vector3.zero;

                    middlePanelForSignUp.transform
                        .DOScale(Vector3.one, 0.35f)
                        .SetEase(Ease.OutBack);
                });
        }
        else
        {
            signupPanel.SetActive(true);

            middlePanelForSignUp.transform.localScale = Vector3.zero;

            middlePanelForSignUp.transform
                .DOScale(Vector3.one, 0.35f)
                .SetEase(Ease.OutBack);
        }
    }

    public void TurnOnLoginPanel()
    {
        // If signup is currently active
        if (signupPanel.activeSelf)
        {
            middlePanelForSignUp.transform
                .DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack)
                .OnComplete(() =>
                {
                    signupPanel.SetActive(false);

                    loginPanel.SetActive(true);

                    middlePanelForLogin.transform.localScale = Vector3.zero;

                    middlePanelForLogin.transform
                        .DOScale(Vector3.one, 0.35f)
                        .SetEase(Ease.OutBack);
                });
        }
        else
        {
            loginPanel.SetActive(true);

            middlePanelForLogin.transform.localScale = Vector3.zero;

            middlePanelForLogin.transform
                .DOScale(Vector3.one, 0.35f)
                .SetEase(Ease.OutBack);
        }
    }


    public void AddTrophy(int amount)
    {
        _totalTrophy += Mathf.Abs(amount);
        RefreshAllStatTexts();
    }

    public void RemoveTrophy(int amount)
    {
        _totalTrophy = Mathf.Max(0, _totalTrophy - Mathf.Abs(amount));
        RefreshAllStatTexts();
    }

    public void UpdateTrophyLeaderboard()
    {
        PlayFabClientAPI.UpdatePlayerStatistics(
            new UpdatePlayerStatisticsRequest
            {
                Statistics = new List<StatisticUpdate>()
                {
                new StatisticUpdate
                {
                    StatisticName = "Trophy",
                    Value = _totalTrophy
                }
                }
            },
            result =>
            {
                Debug.Log("Trophy Leaderboard Updated");
            },
            error =>
            {
                Debug.LogError(error.GenerateErrorReport());
                ShowError("Failed to update trophy leaderboard. Please check your connection.");
            });
    }

    public void ClearLeaderboard()
    {
        foreach (Transform child in leaderboardContent)
        {
            Destroy(child.gameObject);
        }

        leaderboardContent.sizeDelta =
            new Vector2(
                leaderboardContent.sizeDelta.x,
                0);
    }

    public void ShowLeaderboard()
    {
        leaderboardPanel.SetActive(true);

        ClearLeaderboard();

        GetMyLeaderboardPosition();
    }


    public void GetMyLeaderboardPosition()
    {
        PlayFabClientAPI.GetLeaderboardAroundPlayer(
            new GetLeaderboardAroundPlayerRequest
            {
                StatisticName = "Trophy",
                MaxResultsCount = 1
            },
            result =>
            {
                if (result.Leaderboard.Count > 0)
                {
                    SpawnMyCard(result.Leaderboard[0]);
                }

                LoadTopLeaderboard();
            },
            error =>
            {
                Debug.LogError(error.GenerateErrorReport());
            });
    }

    

    private void LoadTopLeaderboard()
    {
        PlayFabClientAPI.GetLeaderboard(
            new GetLeaderboardRequest
            {
                StatisticName = "Trophy",
                StartPosition = 0,
                MaxResultsCount = 100
            },
            result =>
            {
                foreach (var player in result.Leaderboard)
                {
                    SpawnLeaderboardPlayer(player);
                }
            },
            error =>
            {
                Debug.LogError(error.GenerateErrorReport());
            });
    }

    

    private void IncreaseLeaderboardHeight()
    {
        leaderboardContent.sizeDelta =
            new Vector2(
                leaderboardContent.sizeDelta.x,
                leaderboardContent.sizeDelta.y + 33f);
    }

    //turn off the leaderboard panel
    public void CloseLeaderboard()
    {
        ClearLeaderboard();
        leaderboardPanel.SetActive(false);
    }

    private void SpawnMyCard(PlayerLeaderboardEntry entry)
    {
        FetchAvatarThenSpawnCard(entry, isMe: true);
    }

    private void SpawnLeaderboardPlayer(PlayerLeaderboardEntry entry)
    {
        FetchAvatarThenSpawnCard(entry, isMe: false);
    }

    private void FetchAvatarThenSpawnCard(PlayerLeaderboardEntry entry, bool isMe)
    {
        PlayFabClientAPI.GetPlayerProfile(
            new GetPlayerProfileRequest
            {
                PlayFabId = entry.PlayFabId,
                ProfileConstraints = new PlayerProfileViewConstraints
                {
                    ShowAvatarUrl = true
                }
            },
            result =>
            {
                string avatarUrl = result.PlayerProfile?.AvatarUrl;
                InstantiateLeaderboardCard(entry, isMe, avatarUrl);
            },
            error =>
            {
                InstantiateLeaderboardCard(entry, isMe, null);
            });
    }

    private void InstantiateLeaderboardCard(PlayerLeaderboardEntry entry, bool isMe, string avatarUrl)
    {
        GameObject prefab = isMe ? myLeaderboardCardPrefab : otherLeaderboardCardPrefab;
        GameObject obj = Instantiate(prefab, leaderboardContent);
        LeaderboardCard card = obj.GetComponent<LeaderboardCard>();
        card.Setup(entry.Position + 1, 0, entry.DisplayName, entry.StatValue, avatarUrl, entry.PlayFabId, this);
        IncreaseLeaderboardHeight();
    }


    public void ShowFriendLeaderboard()
    {
        leaderboardPanel.SetActive(true);
        ClearLeaderboard();

        // Step 1: get my position among friends first
        GetMyFriendLeaderboardPosition();
    }

    private void GetMyFriendLeaderboardPosition()
    {
        PlayFabClientAPI.GetFriendLeaderboardAroundPlayer(
            new GetFriendLeaderboardAroundPlayerRequest
            {
                StatisticName = "Trophy",
                MaxResultsCount = 1
            },
            result =>
            {
                if (result.Leaderboard.Count > 0)
                    SpawnMyCard(result.Leaderboard[0]); // reuses your existing SpawnMyCard

                LoadFriendLeaderboard();
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] GetFriendLeaderboardAroundPlayer: " + error.GenerateErrorReport());
                LoadFriendLeaderboard(); // still proceed
            });
    }

    private void LoadFriendLeaderboard()
    {
        PlayFabClientAPI.GetFriendLeaderboard(
            new GetFriendLeaderboardRequest
            {
                StatisticName = "Trophy",
                StartPosition = 0,
                MaxResultsCount = 100
            },
            result =>
            {
                foreach (var player in result.Leaderboard)
                    SpawnLeaderboardPlayer(player); // reuses your existing SpawnLeaderboardPlayer
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] GetFriendLeaderboard: " + error.GenerateErrorReport());
            });
    }

    //Demo function to set random trophy value for testing leaderboard
    public void SetRandomTrophyValue()
    {
        int randomTrophy = UnityEngine.Random.Range(0, 1000);
        _totalTrophy = randomTrophy;
        RefreshAllStatTexts();
        UpdateTrophyLeaderboard();
    }

    public void OnSelectProfileImage()
    {
        NativeGallery.GetImageFromGallery((path) => {
            if (path != null)
            {
                byte[] imageBytes = System.IO.File.ReadAllBytes(path);
                StartCoroutine(UploadToCloudinary(imageBytes));
            }
        }, "Select a profile picture", "image/*");
    }

    IEnumerator UploadToCloudinary(byte[] imageBytes)
    {
        // ── Compress to 512×512 before uploading ──
        Texture2D original = new Texture2D(2, 2);
        original.LoadImage(imageBytes);

        // Scale down to 512×512
        Texture2D resized = ResizeTexture(original, 128, 128);
        byte[] compressedBytes = resized.EncodeToJPG(75); // 75% quality JPEG

        Destroy(original);
        Destroy(resized);

        WWWForm form = new WWWForm();
        form.AddBinaryData("file", compressedBytes, "profile.jpg", "image/jpeg");
        form.AddField("upload_preset", "unity_unsigned_upload");
        string url = "https://api.cloudinary.com/v1_1/deul32vgn/image/upload";

        UnityWebRequest www = UnityWebRequest.Post(url, form);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<CloudinaryResponse>(www.downloadHandler.text);
            SaveImageUrlToPlayFab(response.secure_url);
            ShowInlineError("Profile picture updated successfully.");
        }
        else
        {
            ShowInlineError("Upload failed: " + www.error);
        }
    }



    private Texture2D ResizeTexture(Texture2D source, int width, int height)
    {
        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        RenderTexture.active = rt;
        Graphics.Blit(source, rt);
        Texture2D result = new Texture2D(width, height, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    private void SaveImageUrlToPlayFab(string url)
    {
        PlayFabClientAPI.UpdateAvatarUrl(
            new UpdateAvatarUrlRequest { ImageUrl = url },
            result =>
            {
                Debug.Log("[PlayFabManager] Avatar URL saved.");
                FetchProfileImage();
            },
            error => ShowInlineError("Couldn't save avatar URL: " + error.ErrorMessage));
    }

    // Load from PlayFab Avatar URL (not UserData)
    private void FetchProfileImage()
    {
        PlayFabClientAPI.GetPlayerProfile(
            new GetPlayerProfileRequest
            {
                ProfileConstraints = new PlayerProfileViewConstraints
                {
                    ShowAvatarUrl = true
                }
            },
            result =>
            {
                string avatarUrl = result.PlayerProfile?.AvatarUrl;
                if (!string.IsNullOrEmpty(avatarUrl))
                    StartCoroutine(LoadImage(avatarUrl));
                else
                {
                    // No URL — apply default immediately
                    foreach (var img in profileImages)
                        if (img != null) img.sprite = defaultProfileSprite;
                }
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] FetchProfileImage: " + error.GenerateErrorReport());
                foreach (var img in profileImages)
                    if (img != null) img.sprite = defaultProfileSprite;
            });
    }

    IEnumerator LoadImage(string url)
    {
        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Texture2D texture = DownloadHandlerTexture.GetContent(www);
            Sprite sprite = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));

            foreach (var img in profileImages)
                if (img != null) img.sprite = sprite;
        }
        else
        {
            // Load default
            foreach (var img in profileImages)
                if (img != null) img.sprite = defaultProfileSprite;
        }
    }

    public void PublicMatch()
    {
        MatchmakingData.Instance.PlayerName =
            string.IsNullOrEmpty(displayNameTexts[0].text)
                ? "Anonymous"
                : displayNameTexts[0].text;

        MatchmakingData.Instance.IsPrivateMatch = false;

        UnityEngine.SceneManagement.SceneManager.LoadScene("EuchreGame");
    }
    
    public void ProceedPrivateRoom()
    {
        string roomCode = roomCodeInput.text.Trim();

        if (string.IsNullOrEmpty(roomCode))
        {
            ShowInlineError("Enter room code.");
            return;
        }

        MatchmakingData.Instance.RoomCode = roomCode;
        MatchmakingData.Instance.IsPrivateMatch = true;

        teamSelectionPanel.SetActive(true);
    }
    
    private void StartPrivateGame(int teamIndex)
    {
        MatchmakingData.Instance.PlayerName =
            string.IsNullOrEmpty(displayNameTexts[0].text)
                ? "Anonymous"
                : displayNameTexts[0].text;

        MatchmakingData.Instance.TeamIndex = teamIndex;

        UnityEngine.SceneManagement.SceneManager.LoadScene("EuchreGame");
    }
    
    public void SelectTeam1()
    {
        StartPrivateGame(0);
    }

    public void SelectTeam2()
    {
        StartPrivateGame(1);
    }

    public void OpenFriendPanel()
    {
        friendPanel.SetActive(true);
        RefreshFriendIds(onDone: null); // pre-load friend list silently
    }

    public void CloseFriendPanel()
    {
        friendPanel.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  SEARCH — hook to your Search button's onClick
    // ─────────────────────────────────────────────────────────────
    public void OnSearchButtonPressed()
    {
        string query = searchInput.text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        ClearContent();

        // PlayFab lets you look up by DisplayName, Email, or PlayFabId
        var request = new GetAccountInfoRequest
        {
            // Try all three — PlayFab will use whichever is non-null
            // but only one should be set at a time ideally.
            // We detect which one the user typed:
        };

        if (query.Contains("@"))          // looks like an email
            request.Email = query;
        else if (query.Length == 16 &&    // PlayFab IDs are 16-char hex
                 System.Text.RegularExpressions.Regex.IsMatch(query, @"^[0-9A-Fa-f]+$"))
            request.PlayFabId = query;
        else
            request.TitleDisplayName = query; // display name

        PlayFabClientAPI.GetAccountInfo(request,
            result =>
            {
                var account = result.AccountInfo;
                string foundId = account.PlayFabId;
                string foundName = account.TitleInfo?.DisplayName ?? "Unknown";
                string imageUrl = null;

                // Fetch their profile image URL from their UserData
                FetchOtherPlayerImageThenSpawn(foundId, foundName, imageUrl);
            },
            error =>
            {
                Debug.LogError("[FriendManager] Search failed: " + error.GenerateErrorReport());
                ShowInlineError("No player found with that identifier.");
                // Optionally show an error in your existing errorPanel here
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  SHOW ALL FRIENDS — call from "My Friends" button
    // ─────────────────────────────────────────────────────────────
    

    public void ShowAllFriends()
    {
        ClearContent();

        PlayFabClientAPI.GetFriendsList(
            new GetFriendsListRequest
            {
                //IncludeSteamFriends = false,
                //IncludeFacebookFriends = false
            },
            result =>
            {
                _friendIds.Clear();

                foreach (var friend in result.Friends)
                {
                    _friendIds.Add(friend.FriendPlayFabId);
                    string name = friend.TitleDisplayName ?? friend.FriendPlayFabId;

                    // Same image fetch flow as search — fetches AvatarUrl then spawns
                    FetchOtherPlayerImageThenSpawn(friend.FriendPlayFabId, name, null);
                }
            },
            error =>
            {
                Debug.LogError("[FriendManager] GetFriendsList: " + error.GenerateErrorReport());
                //Show error in your existing errorPanel here if desired
                ShowError("Failed to load friends list. Please try again later.");
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  ADD FRIEND
    // ─────────────────────────────────────────────────────────────
    public void AddFriend(string friendPlayFabId, FriendCard card)
    {
        PlayFabClientAPI.AddFriend(
            new AddFriendRequest
            {
                FriendPlayFabId = friendPlayFabId
            },
            result =>
            {
                Debug.Log("[FriendManager] Friend added: " + friendPlayFabId);
                if (!_friendIds.Contains(friendPlayFabId))
                    _friendIds.Add(friendPlayFabId);
                card.SetFriendStatus(true);
            },
            error =>
            {
                Debug.LogError("[FriendManager] AddFriend error: " + error.GenerateErrorReport());
                ShowInlineError("Failed to add friend. Please try again.");
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  REMOVE FRIEND
    // ─────────────────────────────────────────────────────────────
    public void RemoveFriend(string friendPlayFabId, FriendCard card)
    {
        PlayFabClientAPI.RemoveFriend(
            new RemoveFriendRequest
            {
                FriendPlayFabId = friendPlayFabId
            },
            result =>
            {
                Debug.Log("[FriendManager] Friend removed: " + friendPlayFabId);
                _friendIds.Remove(friendPlayFabId);
                card.SetFriendStatus(false);
            },
            error =>
            {
                Debug.LogError("[FriendManager] RemoveFriend error: " + error.GenerateErrorReport());
                ShowInlineError("Failed to remove friend. Please try again.");
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  INTERNAL HELPERS
    // ─────────────────────────────────────────────────────────────

    // Refresh the local _friendIds cache, then call onDone
    private void RefreshFriendIds(System.Action onDone)
    {
        PlayFabClientAPI.GetFriendsList(
            new GetFriendsListRequest
            {
                //IncludeSteamFriends = false,
                //IncludeFacebookFriends = false
            },
            result =>
            {
                _friendIds.Clear();
                foreach (var f in result.Friends)
                    _friendIds.Add(f.FriendPlayFabId);
                onDone?.Invoke();
            },
            error =>
            {
                Debug.LogError("[FriendManager] RefreshFriendIds: " + error.GenerateErrorReport());
                ShowError("Failed to refresh friends list. Please check your connection.");
                onDone?.Invoke();
            });
    }

    // Fetches the other player's ProfileImageUrl from their public UserData, then spawns the card
    private void FetchOtherPlayerImageThenSpawn(string playFabId, string displayName, string fallbackUrl)
    {
        // AvatarUrl is a built-in profile field — readable for any player
        // without needing "allow clients to read other players' data" setting
        PlayFabClientAPI.GetPlayerProfile(
            new GetPlayerProfileRequest
            {
                PlayFabId = playFabId,
                ProfileConstraints = new PlayerProfileViewConstraints
                {
                    ShowAvatarUrl = true,
                    ShowDisplayName = true
                }
            },
            result =>
            {
                string url = result.PlayerProfile?.AvatarUrl;
                string name = result.PlayerProfile?.DisplayName ?? displayName;

                if (!string.IsNullOrEmpty(url))
                    StartCoroutine(LoadImageThenSpawn(url, playFabId, name));
                else
                    SpawnCard(playFabId, name, _friendIds.Contains(playFabId), null);
            },
            error =>
            {
                Debug.LogError("[FriendManager] GetPlayerProfile: " + error.GenerateErrorReport());
                SpawnCard(playFabId, displayName, _friendIds.Contains(playFabId), null);
            });
    }

    private IEnumerator LoadImageThenSpawn(string url, string playFabId, string displayName)
    {
        Texture2D tex = null;

        if (!string.IsNullOrEmpty(url))
        {
            UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
                tex = DownloadHandlerTexture.GetContent(www);
        }

        // tex will be null if failed — FriendCard handles default internally
        SpawnCard(playFabId, displayName, _friendIds.Contains(playFabId), tex);
    }

    private void SpawnCard(string playFabId, string displayName, bool isFriend, Texture2D tex)
    {
        GameObject obj = Instantiate(friendCardPrefab, scrollContent);
        FriendCard card = obj.GetComponent<FriendCard>();
        card.Setup(displayName, playFabId, isFriend, tex, this);

        // Expand scroll content width by 100 per card (for horizontal layout)
        scrollContent.sizeDelta = new Vector2(
            scrollContent.sizeDelta.x + 0f,
            scrollContent.sizeDelta.y);
    }

    private void ClearContent()
    {
        foreach (Transform child in scrollContent)
            Destroy(child.gameObject);

        scrollContent.sizeDelta = new Vector2(0f, scrollContent.sizeDelta.y);
    }

    public void OpenOtherPlayerProfile(string playFabId)
    {
        PlayFabClientAPI.ExecuteCloudScript(
            new ExecuteCloudScriptRequest
            {
                FunctionName = "GetOtherPlayerProfile",
                FunctionParameter = new { playFabId = playFabId },
                GeneratePlayStreamEvent = false
            },
            result =>
            {
                if (result.FunctionResult == null)
                {
                    Debug.LogError("[PlayFabManager] CloudScript returned null.");
                    ShowError("Failed to retrieve player profile. Please try again.");
                    return;
                }

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(result.FunctionResult);
                Debug.Log("[PlayFabManager] CloudScript raw result: " + json);

                CloudScriptGetOtherPlayerResult raw =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<CloudScriptGetOtherPlayerResult>(json);

                if (raw == null)
                {
                    Debug.LogError("[PlayFabManager] Failed to deserialize CloudScript result.");
                    ShowError("Failed to retrieve player profile. Please try again.");
                    return;
                }

                OtherPlayerData data = new OtherPlayerData
                {
                    displayName = raw.displayName,
                    avatarUrl = raw.avatarUrl,
                    playFabId = raw.playFabId,
                    email = raw.email,
                    stats = new OtherPlayerStats
                    {
                        playerLevel = raw.playerLevel,
                        playerXP = raw.playerXP,
                        totalGamesPlayed = raw.totalGamesPlayed,
                        totalGamesWon = raw.totalGamesWon,
                        totalTournPlayed = raw.totalTournPlayed,
                        totalTournWon = raw.totalTournWon
                    }
                };

                otherPlayerInfoPanel.Show(data);
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] OpenOtherPlayerProfile: " +
                               error.GenerateErrorReport());
                ShowError("Failed to retrieve player profile. Please try again.");
            });
    }

}