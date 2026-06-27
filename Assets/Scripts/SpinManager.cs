using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SpinManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform wheel;
    [SerializeField] private GameObject spinButton;

    [Header("Spin Settings")]
    [SerializeField] private float minSpinDuration = 4f;
    [SerializeField] private float maxSpinDuration = 6f;

    [SerializeField] private float minExtraRotations = 5f;
    [SerializeField] private float maxExtraRotations = 10f;

    private bool isSpinning;

    public bool IsSpinning => isSpinning;

    public void Spin()
    {
        if (isSpinning)
            return;

        StartCoroutine(SpinRoutine());
    }

    private IEnumerator SpinRoutine()
    {
        isSpinning = true;

        if (spinButton != null)
            spinButton.SetActive(false);

        float duration = Random.Range(minSpinDuration, maxSpinDuration);

        // Random final angle
        float targetAngle = Random.Range(0f, 360f);

        // Extra full rotations for realistic spinning
        float extraRotations = Random.Range(minExtraRotations, maxExtraRotations);

        float startAngle = wheel.eulerAngles.z;
        float endAngle = startAngle + (extraRotations * 360f) + targetAngle;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);

            // Ease Out Cubic (fast start, slow finish)
            float easedT = 1f - Mathf.Pow(1f - t, 3f);

            float currentAngle = Mathf.Lerp(startAngle, endAngle, easedT);

            wheel.rotation = Quaternion.Euler(0f, 0f, currentAngle);

            yield return null;
        }

        wheel.rotation = Quaternion.Euler(0f, 0f, endAngle);

        isSpinning = false;

        if (spinButton != null)
            spinButton.SetActive(true);

        Debug.Log("Spin Finished");
    }
}