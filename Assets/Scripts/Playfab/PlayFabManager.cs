using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PlayFab;
using PlayFab.ClientModels;

public class PlayFabManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject splashAndLoading;
    public GameObject mainSplashPanel;
    public GameObject signUpPanel;
    public GameObject mainMenuPanel;

    [Header("Splash")]
    public Transform splashScreenLogo;
    public RectTransform firstLoadingParent;
    public Image firstLoadingFill;

    [Header("Error")]
    public GameObject errorPanel;
    public TMP_Text errorTitle;

    [Header("Signup")]
    public TMP_InputField signupEmailInput;
    public TMP_InputField signupPassInput;

    bool loginCompleted;
    bool loginSuccess;

    void Start()
    {
        Application.targetFrameRate = 60;

        errorPanel.SetActive(false);
        signUpPanel.SetActive(false);
        mainMenuPanel.SetActive(false);

        StartCoroutine(BootSequence());
    }

    IEnumerator BootSequence()
    {
        splashAndLoading.SetActive(true);

        yield return PlaySplashAnimation();

        string email = PlayerPrefs.GetString("Email", "");
        string password = PlayerPrefs.GetString("Password", "");

        if (string.IsNullOrEmpty(email) ||
            string.IsNullOrEmpty(password))
        {
            splashAndLoading.SetActive(false);
            signUpPanel.SetActive(true);
            yield break;
        }

        yield return LoginRoutine(email, password);

        if (loginSuccess)
        {
            mainSplashPanel.SetActive(false);
            splashAndLoading.SetActive(false);
            mainMenuPanel.SetActive(true);
        }
    }

    IEnumerator PlaySplashAnimation()
    {
        splashScreenLogo.localScale = Vector3.zero;

        yield return splashScreenLogo
            .DOScale(new Vector3(2.4f, 2f, 2f), 0.8f)
            .SetEase(Ease.OutBack)
            .WaitForCompletion();

        firstLoadingParent.anchoredPosition =
            new Vector2(
                firstLoadingParent.anchoredPosition.x,
                126.2f);

        yield return firstLoadingParent
            .DOAnchorPosY(-126.2f, 0.5f)
            .SetEase(Ease.OutQuad)
            .WaitForCompletion();
    }

    IEnumerator LoginRoutine(string email, string password)
    {
        loginCompleted = false;
        loginSuccess = false;

        firstLoadingFill.fillAmount = 0;

        Tween fillTween =
            DOTween.To(
                () => firstLoadingFill.fillAmount,
                x => firstLoadingFill.fillAmount = x,
                0.9f,
                2f);

        PlayFabClientAPI.LoginWithEmailAddress(
            new LoginWithEmailAddressRequest
            {
                Email = email,
                Password = password
            },
            result =>
            {
                loginSuccess = true;
                loginCompleted = true;
            },
            error =>
            {
                ShowError(error.ErrorMessage);

                loginSuccess = false;
                loginCompleted = true;
            });

        yield return new WaitUntil(() => loginCompleted);

        fillTween.Kill();

        if (loginSuccess)
        {
            yield return firstLoadingFill
                .DOFillAmount(1f, 0.4f)
                .WaitForCompletion();
        }
    }

    public void Signup()
    {
        string email = signupEmailInput.text.Trim();
        string password = signupPassInput.text.Trim();

        if (string.IsNullOrEmpty(email))
        {
            ShowError("Email field is empty.");
            return;
        }

        if (password.Length < 6)
        {
            ShowError("Password must be at least 6 characters.");
            return;
        }

        StartCoroutine(SignupRoutine(email, password));
    }

    IEnumerator SignupRoutine(string email, string password)
    {
        signUpPanel.SetActive(false);
        splashAndLoading.SetActive(true);

        yield return PlaySplashAnimation();

        bool completed = false;
        bool success = false;

        firstLoadingFill.fillAmount = 0;

        Tween fillTween =
            DOTween.To(
                () => firstLoadingFill.fillAmount,
                x => firstLoadingFill.fillAmount = x,
                0.9f,
                2f);

        PlayFabClientAPI.RegisterPlayFabUser(
            new RegisterPlayFabUserRequest
            {
                Email = email,
                Password = password,
                Username = email.Split('@')[0],
                RequireBothUsernameAndEmail = false
            },
            result =>
            {
                success = true;
                completed = true;
            },
            error =>
            {
                ShowError(error.ErrorMessage);
                success = false;
                completed = true;
            });

        yield return new WaitUntil(() => completed);

        fillTween.Kill();

        if (!success)
        {
            splashAndLoading.SetActive(false);
            signUpPanel.SetActive(true);
            yield break;
        }

        PlayerPrefs.SetString("Email", email);
        PlayerPrefs.SetString("Password", password);
        PlayerPrefs.Save();

        yield return firstLoadingFill
            .DOFillAmount(1f, 0.4f)
            .WaitForCompletion();

        yield return LoginRoutine(email, password);

        if (loginSuccess)
        {
            splashAndLoading.SetActive(false);
            mainSplashPanel.SetActive(false);
            mainMenuPanel.SetActive(true);
        }
    }

    void ShowError(string message)
    {
        errorPanel.SetActive(true);
        errorTitle.text = message;
    }
}