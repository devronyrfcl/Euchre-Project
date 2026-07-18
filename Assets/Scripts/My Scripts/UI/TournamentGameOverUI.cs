using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// "Tournament Game Over Panel" — shown instead of the normal game-over screen when
/// a tournament match ends. Spawns a score breakdown, animates the totals, and shows
/// the player's new tournament rank.
/// Call Populate() right after activating this panel.
/// </summary>
public class TournamentGameOverUI : MonoBehaviour
{
    [Header("=== BREAKDOWN SCROLL ===")]
    [SerializeField] private RectTransform content;
    [SerializeField] private GameObject    breakdownCardPrefab;

    [Header("=== TOTALS ===")]
    [Tooltip("Points earned this match, animates 0 → matchScore, shown as '+230'")]
    [SerializeField] private TMP_Text totalGainText;
    [Tooltip("New cumulative tournament score, animates previousTotal → newTotal")]
    [SerializeField] private TMP_Text totalScoreText;
    [Tooltip("New tournament rank, shown as '#6'")]
    [SerializeField] private TMP_Text newRankText;

    private const float ANIM_DURATION = 1.2f;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    /// <param name="breakdown">Score line items earned this match (title + value).</param>
    /// <param name="previousTotal">Player's tournament score before this match.</param>
    /// <param name="newRank">Player's tournament rank after this match.</param>
    public void Populate(List<ScoreBreakdownEntry> breakdown, int previousTotal, int newRank)
    {
        // Clear previous cards
        foreach (GameObject go in _spawned)
            if (go != null) Destroy(go);
        _spawned.Clear();

        int matchScoreSum = 0;

        if (breakdown != null && content != null && breakdownCardPrefab != null)
        {
            foreach (ScoreBreakdownEntry entry in breakdown)
            {
                matchScoreSum += entry.value;

                GameObject obj = Instantiate(breakdownCardPrefab, content);
                PlayerScoreBreakdownCard card = obj.GetComponent<PlayerScoreBreakdownCard>();
                card?.Setup(entry.title, entry.value);

                _spawned.Add(obj);
            }
        }

        int newTotal = previousTotal + matchScoreSum;

        AnimateInt(totalGainText,  0,             matchScoreSum, "+{0:N0}");
        AnimateInt(totalScoreText, previousTotal,  newTotal,      "{0:N0}");

        if (newRankText != null)
            newRankText.text = $"#{newRank}";
    }

    private void AnimateInt(TMP_Text text, int from, int to, string format)
    {
        if (text == null) return;

        int current = from;
        text.text = string.Format(format, current);

        DOTween.To(() => current, x =>
        {
            current = x;
            text.text = string.Format(format, current);
        }, to, ANIM_DURATION).SetEase(Ease.OutCubic);
    }
}
