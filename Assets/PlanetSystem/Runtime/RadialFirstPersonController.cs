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
        [SerializeField] private PlanetPrototypeGenerator planet;
        [SerializeField] private LayerMask terrainMask = ~0;

        private Rigidbody body;
        private CapsuleCollider capsule;
        private Camera playerCamera;
        private float pitch;
        private bool grounded;
        private Vector3 groundNormal;
        private bool initialized;
        private float nextGroundProbe;

        public bool IsInitialized => initialized;
        public bool IsGrounded => grounded;

        public void Configure(Transform center, Transform pivot, PlanetGenerationSettings generationSettings)
        {
            planetCenter = center;
            cameraPivot = pivot;
            settings = generationSettings;
            planet = center != null ? center.GetComponent<PlanetPrototypeGenerator>() : null;
            SetupCamera();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.isKinematic = true;
            SetupCamera();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Start()
        {
            if (planet == null && planetCenter != null) planet = planetCenter.GetComponent<PlanetPrototypeGenerator>();
            Invoke(nameof(RespawnToSafeSurface), .15f);
        }

        private void SetupCamera()
        {
            if (cameraPivot == null) return;
            playerCamera = cameraPivot.GetComponentInChildren<Camera>();
            if (playerCamera == null) return;
            if (settings != null)
            {
                playerCamera.nearClipPlane = settings.cameraNearClip;
                playerCamera.fieldOfView = settings.cameraFov;
                playerCamera.farClipPlane = Mathf.Max(10000f, settings.radius * 10f);
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) RespawnToSafeSurface();
            if (!initialized || cameraPivot == null || settings == null) return;
            Vector2 look = ReadLook();
            float sensitivity = Mouse.current != null ? .065f : 1.8f;
            transform.Rotate(0f, look.x * sensitivity, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - look.y * sensitivity, -88f, 88f);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void FixedUpdate()
        {
            if (!initialized || planetCenter == null || settings == null) return;
            Vector3 up = (body.position - planetCenter.position).normalized;
            GroundProbe(up);

            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, up) * body.rotation;
            body.MoveRotation(Quaternion.Slerp(body.rotation, targetRotation, 20f * Time.fixedDeltaTime));

            Vector2 input = ReadMove();
            Vector3 wish = Vector3.ProjectOnPlane(transform.right * input.x + transform.forward * input.y, groundNormal).normalized;
            float speed = IsSprinting() ? settings.sprintSpeed : settings.walkSpeed;
            Vector3 tangentVelocity = Vector3.ProjectOnPlane(body.velocity, up);
            Vector3 desired = wish * speed * input.magnitude;
            float accel = grounded ? settings.groundAcceleration : settings.airAcceleration;
            Vector3 delta = Vector3.ClampMagnitude(desired - tangentVelocity, accel * Time.fixedDeltaTime);
            body.AddForce(delta, ForceMode.VelocityChange);

            float radial = Vector3.Dot(body.velocity, up);
            if (grounded)
            {
                if (radial < 0f) body.AddForce(-up * radial, ForceMode.VelocityChange);
                body.AddForce(-up * 2.5f, ForceMode.Acceleration);
                if (JumpPressed())
                {
                    body.AddForce(up * settings.jumpSpeed, ForceMode.VelocityChange);
                    grounded = false;
                }
            }
            else body.AddForce(-up * settings.gravity, ForceMode.Acceleration);

            if (body.position.sqrMagnitude > settings.floatingOriginThreshold * settings.floatingOriginThreshold * 9f)
                Debug.LogWarning("[Planet V2] Player exceeded expected floating-origin range.");
        }

        private void GroundProbe(Vector3 up)
        {
            grounded = false;
            groundNormal = up;
            float castRadius = Mathf.Max(.1f, capsule.radius * .88f);
            float half = Mathf.Max(0f, capsule.height * .5f - capsule.radius);
            Vector3 center = body.position + transform.rotation * capsule.center;
            Vector3 bottom = center - up * half;
            Vector3 start = bottom + up * .18f;
            float distance = .48f;
            RaycastHit[] hits = Physics.SphereCastAll(start, castRadius, -up, distance, terrainMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == capsule || hits[i].rigidbody == body) continue;
                float angle = Vector3.Angle(hits[i].normal, up);
                if (angle > settings.maxSlopeAngle) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    grounded = true;
                    groundNormal = hits[i].normal;
                }
            }
        }

        [ContextMenu("Respawn To Safe Surface")]
        public void RespawnToSafeSurface()
        {
            if (settings == null || planetCenter == null) return;
            if (planet == null) planet = planetCenter.GetComponent<PlanetPrototypeGenerator>();
            if (planet == null) return;

            initialized = false;
            body.isKinematic = true;
            Vector3 direction = transform.position == planetCenter.position ? new Vector3(.27f, .93f, .24f).normalized : (transform.position - planetCenter.position).normalized;
            if (!planet.TryFindSurface(direction, out Vector3 surface, out Vector3 normal)) return;
            transform.position = surface + normal * 1.15f;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            body.position = transform.position;
            body.rotation = transform.rotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            body.isKinematic = false;
            initialized = true;
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
            if (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0f) return Mouse.current.delta.ReadValue();
            return Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
        }

        private static bool JumpPressed() =>
            (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        private static bool IsSprinting() =>
            (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);
    }
}
