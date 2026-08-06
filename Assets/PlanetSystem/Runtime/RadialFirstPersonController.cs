using UnityEngine;
using UnityEngine.InputSystem;

namespace DoctorWho.Planets
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class RadialFirstPersonController : MonoBehaviour
    {
        [SerializeField] private Transform planetCenter;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private PlanetGenerationSettings settings;
        [SerializeField] private float probeRadius = 0.36f;
        [SerializeField] private float probeDistance = 1.15f;

        private Rigidbody body;
        private CapsuleCollider capsule;
        private float pitch;
        private bool grounded;
        private Vector3 groundNormal;

        public void Configure(Transform center, Transform pivot, PlanetGenerationSettings generationSettings)
        {
            planetCenter = center;
            cameraPivot = pivot;
            settings = generationSettings;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            if (cameraPivot == null || settings == null) return;
            Vector2 look = ReadLook();
            transform.Rotate(0f, look.x * settings.mouseSensitivity, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - look.y * settings.mouseSensitivity, -85f, 85f);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void FixedUpdate()
        {
            if (planetCenter == null || settings == null) return;

            Vector3 up = (body.position - planetCenter.position).normalized;
            ProbeGround(up);

            Quaternion aligned = Quaternion.FromToRotation(transform.up, up) * body.rotation;
            body.MoveRotation(Quaternion.Slerp(body.rotation, aligned, 18f * Time.fixedDeltaTime));

            Vector2 input = ReadMove();
            float maxSpeed = IsSprinting() ? settings.sprintSpeed : settings.walkSpeed;
            Vector3 desired = Vector3.ProjectOnPlane(transform.right * input.x + transform.forward * input.y, up).normalized * (input.magnitude * maxSpeed);
            Vector3 currentTangent = Vector3.ProjectOnPlane(body.velocity, up);
            float acceleration = grounded ? settings.groundAcceleration : settings.airAcceleration;
            Vector3 tangentChange = Vector3.ClampMagnitude(desired - currentTangent, acceleration * Time.fixedDeltaTime);
            body.AddForce(tangentChange, ForceMode.VelocityChange);

            float radialSpeed = Vector3.Dot(body.velocity, up);
            if (grounded && radialSpeed < 0f)
            {
                body.AddForce(up * -radialSpeed, ForceMode.VelocityChange);
                body.AddForce(up * -1.5f, ForceMode.Acceleration);
            }
            else
            {
                body.AddForce(-up * settings.gravity, ForceMode.Acceleration);
            }

            if (grounded && JumpPressed())
            {
                body.AddForce(up * settings.jumpSpeed, ForceMode.VelocityChange);
                grounded = false;
            }
        }

        private void ProbeGround(Vector3 up)
        {
            float halfHeight = Mathf.Max(capsule.height * .5f - capsule.radius, 0f);
            Vector3 origin = body.position + up * (capsule.center.y - halfHeight + .12f);
            grounded = Physics.SphereCast(origin, probeRadius, -up, out RaycastHit hit, probeDistance, ~0, QueryTriggerInteraction.Ignore);
            groundNormal = grounded ? hit.normal : up;
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
