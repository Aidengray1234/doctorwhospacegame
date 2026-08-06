using UnityEngine;
using UnityEngine.InputSystem;

namespace DoctorWho.Planets
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class RadialFirstPersonController : MonoBehaviour
    {
        [SerializeField] private Transform planetCenter;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private PlanetGenerationSettings settings;
        [SerializeField] private float groundProbeDistance = 1.35f;

        private CharacterController controller;
        private float verticalSpeed;
        private float pitch;

        public void Configure(Transform center, Transform pivot, PlanetGenerationSettings generationSettings)
        {
            planetCenter = center;
            cameraPivot = pivot;
            settings = generationSettings;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            if (planetCenter == null || cameraPivot == null || settings == null) return;

            Vector3 up = (transform.position - planetCenter.position).normalized;
            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 12f * Time.deltaTime);

            Vector2 move = ReadMove();
            Vector2 look = ReadLook();
            float speed = IsSprinting() ? settings.sprintSpeed : settings.walkSpeed;

            transform.Rotate(0f, look.x * settings.mouseSensitivity, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - look.y * settings.mouseSensitivity, -85f, 85f);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            bool grounded = Physics.Raycast(transform.position, -up, out _, groundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
            if (grounded && verticalSpeed < 0f) verticalSpeed = -2f;
            if (grounded && JumpPressed()) verticalSpeed = settings.jumpSpeed;
            verticalSpeed -= settings.gravity * Time.deltaTime;

            Vector3 tangentMove = (transform.right * move.x + transform.forward * move.y);
            if (tangentMove.sqrMagnitude > 1f) tangentMove.Normalize();
            Vector3 velocity = tangentMove * speed + up * verticalSpeed;
            controller.Move(velocity * Time.deltaTime);
        }

        private static Vector2 ReadMove()
        {
            Vector2 value = Vector2.zero;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed) value.y += 1f;
                if (Keyboard.current.sKey.isPressed) value.y -= 1f;
                if (Keyboard.current.dKey.isPressed) value.x += 1f;
                if (Keyboard.current.aKey.isPressed) value.x -= 1f;
            }
            if (Gamepad.current != null) value += Gamepad.current.leftStick.ReadValue();
            return Vector2.ClampMagnitude(value, 1f);
        }

        private static Vector2 ReadLook()
        {
            Vector2 value = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            if (Gamepad.current != null) value += Gamepad.current.rightStick.ReadValue() * 14f;
            return value;
        }

        private static bool JumpPressed() =>
            (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        private static bool IsSprinting() =>
            (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);
    }
}
