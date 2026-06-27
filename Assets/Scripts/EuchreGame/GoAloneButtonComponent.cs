using UnityEngine;
using UnityEngine.UI;

public class GoAloneButtonComponent : MonoBehaviour
{
    private Button button;

    void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnClicked);
    }

    void Start()
    {
        gameObject.SetActive(false);
    }

    void OnClicked()
    {
        GameObject gm = GameObject.Find("GameManager");
        if (gm != null)
            gm.SendMessage("GoAloneButtonClicked",
                SendMessageOptions.DontRequireReceiver);
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}