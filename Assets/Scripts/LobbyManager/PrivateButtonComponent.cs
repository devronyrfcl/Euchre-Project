// PrivateButtonComponent.cs
using UnityEngine;
using UnityEngine.UI;

public class PrivateButtonComponent : MonoBehaviour
{
    private Button button;
    private GameObject lobbyManagerObject;

    void Start()
    {
        button             = GetComponent<Button>();
        lobbyManagerObject = GameObject.Find("LobbyManager");
        button.onClick.AddListener(OnPrivateClicked);
    }

    void OnPrivateClicked()
    {
        lobbyManagerObject.SendMessage("Private",
            SendMessageOptions.DontRequireReceiver);
    }
}