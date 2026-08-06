using DoctorWho.VoxelUniverse.Collision;
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

        private Vector3 velocity;
        private float pitch;
        private bool grounded;
        private bool flying;
        private float lastJumpPressTime = -10f;
        private bool waitingForSupport = true;

        public bool Flying { get { return flying; } }
        public Camera PlayerCamera { get { return playerCamera; } }
        public float CapsuleRadius { get { return world != null && world.Settings != null ? world.Settings.capsuleRadius : 0.38f; } }
        public float CapsuleHeight { get { return world != null && world.Settings != null ? world.Settings.capsuleHeight : 1.8f; } }

        public void Configure(
            VoxelUniverseWorld voxelWorld,
            VoxelCollisionWorld logicalCollision,
            VoxelInventory playerInventory,
            Transform pivot,
            Camera camera)
        {
            world = voxelWorld;
            collisionWorld = logicalCollision;
            inventory = playerInventory;
            cameraPivot = pivot;
            playerCamera = camera;
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
            HandleCursor();
            HandleLook();
            HandleFlightToggle();

            VoxelAddress address = world.GetAddress(transform.position + transform.up * (CapsuleHeight * 0.5f));
            world.PrioritizeAddress(address);
            waitingForSupport = !world.IsSectionReady(address.SectionKey);
            if (waitingForSupport)
            {
                velocity = Vector3.zero;
                return;
            }

            MovePlayer();

            float safeInnerRadius = world.Settings.groundRadius + world.Settings.minimumRadialBlock + 3f;
            if ((transform.position - world.Center).magnitude < safeInnerRadius || Input.GetKeyDown(KeyCode.R))
                Respawn();
        }

        private void HandleCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
        }

        private void HandleLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked || (inventory != null && inventory.InventoryOpen)) return;
            float sensitivity = world.Settings.mouseSensitivity;
            float mouseX = Input.GetAxisRaw("Mouse X") * sensitivity * 10f;
            float mouseY = Input.GetAxisRaw("Mouse Y") * sensitivity * 10f;
            transform.Rotate(0f, mouseX, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - mouseY, -88f, 88f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void HandleFlightToggle()
        {
            bool jumpDown = Input.GetKeyDown(KeyCode.Space);
            if (jumpDown)
            {
                if (Time.unscaledTime - lastJumpPressTime <= 0.32f)
                {
                    flying = !flying;
                    velocity = Vector3.zero;
                    lastJumpPressTime = -10f;
                }
                else
                {
                    lastJumpPressTime = Time.unscaledTime;
                }
            }
            if (Input.GetKeyDown(KeyCode.F))
            {
                flying = !flying;
                velocity = Vector3.zero;
            }
        }

        private void MovePlayer()
        {
            Vector3 radialUp = (transform.position - world.Center).normalized;
            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, radialUp) * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-14f * Time.deltaTime));

            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            Vector3 cameraForward = playerCamera != null ? playerCamera.transform.forward : transform.forward;
            Vector3 cameraRight = playerCamera != null ? playerCamera.transform.right : transform.right;
            Vector3 forward = Vector3.ProjectOnPlane(cameraForward, radialUp).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraRight, radialUp).normalized;
            Vector3 wish = Vector3.ClampMagnitude(forward * vertical + right * horizontal, 1f);
            bool sprint = Input.GetKey(KeyCode.LeftShift);

            if (flying)
            {
                float verticalFlight = 0f;
                if (Input.GetKey(KeyCode.Space)) verticalFlight += 1f;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) verticalFlight -= 1f;
                float speed = sprint ? world.Settings.flightSprintSpeed : world.Settings.flightSpeed;
                velocity = (wish + radialUp * verticalFlight).normalized * speed;
                transform.position += velocity * Time.deltaTime;
                grounded = false;
                return;
            }

            float targetSpeed = sprint ? world.Settings.sprintSpeed : world.Settings.walkSpeed;
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, radialUp);
            Vector3 desired = wish * targetSpeed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desired, 30f * Time.deltaTime);
            float verticalSpeed = Vector3.Dot(velocity, radialUp);

            if (grounded && Input.GetKeyDown(KeyCode.Space))
            {
                verticalSpeed = world.Settings.jumpSpeed;
                grounded = false;
            }
            else
            {
                verticalSpeed -= world.Settings.gravity * Time.deltaTime;
            }

            velocity = horizontalVelocity + radialUp * verticalSpeed;
            bool resolvedGrounded;
            Vector3 resolved = collisionWorld.ResolveMotion(
                transform.position,
                velocity * Time.deltaTime,
                world.Settings.capsuleRadius,
                world.Settings.capsuleHeight,
                world.Settings.stepHeight,
                out resolvedGrounded);
            transform.position = resolved;
            grounded = resolvedGrounded;
            if (grounded && verticalSpeed < 0f)
                velocity = Vector3.ProjectOnPlane(velocity, radialUp);
        }

        public bool WouldOverlap(VoxelAddress address)
        {
            return collisionWorld != null && collisionWorld.CapsuleOverlapsBlock(
                transform.position,
                CapsuleRadius,
                CapsuleHeight,
                address);
        }

        public void Respawn()
        {
            if (world == null || world.Settings == null) return;
            VoxelAddress surface = world.FindSurfaceAddress(Vector3.up);
            world.PrioritizeAddress(surface);
            Vector3 center = world.GetBlockCenter(surface);
            Vector3 up = (center - world.Center).normalized;
            transform.position = center + up * (world.Settings.capsuleHeight + 1.2f);
            transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            velocity = Vector3.zero;
            flying = false;
        }

        private void OnGUI()
        {
            if (!waitingForSupport) return;
            Rect rect = new Rect((Screen.width - 360f) * 0.5f, 40f, 360f, 44f);
            GUI.Box(rect, "Loading supporting voxel section — movement paused");
        }
    }
}
