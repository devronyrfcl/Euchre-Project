// AcceptButtonComponent.cs
using UnityEngine;
using UnityEngine.UI;

public class AcceptButtonComponent : MonoBehaviour
{
    private Button button;
    private GameObject lobbyManagerObject;

    void Start()
    {
        button             = GetComponent<Button>();
        lobbyManagerObject = GameObject.Find("LobbyManager");
        button.onClick.AddListener(OnAcceptClicked);
        // REMOVED: SetVisible(false) — LobbyManager controls visibility
    }

    void OnAcceptClicked()
    {
        lobbyManagerObject.SendMessage("Accept",
            SendMessageOptions.DontRequireReceiver);
    }
}