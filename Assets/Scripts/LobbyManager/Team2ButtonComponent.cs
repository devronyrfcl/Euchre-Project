// Team2ButtonComponent.cs
using UnityEngine;
using UnityEngine.UI;

public class Team2ButtonComponent : MonoBehaviour
{
    private Button button;
    private GameObject lobbyManagerObject;

    void Start()
    {
        button             = GetComponent<Button>();
        lobbyManagerObject = GameObject.Find("LobbyManager");
        button.onClick.AddListener(OnTeam2Clicked);
    }

    void OnTeam2Clicked()
    {
        lobbyManagerObject.SendMessage("JoinTeam1",
            SendMessageOptions.DontRequireReceiver);
    }
}