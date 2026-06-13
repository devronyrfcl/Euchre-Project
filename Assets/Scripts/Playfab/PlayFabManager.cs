using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using PlayFab;
using PlayFab.ClientModels;

/// <summary>
/// PlayFabManager — Eurchre Project
/// Handles: Splash/Loading, Signup, Login (cached & new), Error states.
/// Attach this to a persistent GameObject in your scene (e.g. "GameManager").
/// </summary>
public class PlayFabManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("=== ROOT PANELS ===")]
    [SerializeField] private GameObject splashAndLoading;       // "Splash And Loading"
    [SerializeField] private GameObject mainSplashPanel;        // "Main Splash Panel"
    [SerializeField] private GameObject signupPanel;            // "Signup Panel"
    [SerializeField] private GameObject mainMenuPanel;          // "Main Menu Panel"
    [SerializeField] private GameObject errorPanel;             // "Error Panel"

    [Header("=== SPLASH / LOADING ===")]
    [SerializeField] private Transform splashScreenLogo;        // Splash Screen Logo (Transform)
    [SerializeField] private RectTransform firstLoadingParent;  // "First Loading Parent"
    [SerializeField] private Image firstLoadingFill;            // "First_Loading_Fill" — Filled Image

    [Header("=== SIGNUP ===")]
    [SerializeField] private TMP_InputField signupEmailInput;   // "Signup Email Input"
    [SerializeField] private TMP_InputField signupPassField;    // "Signup Pass Field"

    [Header("=== ERROR ===")]
    [SerializeField] private TMP_Text errorTitle;               // "Error Title"

    // ─────────────────────────────────────────────────────────────
    //  Constants & Private State
    // ─────────────────────────────────────────────────────────────

    private const string CACHED_EMAIL_KEY = "PlayFab_CachedEmail";
    private const string CACHED_PASS_KEY = "PlayFab_CachedPass";
    private const float MIN_LOAD_SECONDS = 3f;

    // These are set by PlayFab callbacks and read by the loading coroutine.
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
    //  LOADING COROUTINE A — Timer Only (initial splash, no backend)
    //  Fill goes 0 → 1 smoothly over MIN_LOAD_SECONDS then fires onComplete.
    // ─────────────────────────────────────────────────────────────

    private IEnumerator InitialLoadingFlow()
    {
        yield return StartCoroutine(PlaySplashAnimation());

        firstLoadingFill.fillAmount = 0f;
        float elapsed = 0f;
        bool errorShown = false;

        // Check internet immediately
        ResetPlayFabState();
        CheckInternetConnection();

        while (true)
        {
            elapsed += Time.deltaTime;

            // ── NO INTERNET → stop at 30% ─────────────────────────
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

            // ── CONNECTED + minimum time passed → proceed ─────────
            if (_playfabReady && elapsed >= MIN_LOAD_SECONDS)
            {
                yield return firstLoadingFill
                    .DOFillAmount(1f, 0.35f)
                    .SetEase(Ease.OutQuad)
                    .WaitForCompletion();

                yield return new WaitForSeconds(0.2f);
                DecideLoginPath();
                yield break;
            }

            // ── CRAWL toward 90% while waiting ────────────────────
            float naturalTarget = Mathf.Clamp01(elapsed / MIN_LOAD_SECONDS) * 0.9f;
            if (firstLoadingFill.fillAmount < naturalTarget)
            {
                firstLoadingFill.fillAmount = Mathf.Lerp(
                    firstLoadingFill.fillAmount, naturalTarget, Time.deltaTime * 3f);
            }

            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  LOADING COROUTINE B — PlayFab-Aware (signup & cached login)
    //
    //  Behaviour:
    //   • Fill smoothly crawls from 0 → 0.9 over MIN_LOAD_SECONDS.
    //   • If PlayFab succeeds  → snaps fill to 1 → fires onComplete.
    //   • If PlayFab fails     → fill STOPS at 0.3 immediately → shows error panel.
    //  Start the PlayFab call BEFORE calling this coroutine.
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

            // ── ERROR: stop at 30 % and bail ──────────────────
            if (_playfabFailed && !errorShown)
            {
                errorShown = true;

                // Tween to exactly 0.3, then show the error panel
                yield return firstLoadingFill
                    .DOFillAmount(0.3f, 0.4f)
                    .SetEase(Ease.OutQuad)
                    .WaitForCompletion();

                yield return new WaitForSeconds(0.5f);
                ShowError(_playfabError);
                yield break;
            }

            // ── SUCCESS: snap to 1 and finish ─────────────────
            if (_playfabReady && elapsed >= MIN_LOAD_SECONDS)
            {
                yield return firstLoadingFill
                    .DOFillAmount(1f, 0.35f)
                    .SetEase(Ease.OutQuad)
                    .WaitForCompletion();

                yield return new WaitForSeconds(0.2f);
                onComplete?.Invoke();
                yield break;
            }

            // ── CRAWL: natural progress toward 0.9 ────────────
            // Never goes backward. Stays below 0.9 until PlayFab confirms.
            float naturalTarget = Mathf.Clamp01(elapsed / MIN_LOAD_SECONDS) * 0.9f;
            if (firstLoadingFill.fillAmount < naturalTarget)
            {
                firstLoadingFill.fillAmount = Mathf.Lerp(
                    firstLoadingFill.fillAmount, naturalTarget, Time.deltaTime * 3f);
            }

            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Shared splash animation (logo scale + bar slide-in)
    // ─────────────────────────────────────────────────────────────

    private IEnumerator PlaySplashAnimation()
    {
        firstLoadingFill.fillAmount = 0f;

        // Logo: 0 → (2.4, 2, 2)
        splashScreenLogo.localScale = Vector3.zero;
        splashScreenLogo
            .DOScale(new Vector3(2.4f, 2f, 2f), 0.8f)
            .SetEase(Ease.OutBack);

        // Loading bar slide up
        firstLoadingParent.anchoredPosition = new Vector2(
            firstLoadingParent.anchoredPosition.x, -250f);
        firstLoadingParent
            .DOAnchorPosY(-126.2f, 0.6f)
            .SetEase(Ease.OutCubic);

        yield return new WaitForSeconds(0.65f); // let animation begin before fill starts
    }

    // ─────────────────────────────────────────────────────────────
    //  Login Path Decision  (fires after initial timer splash)
    // ─────────────────────────────────────────────────────────────

    private void DecideLoginPath()
    {
        if (HasCachedCredentials())
        {
            // Returning user: start PlayFab login immediately, then run the
            // PlayFab-aware loading flow which waits for the response.
            string email = PlayerPrefs.GetString(CACHED_EMAIL_KEY);
            string pass = PlayerPrefs.GetString(CACHED_PASS_KEY);

            ResetPlayFabState();
            LoginWithPlayFab(email, pass, onSuccess: null); // sets _playfabReady internally
            StartCoroutine(PlayFabAwareLoadingFlow(onComplete: GoToMainMenu));
        }
        else
        {
            // New user: splash done → show signup, no more loading needed.
            splashAndLoading.SetActive(false);
            signupPanel.SetActive(true);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  FLOW 2 — Signup  (wire to button)
    // ─────────────────────────────────────────────────────────────

    public void OnSignupButtonPressed()
    {
        string email = signupEmailInput.text.Trim();
        string pass = signupPassField.text;

        // Validate
        if (!IsValidEmail(email))
        {
            ShowInlineError("Please enter a valid email address.");
            return;
        }
        if (pass.Length < 6)
        {
            ShowInlineError("Password must be at least 6 characters.");
            return;
        }

        // Swap panels
        signupPanel.SetActive(false);
        errorPanel.SetActive(false);
        splashAndLoading.SetActive(true);
        mainSplashPanel.SetActive(true);

        ResetPlayFabState();

        // Fire register → login chain; callbacks will set _playfabReady or _playfabFailed.
        RegisterWithPlayFab(email, pass, onSuccess: () =>
        {
            CacheCredentials(email, pass);
            LoginWithPlayFab(email, pass, onSuccess: null);
        });

        // Loading coroutine runs concurrently and reacts to _playfabReady / _playfabFailed.
        StartCoroutine(PlayFabAwareLoadingFlow(onComplete: GoToMainMenu));
    }

    // ─────────────────────────────────────────────────────────────
    //  FLOW 3 — Main Menu
    // ─────────────────────────────────────────────────────────────

    private void GoToMainMenu()
    {
        splashAndLoading.SetActive(false);
        mainSplashPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    // ─────────────────────────────────────────────────────────────
    //  PlayFab — Register
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

                // Account already exists → skip to login
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

    // ─────────────────────────────────────────────────────────────
    //  PlayFab — Login
    // ─────────────────────────────────────────────────────────────

    private void LoginWithPlayFab(string email, string password, Action onSuccess)
    {
        PlayFabClientAPI.LoginWithEmailAddress(
            new LoginWithEmailAddressRequest { Email = email, Password = password },
            result =>
            {
                Debug.Log("[PlayFabManager] Logged in: " + result.PlayFabId);
                _playfabReady = true;   // coroutine detects this and completes
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

    /// <summary>Full-screen error — hides everything else.</summary>
    private void ShowError(string message)
    {
        errorPanel.SetActive(true);
        errorTitle.text = message;
    }

    /// <summary>Inline error — keeps signup panel visible.</summary>
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

    /// <summary>Wire to a Logout button to force signup on next launch.</summary>
    public void ClearCachedCredentials()
    {
        PlayerPrefs.DeleteKey(CACHED_EMAIL_KEY);
        PlayerPrefs.DeleteKey(CACHED_PASS_KEY);
        PlayerPrefs.Save();
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
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
            // NetworkReachability.ReachableViaCarrierDataNetwork  → mobile data
            // NetworkReachability.ReachableViaLocalAreaNetwork    → WiFi / Editor
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