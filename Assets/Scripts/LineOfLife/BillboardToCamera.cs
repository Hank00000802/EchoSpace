using UnityEngine;

namespace EchoSpace.LineOfLife
{
    /// <summary>
    /// Keeps the object facing the player camera (assigned XR camera or Main Camera).
    /// Attach only to labels or UI that should billboard; do not put on parent nodes or anchors.
    /// </summary>
    public class BillboardToCamera : MonoBehaviour
    {
        public Transform targetCamera;
        public bool lockYOnly = false;
        public bool flip180 = true;

        private void LateUpdate()
        {
            Transform cam = targetCamera;
            if (cam == null && Camera.main != null)
            {
                cam = Camera.main.transform;
            }

            if (cam == null)
            {
                return;
            }

            Vector3 toCamera = cam.position - transform.position;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (lockYOnly)
            {
                toCamera.y = 0f;
                if (toCamera.sqrMagnitude < 0.0001f)
                {
                    return;
                }

                transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            }
            else
            {
                transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            }

            if (flip180)
            {
                transform.Rotate(0f, 180f, 0f, Space.Self);
            }
        }
    }
}
