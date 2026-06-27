// Team1ButtonComponent.cs
using UnityEngine;
using UnityEngine.UI;

public class Team1ButtonComponent : MonoBehaviour
{
    private Button button;
    private GameObject lobbyManagerObject;

    void Start()
    {
        button             = GetComponent<Button>();
        lobbyManagerObject = GameObject.Find("LobbyManager");
        button.onClick.AddListener(OnTeam1Clicked);
    }

    void OnTeam1Clicked()
    {
        lobbyManagerObject.SendMessage("JoinTeam0",
            SendMessageOptions.DontRequireReceiver);
    }
}