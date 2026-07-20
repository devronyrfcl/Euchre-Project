using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays out "deal face-up until the Jack of Clubs appears" once at the start of
/// each match. GameplayNetworkManager computes the reveal sequence (fair — same
/// as picking a uniformly random seat) and calls PlayReveal() with it on every client.
/// </summary>
public class DealerSelectionUI : MonoBehaviour
{
    [Header("=== PANEL ===")]
    [SerializeField] private GameObject rootPanel;
    [SerializeField] private TMP_Text   statusText;

    [Header("=== SLOTS (index = panel: 0=me,1=partner,2=left,3=right) ===")]
    [SerializeField] private Image[]      slotImages    = new Image[4];
    [SerializeField] private GameObject[] winnerHighlights = new GameObject[4];

    [Header("=== TIMING ===")]
    [SerializeField] private float flipHalfDuration = 0.15f;
    [SerializeField] private float delayBetweenCards = 0.35f;
    [SerializeField] private float holdAfterWinner   = 1.2f;

    private void Awake()
    {
        rootPanel?.SetActive(false);
    }

    public void PlayReveal(List<string> sequence, int firstDealerSeatAbsolute)
    {
        StartCoroutine(RevealCoroutine(sequence, firstDealerSeatAbsolute));
    }

    private IEnumerator RevealCoroutine(List<string> sequence, int firstDealerSeatAbsolute)
    {
        rootPanel?.SetActive(true);
        ResetSlots();

        if (statusText != null) statusText.text = "Determining first dealer...";

        int seat = 0;
        for (int i = 0; i < sequence.Count; i++)
        {
            int panelIdx = GameplayNetworkManager.Instance != null
                ? GameplayNetworkManager.Instance.RemapSeatToPanel(seat)
                : seat;

            yield return StartCoroutine(FlipSlot(panelIdx, sequence[i]));
            seat = (seat + 1) % 4;

            yield return new WaitForSeconds(delayBetweenCards);
        }

        int winnerPanel = GameplayNetworkManager.Instance != null
            ? GameplayNetworkManager.Instance.RemapSeatToPanel(firstDealerSeatAbsolute)
            : firstDealerSeatAbsolute;

        if (statusText != null)
            statusText.text = winnerPanel == 0 ? "You deal first!" : "First dealer found!";

        if (winnerPanel >= 0 && winnerPanel < winnerHighlights.Length && winnerHighlights[winnerPanel] != null)
        {
            winnerHighlights[winnerPanel].SetActive(true);
            winnerHighlights[winnerPanel].transform.localScale = Vector3.one;
            winnerHighlights[winnerPanel].transform.DOScale(1.15f, 0.35f)
                .SetEase(Ease.InOutSine)
                .SetLoops(4, LoopType.Yoyo);
        }

        yield return new WaitForSeconds(holdAfterWinner);

        rootPanel?.SetActive(false);
    }

    private IEnumerator FlipSlot(int panelIdx, string cardType)
    {
        if (panelIdx < 0 || panelIdx >= slotImages.Length) yield break;
        Image img = slotImages[panelIdx];
        if (img == null) yield break;

        img.rectTransform.DOScaleX(0f, flipHalfDuration).SetEase(Ease.InQuad);
        yield return new WaitForSeconds(flipHalfDuration);

        if (EuchreCardManager.Instance != null)
            img.sprite = EuchreCardManager.Instance.GetCardSprite(cardType);

        img.rectTransform.DOScaleX(1f, flipHalfDuration).SetEase(Ease.OutQuad);
        yield return new WaitForSeconds(flipHalfDuration);
    }

    private void ResetSlots()
    {
        for (int i = 0; i < slotImages.Length; i++)
        {
            if (slotImages[i] != null)
            {
                slotImages[i].rectTransform.localScale = Vector3.one;
                if (EuchreCardManager.Instance != null)
                    slotImages[i].sprite = EuchreCardManager.Instance.CardBackSprite;
            }

            if (i < winnerHighlights.Length && winnerHighlights[i] != null)
                winnerHighlights[i].SetActive(false);
        }
    }
}
