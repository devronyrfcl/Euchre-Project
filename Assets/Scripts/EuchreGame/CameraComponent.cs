using UnityEngine;

public class CameraComponent : MonoBehaviour
{
    // Kept minimal — no drag tracking needed since cards use tap not drag
    // Extend this later if you want pinch-to-zoom or camera pan

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
    }

    // Call this from GameManager if you ever need to convert
    // a screen point to world point (convenience wrapper)
    public Vector3 ScreenToWorld(Vector2 screenPos)
    {
        Vector3 pos = cam.ScreenToWorldPoint(
            new Vector3(screenPos.x, screenPos.y,
                        Mathf.Abs(cam.transform.position.z)));
        pos.z = 0;
        return pos;
    }
}