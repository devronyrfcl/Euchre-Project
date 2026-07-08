using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class ShopManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject gameCardPanel;
    [SerializeField] private GameObject comboPanel;
    [SerializeField] private GameObject goldPlatePanel;
    [SerializeField] private GameObject coinPanel;

    [Header("Buttons")]
    [SerializeField] private Button gameCardButton;
    [SerializeField] private Button comboButton;
    [SerializeField] private Button goldPlateButton;
    [SerializeField] private Button coinButton;

    [Header("Button Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(1f, 1f, 1f, 0.7f); // Alpha 0.7 for selected state

    private Dictionary<GameObject, Button> panelButtonMap;
    private Button currentActiveButton;
    private GameObject currentActivePanel;

    private void Start()
    {
        InitializePanelButtonMap();
        SetupButtonListeners();
        // Activate first panel by default (Game Card Panel)
        ActivatePanel(gameCardPanel, gameCardButton);
    }

    private void InitializePanelButtonMap()
    {
        panelButtonMap = new Dictionary<GameObject, Button>
        {
            { gameCardPanel, gameCardButton },
            { comboPanel, comboButton },
            { goldPlatePanel, goldPlateButton },
            { coinPanel, coinButton }
        };
    }

    private void SetupButtonListeners()
    {
        gameCardButton.onClick.AddListener(() => ActivatePanel(gameCardPanel, gameCardButton));
        comboButton.onClick.AddListener(() => ActivatePanel(comboPanel, comboButton));
        goldPlateButton.onClick.AddListener(() => ActivatePanel(goldPlatePanel, goldPlateButton));
        coinButton.onClick.AddListener(() => ActivatePanel(coinPanel, coinButton));
    }

    private void ActivatePanel(GameObject panelToActivate, Button buttonToHighlight)
    {
        // Deactivate all panels
        foreach (var panel in panelButtonMap.Keys)
        {
            if (panel != null)
                panel.SetActive(false);
        }

        // Activate the selected panel
        if (panelToActivate != null)
        {
            panelToActivate.SetActive(true);
            currentActivePanel = panelToActivate;
        }

        // Reset all buttons to normal color
        foreach (var button in panelButtonMap.Values)
        {
            if (button != null)
                SetButtonColor(button, normalColor);
        }

        // Highlight the selected button
        if (buttonToHighlight != null)
        {
            SetButtonColor(buttonToHighlight, selectedColor);
            currentActiveButton = buttonToHighlight;
        }
    }

    private void SetButtonColor(Button button, Color color)
    {
        ColorBlock colorBlock = button.colors;
        colorBlock.normalColor = color;
        colorBlock.highlightedColor = color;
        colorBlock.pressedColor = color;
        colorBlock.selectedColor = color;
        button.colors = colorBlock;
    }

    // Public method to activate panel from other scripts
    public void ActivatePanelByName(string panelName)
    {
        switch (panelName)
        {
            case "GameCard":
                ActivatePanel(gameCardPanel, gameCardButton);
                break;
            case "Combo":
                ActivatePanel(comboPanel, comboButton);
                break;
            case "GoldPlate":
                ActivatePanel(goldPlatePanel, goldPlateButton);
                break;
            case "Coin":
                ActivatePanel(coinPanel, coinButton);
                break;
            default:
                Debug.LogWarning($"Panel '{panelName}' not found!");
                break;
        }
    }

    // Public method to get currently active panel
    public GameObject GetCurrentActivePanel()
    {
        return currentActivePanel;
    }

    // Public method to get currently active button
    public Button GetCurrentActiveButton()
    {
        return currentActiveButton;
    }
}