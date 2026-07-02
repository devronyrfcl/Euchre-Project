using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Manages 4 turn indicator RectTransforms (one per player panel).
/// Each rotates on Z axis while active.
/// GameplayNetworkManager calls SetActiveTurn(panelIndex) each turn.
/// </summary>
public class TurnIndicatorController : MonoBehaviour
{
    public static TurnIndicatorController Instance { get; private set; }

    [Header("=== INDICATORS ===")]
    [Tooltip("Index 0=me, 1=partner, 2=left, 3=right. Assign the RectTransform of each.")]
    [SerializeField] private RectTransform[] indicators = new RectTransform[4];

    [Header("=== SETTINGS ===")]
    [SerializeField] private float rotationSpeed  = 180f; // degrees per second
    [SerializeField] private float scaleInTime    = 0.2f;
    [SerializeField] private float scaleOutTime   = 0.15f;

    private int         _activePanelIndex = -1;
    private Coroutine   _rotateRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Disable all indicators at start
        foreach (RectTransform rt in indicators)
            if (rt != null) rt.gameObject.SetActive(false);
    }

    // ─── Called by GameplayNetworkManager ────────────────────────

    /// <summary>
    /// panelIndex: 0=me, 1=partner, 2=left, 3=right.
    /// Pass -1 to deactivate all.
    /// </summary>
    public void SetActiveTurn(int panelIndex)
    {
        // Deactivate previous
        if (_activePanelIndex >= 0 && _activePanelIndex < indicators.Length)
            DeactivateIndicator(_activePanelIndex);

        _activePanelIndex = panelIndex;

        if (panelIndex < 0 || panelIndex >= indicators.Length) return;

        if (indicators[panelIndex] != null)
            ActivateIndicator(panelIndex);
    }

    // ─── Internal ────────────────────────────────────────────────

    private void ActivateIndicator(int index)
    {
        RectTransform rt = indicators[index];
        if (rt == null) return;

        rt.gameObject.SetActive(true);
        rt.localScale      = Vector3.zero;
        rt.localEulerAngles = Vector3.zero;

        rt.DOScale(Vector3.one, scaleInTime).SetEase(Ease.OutBack);

        if (_rotateRoutine != null) StopCoroutine(_rotateRoutine);
        _rotateRoutine = StartCoroutine(RotateLoop(rt));
    }

    private void DeactivateIndicator(int index)
    {
        if (_rotateRoutine != null)
        {
            StopCoroutine(_rotateRoutine);
            _rotateRoutine = null;
        }

        RectTransform rt = indicators[index];
        if (rt == null) return;

        rt.DOScale(Vector3.zero, scaleOutTime)
            .SetEase(Ease.InBack)
            .OnComplete(() =>
            {
                if (rt != null)
                    rt.gameObject.SetActive(false);
            });
    }

    private IEnumerator RotateLoop(RectTransform rt)
    {
        while (true)
        {
            rt.Rotate(0f, 0f, -rotationSpeed * Time.deltaTime);
            yield return null;
        }
    }
}