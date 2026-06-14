using UnityEngine;

namespace EchoSpace.Core
{
    /// <summary>
    /// Editor / Play Mode 測試用玩家控制器（WASD + 滑鼠右鍵視角）。
    /// 非正式 VR locomotion，請勿用於最終 XR 操作。
    /// </summary>
    public class EditorWASDPlayerController : MonoBehaviour
    {
        public float moveSpeed = 3f;
        public float sprintMultiplier = 2.5f;
        public float lookSensitivity = 2f;
        public bool enableVerticalMove = true;
        public bool lockCursorWhileLooking = true;

        private float _pitch;
        private float _yaw;
        private bool _wasLooking;

        private void Start()
        {
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
            if (_pitch > 180f)
            {
                _pitch -= 360f;
            }
        }

        private void OnDisable()
        {
            UnlockCursor();
        }

        private void Update()
        {
            HandleLook();
            HandleMove();
        }

        private void HandleLook()
        {
            bool isLooking = Input.GetMouseButton(1);

            if (isLooking)
            {
                if (lockCursorWhileLooking)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

                _yaw += Input.GetAxis("Mouse X") * lookSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch, -85f, 85f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else if (_wasLooking && lockCursorWhileLooking)
            {
                UnlockCursor();
            }

            _wasLooking = isLooking;
        }

        private void HandleMove()
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                forward.Normalize();
            }

            Vector3 right = transform.right;
            right.y = 0f;
            if (right.sqrMagnitude > 0.0001f)
            {
                right.Normalize();
            }

            Vector3 move = Vector3.zero;

            if (Input.GetKey(KeyCode.W))
            {
                move += forward;
            }

            if (Input.GetKey(KeyCode.S))
            {
                move -= forward;
            }

            if (Input.GetKey(KeyCode.A))
            {
                move -= right;
            }

            if (Input.GetKey(KeyCode.D))
            {
                move += right;
            }

            if (enableVerticalMove)
            {
                if (Input.GetKey(KeyCode.E))
                {
                    move += Vector3.up;
                }

                if (Input.GetKey(KeyCode.Q))
                {
                    move += Vector3.down;
                }
            }

            if (move.sqrMagnitude < 0.0001f)
            {
                return;
            }

            move.Normalize();

            float speed = moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift))
            {
                speed *= sprintMultiplier;
            }

            transform.position += move * speed * Time.deltaTime;
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
