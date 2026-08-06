using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DoctorWho.BlockPlanets
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class BlockPlanetPlayerController : MonoBehaviour
    {
        [SerializeField] private BlockPlanetWorld world;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private BlockPlanetSettings settings;
        [SerializeField] private BlockInventory inventory;
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float gamepadLookSpeed = 130f;

        private Rigidbody body;
        private CapsuleCollider capsule;
        private float pitch;
        private float pendingYaw;
        private Vector2 moveInput;
        private bool jumpQueued;
        private bool grounded;
        private bool ready;
        private bool waitingForChunks;
        private bool respawning;

        public void Configure(BlockPlanetWorld owner, Transform pivot, BlockPlanetSettings value, BlockInventory blockInventory)
        {
            world = owner;
            cameraPivot = pivot;
            settings = value;
            inventory = blockInventory;
            if (world != null) world.SetObserver(transform);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            capsule.height = 1.8f;
            capsule.radius = 0.36f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
        }

        private IEnumerator Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (world != null) world.SetObserver(transform);
            yield return RespawnRoutine();
        }

        private void Update()
        {
            if (settings == null || world == null) return;
            bool uiOpen = inventory != null && inventory.IsOpen;
            Vector2 keyboardMove = Vector2.zero;
            if (!uiOpen && Keyboard.current != null)
            {
                keyboardMove.x = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
                keyboardMove.y = (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
                if (Keyboard.current.spaceKey.wasPressedThisFrame) jumpQueued = true;
                if (Keyboard.current.rKey.wasPressedThisFrame) RespawnToSafeSurface();
            }
            Vector2 gamepadMove = !uiOpen && Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
            moveInput = Vector2.ClampMagnitude(keyboardMove + gamepadMove, 1f);

            if (!uiOpen)
            {
                Vector2 look = Vector2.zero;
                if (Mouse.current != null) look += Mouse.current.delta.ReadValue() * mouseSensitivity;
                if (Gamepad.current != null) look += Gamepad.current.rightStick.ReadValue() * gamepadLookSpeed * Time.unscaledDeltaTime;
                pendingYaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -88f, 88f);
                if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            if (ready && !uiOpen && Mouse.current != null && cameraPivot != null)
            {
                Camera camera = cameraPivot.GetComponentInChildren<Camera>();
                if (camera != null)
                {
                    Ray ray = new Ray(camera.transform.position, camera.transform.forward);
                    BlockId affected;
                    if (Mouse.current.leftButton.wasPressedThisFrame && world.TryModify(ray, false, BlockId.Air, out affected))
                    {
                        if (inventory != null) inventory.Add(affected, 1);
                    }
                    if (Mouse.current.rightButton.wasPressedThisFrame && inventory != null && inventory.CanPlaceSelected())
                    {
                        if (world.TryModify(ray, true, inventory.SelectedBlock, out affected)) inventory.ConsumeSelected();
                    }
                }
            }
        }

        private void FixedUpdate()
        {
            if (!ready || settings == null || world == null) return;
            float radialDistance = (body.position - world.Center).magnitude;
            if (radialDistance < world.SafetyRadius + 1.5f)
            {
                RespawnToSafeSurface();
                return;
            }

            if (!world.IsAreaReady(body.position))
            {
                waitingForChunks = true;
                body.isKinematic = true;
                world.BuildImmediateNearObserver(settings.initialChunkBuildsPerFrame);
                return;
            }
            if (waitingForChunks)
            {
                waitingForChunks = false;
                body.isKinematic = false;
                body.velocity = Vector3.zero;
            }

            Vector3 up = (body.position - world.Center).normalized;
            Quaternion aligned = Quaternion.FromToRotation(transform.up, up) * body.rotation;
            if (Mathf.Abs(pendingYaw) > 0.001f)
            {
                aligned = Quaternion.AngleAxis(pendingYaw, up) * aligned;
                pendingYaw = 0f;
            }
            body.MoveRotation(Quaternion.Slerp(body.rotation, aligned, 18f * Time.fixedDeltaTime));

            ProbeGround(up);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 desired = right * moveInput.x + forward * moveInput.y;
            if (desired.sqrMagnitude > 1f) desired.Normalize();
            bool sprint = (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ||
                          (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);
            float speed = sprint ? settings.sprintSpeed : settings.walkSpeed;
            Vector3 radialVelocity = Vector3.Project(body.velocity, up);
            Vector3 tangentVelocity = body.velocity - radialVelocity;
            float acceleration = grounded ? settings.groundAcceleration : settings.airAcceleration;
            tangentVelocity = Vector3.MoveTowards(tangentVelocity, desired * speed, acceleration * Time.fixedDeltaTime);
            if (grounded && Vector3.Dot(radialVelocity, up) < 0f) radialVelocity = Vector3.zero;
            body.velocity = tangentVelocity + radialVelocity;
            body.AddForce(-up * settings.gravity, ForceMode.Acceleration);

            if (jumpQueued)
            {
                jumpQueued = false;
                if (grounded)
                {
                    body.velocity = Vector3.ProjectOnPlane(body.velocity, up) + up * settings.jumpSpeed;
                    grounded = false;
                }
            }
        }

        private void ProbeGround(Vector3 up)
        {
            grounded = false;
            Vector3 origin = body.position + up * 0.52f;
            RaycastHit hit;
            if (Physics.SphereCast(origin, 0.30f, -up, out hit, 0.52f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponent<BlockPlanetChunk>() != null)
                    grounded = Vector3.Angle(hit.normal, up) <= settings.maxSlopeAngle;
            }
        }

        public void RespawnToSafeSurface()
        {
            if (!respawning) StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            if (world == null || settings == null) yield break;
            respawning = true;
            ready = false;
            body.isKinematic = true;
            Vector3 direction = (transform.position - world.Center).sqrMagnitude > 1f
                ? (transform.position - world.Center).normalized
                : new Vector3(0.27f, 0.93f, 0.24f).normalized;
            transform.position = world.GetSurfacePoint(direction) + direction * 4f;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
            world.SetObserver(transform);

            for (int frame = 0; frame < 240; frame++)
            {
                world.BuildImmediateNearObserver(settings.initialChunkBuildsPerFrame);
                Vector3 up = (transform.position - world.Center).normalized;
                if (world.IsAreaReady(transform.position))
                {
                    Ray ray = new Ray(world.GetSurfacePoint(up) + up * 18f, -up);
                    RaycastHit hit;
                    if (Physics.Raycast(ray, out hit, 40f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponent<BlockPlanetChunk>() != null)
                    {
                        transform.position = hit.point + up * 0.06f;
                        body.velocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                        body.isKinematic = false;
                        ready = true;
                        respawning = false;
                        yield break;
                    }
                }
                yield return null;
            }

            transform.position = world.GetSurfacePoint(direction) + direction * 2f;
            body.isKinematic = false;
            ready = true;
            respawning = false;
        }

        private void OnGUI()
        {
            if (!ready)
            {
                GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, normal = { textColor = Color.white } };
                GUI.Box(new Rect(Screen.width * 0.5f - 170f, Screen.height * 0.5f - 35f, 340f, 70f), GUIContent.none);
                GUI.Label(new Rect(Screen.width * 0.5f - 160f, Screen.height * 0.5f - 25f, 320f, 50f), "Building safe terrain colliders...", style);
            }
        }
    }
}
