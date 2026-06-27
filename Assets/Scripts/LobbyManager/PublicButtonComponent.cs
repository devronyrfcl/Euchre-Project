// PublicButtonComponent.cs
using UnityEngine;
using UnityEngine.UI;

public class PublicButtonComponent : MonoBehaviour
{
    private Button button;
    private GameObject lobbyManagerObject;

    void Start()
    {
        button             = GetComponent<Button>();
        lobbyManagerObject = GameObject.Find("LobbyManager");
        button.onClick.AddListener(OnPublicClicked);
    }

    void OnPublicClicked()
    {
        lobbyManagerObject.SendMessage("Public",
            SendMessageOptions.DontRequireReceiver);
    }
}