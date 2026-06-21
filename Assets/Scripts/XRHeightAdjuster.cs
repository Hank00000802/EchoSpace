using UnityEngine;

public class XRHeightAdjuster : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Assign the Camera Offset object under XR Origin here.")]
    public Transform cameraOffset;

    [Header("Height Settings")]
    public float speed = 0.25f;
    public float minLocalY = -2.0f;
    public float maxLocalY = 2.0f;

    private Vector3 initialLocalPosition;

    private void Start()
    {
        if (cameraOffset == null)
        {
            Debug.LogWarning("[XRHeightAdjuster] Camera Offset is not assigned.");
            return;
        }

        initialLocalPosition = cameraOffset.localPosition;
    }

    private void Update()
    {
        if (cameraOffset == null) return;

        if (Input.GetKey(KeyCode.Q))
        {
            AdjustHeight(speed * Time.deltaTime);
        }

        if (Input.GetKey(KeyCode.E))
        {
            AdjustHeight(-speed * Time.deltaTime);
        }

        if (Input.GetKeyDown(KeyCode.Home))
        {
            cameraOffset.localPosition = initialLocalPosition;
            Debug.Log($"[XRHeightAdjuster] Reset Camera Offset local Y = {cameraOffset.localPosition.y:F2}");
        }
    }

    private void AdjustHeight(float delta)
    {
        Vector3 pos = cameraOffset.localPosition;
        pos.y = Mathf.Clamp(pos.y + delta, minLocalY, maxLocalY);
        cameraOffset.localPosition = pos;

        Debug.Log($"[XRHeightAdjuster] Camera Offset local Y = {cameraOffset.localPosition.y:F2}");
    }
}