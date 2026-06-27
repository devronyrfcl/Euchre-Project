using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class LobbyManagerComponent : MonoBehaviour
{
    // ─── Inspector References ─────────────────────────────────────
    // Assign all of these in the Inspector after setting up the scene

    [Header("Input")]
    public TMP_InputField inputField;

    [Header("Buttons")]
    public GameObject acceptButton;
    public GameObject publicButton;
    public GameObject privateButton;
    public GameObject team1Button;
    public GameObject team2Button;

    [Header("Text")]
    public TextMeshProUGUI mainText;

    public static LobbyManagerComponent Instance { get; private set; }

    // ─── Private State ────────────────────────────────────────────
    private string playerName = "";
    private string roomID     = "";
    private int    teamAttempt = 0;

    // ─── Lobby Flow State ─────────────────────────────────────────
    // Step 1: Player enters name → presses Accept
    // Step 2: Player chooses Public or Private
    // Step 3 (Private only): Player enters room code → presses Accept
    // Step 4 (Private only): Player chooses Team 1 or Team 2
    private enum LobbyStep
    {
        EnterName,
        ChooseGameType,
        EnterRoomCode,
        ChooseTeam
    }


        void Awake()
    {
        // Singleton setup — only one LobbyManager should ever exist
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    private LobbyStep currentStep = LobbyStep.EnterName;

    // ─────────────────────────────────────────────────────────────
    void Start()
    {
        // Initial state — only name input and accept button visible
        ShowOnly(acceptButton);
        inputField.gameObject.SetActive(true);
        mainText.text = "Enter your name";
        // Remove DontDestroyOnLoad from here — it's now in Awake()
        acceptButton .SetActive(true);
        publicButton .SetActive(false);
        privateButton.SetActive(false);
        team1Button  .SetActive(false);
        team2Button  .SetActive(false);
        inputField.gameObject.SetActive(true);
        mainText.text = "Enter your name";

    DontDestroyOnLoad(gameObject);

        // Make sure this object survives scene load
        // so GameManagerComponent can read name/roomID/teamAttempt
        DontDestroyOnLoad(gameObject);
    }

    // ─── Step 1: Accept button pressed ───────────────────────────
    // Called by AcceptButtonComponent via SendMessage
    public void Accept()
    {
        if (currentStep == LobbyStep.EnterName)
        {
            // Grab name from input field
            string input = inputField.text.Trim();
            playerName = input.Length > 0 ? input : "Anonymous";

            inputField.gameObject.SetActive(false);
            mainText.text = "Join a public game or create a private room?";

            currentStep = LobbyStep.ChooseGameType;
            ShowOnly(publicButton, privateButton);
        }
        else if (currentStep == LobbyStep.EnterRoomCode)
        {
            string input = inputField.text.Trim();

            if (input.Length == 0)
            {
                mainText.text = "Please enter a room code.";
                return;
            }

            roomID = input;
            inputField.gameObject.SetActive(false);
            mainText.text = "Select your preferred team\n(if full you'll be placed in the other)";

            currentStep = LobbyStep.ChooseTeam;
            ShowOnly(team1Button, team2Button);
        }
    }

    // ─── Step 2a: Public game ─────────────────────────────────────
    // Called by PublicButtonComponent via SendMessage
    public void Public()
    {
        mainText.text = "Finding a public game...";
        ShowOnly(); // hide all buttons
        SceneManager.LoadScene("EuchreGame");
    }

    // ─── Step 2b: Private game ────────────────────────────────────
    // Called by PrivateButtonComponent via SendMessage
    public void Private()
    {
        mainText.text = "Enter a room code to join or create a private game";

        inputField.text = "";
        inputField.gameObject.SetActive(true);
        inputField.placeholder.GetComponent<TextMeshProUGUI>().text = "Room code...";

        currentStep = LobbyStep.EnterRoomCode;
        ShowOnly(acceptButton);
    }

    // ─── Step 4: Team selection ───────────────────────────────────
    // Split into two methods because SendMessage only passes one arg
    // Called by Team1ButtonComponent via SendMessage
    public void JoinTeam0()
    {
        JoinTeam(0);
    }

    // Called by Team2ButtonComponent via SendMessage
    public void JoinTeam1()
    {
        JoinTeam(1);
    }

    private void JoinTeam(int team)
    {
        teamAttempt = team;
        mainText.text = "Connecting to room: " + roomID + "...";
        ShowOnly(); // hide all buttons
        SceneManager.LoadScene("EuchreGame");
    }

    // ─── Public Getters (read by GameManagerComponent) ────────────
    public string GetName()        => playerName;
    public string GetRoomID()      => roomID;
    public int    GetTeamAttempt() => teamAttempt;

    // ─── Helper: show only specific buttons, hide the rest ────────
    void ShowOnly(params GameObject[] toShow)
    {
        acceptButton  .SetActive(false);
        publicButton  .SetActive(false);
        privateButton .SetActive(false);
        team1Button   .SetActive(false);
        team2Button   .SetActive(false);

        foreach (var obj in toShow)
            if (obj != null)
                obj.SetActive(true);
    }
}