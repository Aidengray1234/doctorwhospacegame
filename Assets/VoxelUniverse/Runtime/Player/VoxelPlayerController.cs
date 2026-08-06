using DoctorWho.VoxelUniverse.Collision;
using DoctorWho.VoxelUniverse.Input;
using DoctorWho.VoxelUniverse.Inventory;
using DoctorWho.VoxelUniverse.Rendering;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Player
{
    public sealed class VoxelPlayerController : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private VoxelCollisionWorld collisionWorld;
        [SerializeField] private VoxelInventory inventory;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private TangentVoxelClipmap tangentPatch;

        private Vector3 velocity;
        private float pitch;
        private bool grounded;
        private bool flying;
        private float lastJumpPressTime = -10f;
        private bool waitingForSupport = true;

        public bool Flying { get { return flying; } }
        public Camera PlayerCamera { get { return playerCamera; } }
        public float CapsuleRadius
        {
            get { return world != null && world.Settings != null
                ? world.Settings.capsuleRadius : 0.38f; }
        }
        public float CapsuleHeight
        {
            get { return world != null && world.Settings != null
                ? world.Settings.capsuleHeight : 1.8f; }
        }

        public void Configure(VoxelUniverseWorld voxelWorld, VoxelCollisionWorld logicalCollision,
            VoxelInventory playerInventory, Transform pivot, Camera camera)
        {
            world = voxelWorld;
            collisionWorld = logicalCollision;
            inventory = playerInventory;
            cameraPivot = pivot;
            playerCamera = camera;
            FindPatch();
        }

        private void Awake()
        {
            FindPatch();
        }

        private void FindPatch()
        {
            if (tangentPatch == null) tangentPatch = FindObjectOfType<TangentVoxelClipmap>();
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Respawn();
        }

        private void Update()
        {
            if (world == null || world.Settings == null || collisionWorld == null) return;
            FindPatch();
            HandleCursor();
            HandleLook();
            HandleFlightToggle();

            Vector3 radialUp = (transform.position - world.Center).normalized;
            if (radialUp.sqrMagnitude < 0.5f) radialUp = Vector3.up;
            float altitude = world.GetAltitude(transform.position);

            if (!flying && altitude > world.Settings.tangentPatchMaxAltitude)
            {
                Respawn();
                return;
            }

            if (!flying)
            {
                if (tangentPatch != null)
                {
                    waitingForSupport = !tangentPatch.Ready;
                }
                else
                {
                    VoxelAddress support = world.FindSurfaceAddress(radialUp);
                    world.PrioritizeAddress(support);
                    waitingForSupport = !world.IsSectionReady(support.SectionKey);
                }
                if (waitingForSupport)
                {
                    velocity = Vector3.zero;
                    return;
                }
            }
            else waitingForSupport = false;

            MovePlayer(radialUp);

            float safeInnerRadius = world.Settings.groundRadius
                                    + world.Settings.minimumRadialBlock + 3f;
            if ((transform.position - world.Center).magnitude < safeInnerRadius
                || VoxelInput.RespawnPressed)
                Respawn();
        }

        private void HandleCursor()
        {
            if (!VoxelInput.EscapePressed) return;
            if (inventory != null && inventory.InventoryOpen) return;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }

        private void HandleLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked
                || (inventory != null && inventory.InventoryOpen)) return;
            Vector2 look = VoxelInput.LookDelta;
            float sensitivity = world.Settings.mouseSensitivity;
            transform.Rotate(0f, look.x * sensitivity, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - look.y * sensitivity, -88f, 88f);
            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void HandleFlightToggle()
        {
            if (inventory != null && inventory.InventoryOpen) return;
            bool requested = false;
            if (VoxelInput.JumpPressed)
            {
                if (Time.unscaledTime - lastJumpPressTime <= 0.32f)
                {
                    requested = true;
                    lastJumpPressTime = -10f;
                }
                else lastJumpPressTime = Time.unscaledTime;
            }
            if (VoxelInput.FlightTogglePressed) requested = true;
            if (!requested) return;

            if (flying && world.GetAltitude(transform.position)
                > world.Settings.tangentPatchMaxAltitude)
            {
                Respawn();
                return;
            }
            flying = !flying;
            velocity = Vector3.zero;
        }

        private void MovePlayer(Vector3 radialUp)
        {
            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, radialUp)
                                        * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation,
                1f - Mathf.Exp(-14f * Time.deltaTime));

            Vector2 move = VoxelInput.Move;
            Vector3 cameraForward = playerCamera != null
                ? playerCamera.transform.forward : transform.forward;
            Vector3 cameraRight = playerCamera != null
                ? playerCamera.transform.right : transform.right;
            Vector3 forward = Vector3.ProjectOnPlane(cameraForward, radialUp).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraRight, radialUp).normalized;
            Vector3 wish = Vector3.ClampMagnitude(forward * move.y + right * move.x, 1f);
            bool sprint = VoxelInput.SprintHeld;

            if (flying)
            {
                float verticalFlight = 0f;
                if (VoxelInput.JumpHeld) verticalFlight += 1f;
                if (VoxelInput.DescendHeld) verticalFlight -= 1f;
                float speed = sprint ? world.Settings.flightSprintSpeed
                    : world.Settings.flightSpeed;
                Vector3 requested = wish + radialUp * verticalFlight;
                velocity = requested.sqrMagnitude > 0.0001f
                    ? requested.normalized * speed : Vector3.zero;
                transform.position += velocity * Time.deltaTime;
                grounded = false;
                return;
            }

            float targetSpeed = sprint ? world.Settings.sprintSpeed : world.Settings.walkSpeed;
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, radialUp);
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, wish * targetSpeed,
                30f * Time.deltaTime);
            float verticalSpeed = Vector3.Dot(velocity, radialUp);
            if (grounded && VoxelInput.JumpPressed)
            {
                verticalSpeed = world.Settings.jumpSpeed;
                grounded = false;
            }
            else verticalSpeed -= world.Settings.gravity * Time.deltaTime;

            velocity = horizontalVelocity + radialUp * verticalSpeed;
            bool resolvedGrounded;
            transform.position = collisionWorld.ResolveMotion(transform.position,
                velocity * Time.deltaTime, world.Settings.capsuleRadius,
                world.Settings.capsuleHeight, world.Settings.stepHeight,
                out resolvedGrounded);
            grounded = resolvedGrounded;
            if (grounded && verticalSpeed < 0f)
                velocity = Vector3.ProjectOnPlane(velocity, radialUp);
        }

        public bool WouldOverlap(VoxelAddress address)
        {
            return collisionWorld != null && collisionWorld.CapsuleOverlapsBlock(
                transform.position, CapsuleRadius, CapsuleHeight, address);
        }

        public void Respawn()
        {
            if (world == null || world.Settings == null) return;
            VoxelAddress surface = world.FindSurfaceAddress(Vector3.up);
            Vector3 center = world.Center + Vector3.up
                * (world.Settings.groundRadius + surface.radial + 1f);
            Vector3 up = (center - world.Center).normalized;
            transform.position = center + up * (world.Settings.capsuleHeight + 1.2f);
            transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            velocity = Vector3.zero;
            flying = false;
            waitingForSupport = true;
            if (tangentPatch != null) tangentPatch.NotifyLogicalEdit();
        }

        private void OnGUI()
        {
            if (!waitingForSupport) return;
            float width = Mathf.Min(460f, Screen.width - 24f);
            Rect rect = new Rect((Screen.width - width) * 0.5f, 36f, width, 42f);
            GUI.Box(rect, "Building no-warp cube terrain beneath the player — movement paused");
        }
    }
}
