using UnityEngine;
using UnityEngine.InputSystem;

namespace DoctorWho.VoxelUniverse.Input
{
    /// <summary>
    /// Central Input System adapter for the VoxelUniverse runtime.
    /// Keeps gameplay code independent from legacy UnityEngine.Input.
    /// </summary>
    public static class VoxelInput
    {
        public static Vector2 Move
        {
            get
            {
                Vector2 value = Vector2.zero;
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) value.x -= 1f;
                    if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) value.x += 1f;
                    if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) value.y -= 1f;
                    if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) value.y += 1f;
                }

                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                    value += gamepad.leftStick.ReadValue();

                return Vector2.ClampMagnitude(value, 1f);
            }
        }

        public static Vector2 LookDelta
        {
            get
            {
                Vector2 value = Mouse.current != null
                    ? Mouse.current.delta.ReadValue()
                    : Vector2.zero;

                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                    value += gamepad.rightStick.ReadValue() * (700f * Time.unscaledDeltaTime);

                return value;
            }
        }

        public static float ScrollY
        {
            get { return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f; }
        }

        public static bool EscapePressed
        {
            get
            {
                return Pressed(Keyboard.current != null ? Keyboard.current.escapeKey : null)
                    || Pressed(Gamepad.current != null ? Gamepad.current.startButton : null);
            }
        }

        public static bool RespawnPressed
        {
            get
            {
                return Pressed(Keyboard.current != null ? Keyboard.current.rKey : null)
                    || Pressed(Gamepad.current != null ? Gamepad.current.dpad.down : null);
            }
        }

        public static bool JumpPressed
        {
            get
            {
                return Pressed(Keyboard.current != null ? Keyboard.current.spaceKey : null)
                    || Pressed(Gamepad.current != null ? Gamepad.current.buttonSouth : null);
            }
        }

        public static bool JumpHeld
        {
            get
            {
                return Held(Keyboard.current != null ? Keyboard.current.spaceKey : null)
                    || Held(Gamepad.current != null ? Gamepad.current.buttonSouth : null);
            }
        }

        public static bool FlightTogglePressed
        {
            get
            {
                return Pressed(Keyboard.current != null ? Keyboard.current.fKey : null)
                    || Pressed(Gamepad.current != null ? Gamepad.current.dpad.up : null);
            }
        }

        public static bool SprintHeld
        {
            get
            {
                return Held(Keyboard.current != null ? Keyboard.current.leftShiftKey : null)
                    || Held(Keyboard.current != null ? Keyboard.current.rightShiftKey : null)
                    || Held(Gamepad.current != null ? Gamepad.current.leftStickButton : null);
            }
        }

        public static bool DescendHeld
        {
            get
            {
                return Held(Keyboard.current != null ? Keyboard.current.leftCtrlKey : null)
                    || Held(Keyboard.current != null ? Keyboard.current.rightCtrlKey : null)
                    || Held(Keyboard.current != null ? Keyboard.current.cKey : null)
                    || Held(Gamepad.current != null ? Gamepad.current.buttonEast : null);
            }
        }

        public static bool InventoryPressed
        {
            get
            {
                return Pressed(Keyboard.current != null ? Keyboard.current.eKey : null)
                    || Pressed(Gamepad.current != null ? Gamepad.current.selectButton : null);
            }
        }

        public static bool PreviousHotbarPressed
        {
            get
            {
                return ScrollY > 0.01f
                    || Pressed(Gamepad.current != null ? Gamepad.current.leftShoulder : null);
            }
        }

        public static bool NextHotbarPressed
        {
            get
            {
                return ScrollY < -0.01f
                    || Pressed(Gamepad.current != null ? Gamepad.current.rightShoulder : null);
            }
        }

        public static bool HotbarSlotPressed(int index)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || index < 0 || index > 8) return false;

            switch (index)
            {
                case 0: return keyboard.digit1Key.wasPressedThisFrame;
                case 1: return keyboard.digit2Key.wasPressedThisFrame;
                case 2: return keyboard.digit3Key.wasPressedThisFrame;
                case 3: return keyboard.digit4Key.wasPressedThisFrame;
                case 4: return keyboard.digit5Key.wasPressedThisFrame;
                case 5: return keyboard.digit6Key.wasPressedThisFrame;
                case 6: return keyboard.digit7Key.wasPressedThisFrame;
                case 7: return keyboard.digit8Key.wasPressedThisFrame;
                case 8: return keyboard.digit9Key.wasPressedThisFrame;
                default: return false;
            }
        }

        public static bool PrimaryPressed
        {
            get
            {
                return Pressed(Mouse.current != null ? Mouse.current.leftButton : null)
                    || Pressed(Gamepad.current != null ? Gamepad.current.rightTrigger : null);
            }
        }

        public static bool SecondaryPressed
        {
            get
            {
                return Pressed(Mouse.current != null ? Mouse.current.rightButton : null)
                    || Pressed(Gamepad.current != null ? Gamepad.current.leftTrigger : null);
            }
        }

        public static bool DiagnosticsPressed
        {
            get { return Pressed(Keyboard.current != null ? Keyboard.current.f3Key : null); }
        }

        private static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl control)
        {
            return control != null && control.wasPressedThisFrame;
        }

        private static bool Held(UnityEngine.InputSystem.Controls.ButtonControl control)
        {
            return control != null && control.isPressed;
        }
    }
}
