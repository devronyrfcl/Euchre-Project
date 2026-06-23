using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using PlayFab;
using PlayFab.ClientModels;

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

    [Header("=== ROOT PANELS ===")]
    [SerializeField] private GameObject splashAndLoading;
    [SerializeField] private GameObject mainSplashPanel;
    [SerializeField] private GameObject signupPanel;
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject errorPanel;

    [Header("=== SET NAME PANEL ===")]
    [SerializeField] private GameObject setNamePanel;
    [SerializeField] private TMP_InputField setNameInputField;

    [Header("=== SPLASH / LOADING ===")]
    [SerializeField] private Transform splashScreenLogo;
    [SerializeField] private RectTransform firstLoadingParent;
    [SerializeField] private Image firstLoadingFill;

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


    // ─────────────────────────────────────────────────────────────
    //  Constants
    // ─────────────────────────────────────────────────────────────

    private const string CACHED_EMAIL_KEY = "PlayFab_CachedEmail";
    private const string CACHED_PASS_KEY = "PlayFab_CachedPass";
    private const float MIN_LOAD_SECONDS = 3f;

    // PlayFab data keys
    private const string KEY_LEVEL = "Player_Level";
    private const string KEY_XP = "Player_XP";
    private const string KEY_WIN = "Player_Win";
    private const string KEY_WIN_COINS = "Stat_WinCoins";
    private const string KEY_GAME_PTS = "Stat_GamePoints";
    private const string KEY_WIN_GP = "Stat_WinGoldenPass";
    private const string KEY_GAMES_PLAYED = "Stat_GamesPlayed";
    private const string KEY_GAMES_WON = "Stat_GamesWon";
    private const string KEY_TOURN_PLAYED = "Stat_TournPlayed";
    private const string KEY_TOURN_WON = "Stat_TournWon";

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

    // ─────────────────────────────────────────────────────────────
    //  PlayFab flow state
    // ─────────────────────────────────────────────────────────────

    private bool _playfabReady = false;
    private bool _playfabFailed = false;
    private string _playfabError = "";

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake() => DOTween.Init();

    private void Start()
    {
        SetAllPanelsOff();
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

        ResetPlayFabState();
        CheckInternetConnection();

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
                DecideLoginPath();
                yield break;
            }

            float naturalTarget = Mathf.Clamp01(elapsed / MIN_LOAD_SECONDS) * 0.9f;
            if (firstLoadingFill.fillAmount < naturalTarget)
                firstLoadingFill.fillAmount = Mathf.Lerp(firstLoadingFill.fillAmount, naturalTarget, Time.deltaTime * 3f);

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
            signupPanel.SetActive(true);
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
        errorPanel.SetActive(false);
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
                    setNamePanel.SetActive(true);
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
        var keys = new List<string>
        {
            KEY_LEVEL, KEY_XP, KEY_WIN,
            KEY_WIN_COINS, KEY_GAME_PTS, KEY_WIN_GP,
            KEY_GAMES_PLAYED, KEY_GAMES_WON,
            KEY_TOURN_PLAYED, KEY_TOURN_WON
        };

        PlayFabClientAPI.GetUserData(
            new GetUserDataRequest { Keys = keys },
            result =>
            {
                var data = result.Data;
                bool anyMissing = false;

                // ── Core progression ──────────────────────────────
                if (data.ContainsKey(KEY_LEVEL))
                    int.TryParse(data[KEY_LEVEL].Value, out _playerLevel);
                else { _playerLevel = 1; anyMissing = true; }

                if (data.ContainsKey(KEY_XP))
                    int.TryParse(data[KEY_XP].Value, out _playerXP);
                else { _playerXP = 0; anyMissing = true; }

                if (data.ContainsKey(KEY_WIN))
                    int.TryParse(data[KEY_WIN].Value, out _playerWin);
                else { _playerWin = 0; anyMissing = true; }

                // ── Extended stats ────────────────────────────────
                if (data.ContainsKey(KEY_WIN_COINS))
                    int.TryParse(data[KEY_WIN_COINS].Value, out _totalWinCoins);
                else { _totalWinCoins = 0; anyMissing = true; }

                if (data.ContainsKey(KEY_GAME_PTS))
                    int.TryParse(data[KEY_GAME_PTS].Value, out _totalGamePoints);
                else { _totalGamePoints = 0; anyMissing = true; }

                if (data.ContainsKey(KEY_WIN_GP))
                    int.TryParse(data[KEY_WIN_GP].Value, out _totalWinGP);
                else { _totalWinGP = 0; anyMissing = true; }

                if (data.ContainsKey(KEY_GAMES_PLAYED))
                    int.TryParse(data[KEY_GAMES_PLAYED].Value, out _totalGamesPlayed);
                else { _totalGamesPlayed = 0; anyMissing = true; }

                if (data.ContainsKey(KEY_GAMES_WON))
                    int.TryParse(data[KEY_GAMES_WON].Value, out _totalGamesWon);
                else { _totalGamesWon = 0; anyMissing = true; }

                if (data.ContainsKey(KEY_TOURN_PLAYED))
                    int.TryParse(data[KEY_TOURN_PLAYED].Value, out _totalTournPlayed);
                else { _totalTournPlayed = 0; anyMissing = true; }

                if (data.ContainsKey(KEY_TOURN_WON))
                    int.TryParse(data[KEY_TOURN_WON].Value, out _totalTournWon);
                else { _totalTournWon = 0; anyMissing = true; }

                // If any key was missing, push defaults to server immediately
                if (anyMissing)
                    SaveStatsToServer();

                RefreshAllStatTexts();

                // Step 3
                FetchCurrencies();
            },
            error =>
            {
                Debug.LogError("[PlayFabManager] GetUserData: " + error.GenerateErrorReport());
                // Use defaults, push them, still show UI
                SaveStatsToServer();
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
                setNamePanel.SetActive(false);
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
        var payload = new Dictionary<string, string>
        {
            { KEY_LEVEL,        _playerLevel.ToString()      },
            { KEY_XP,           _playerXP.ToString()         },
            { KEY_WIN,          _playerWin.ToString()         },
            { KEY_WIN_COINS,    _totalWinCoins.ToString()     },
            { KEY_GAME_PTS,     _totalGamePoints.ToString()   },
            { KEY_WIN_GP,       _totalWinGP.ToString()        },
            { KEY_GAMES_PLAYED, _totalGamesPlayed.ToString()  },
            { KEY_GAMES_WON,    _totalGamesWon.ToString()     },
            { KEY_TOURN_PLAYED, _totalTournPlayed.ToString()  },
            { KEY_TOURN_WON,    _totalTournWon.ToString()     },
        };

        PlayFabClientAPI.UpdateUserData(
            new UpdateUserDataRequest { Data = payload },
            result => Debug.Log("[PlayFabManager] Stats saved to server."),
            error => Debug.LogError("[PlayFabManager] SaveStats error: " + error.GenerateErrorReport()));
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
                _playfabFailed = true;
                _playfabError = FriendlyError(error);
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Error Helpers
    // ─────────────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        errorPanel.SetActive(true);
        errorTitle.text = message;
    }

    private void ShowInlineError(string message)
    {
        errorPanel.SetActive(true);
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
        if (errorPanel) errorPanel.SetActive(false);
        if (setNamePanel) setNamePanel.SetActive(false);
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
        errorPanel.SetActive(false);
        ResetPlayFabState();
        StartCoroutine(InitialLoadingFlow());
    }
}