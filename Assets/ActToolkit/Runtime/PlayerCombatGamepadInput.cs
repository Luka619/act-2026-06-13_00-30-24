using UnityEngine;
using UnityEngine.InputSystem;

namespace ActToolkit
{
    public sealed class PlayerCombatGamepadInput : MonoBehaviour
    {
        [SerializeField, Range(0f, 0.9f)]
        private float stickDeadzone = 0.18f;

        [SerializeField]
        private bool keyboardFallback = true;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool AttackHeld { get; private set; }
        public bool HeavyPressed { get; private set; }
        public bool DodgePressed { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool GuardHeld { get; private set; }
        public bool LockOnPressed { get; private set; }
        public string ActiveDeviceName { get; private set; } = "None";

        private void Update()
        {
            ResetFrameButtons();

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                ReadGamepad(gamepad);
                return;
            }

            if (keyboardFallback)
            {
                ReadKeyboardFallback();
            }
            else
            {
                Move = Vector2.zero;
                Look = Vector2.zero;
                AttackHeld = false;
                GuardHeld = false;
                ActiveDeviceName = "None";
            }
        }

        private void ResetFrameButtons()
        {
            AttackPressed = false;
            HeavyPressed = false;
            DodgePressed = false;
            JumpPressed = false;
            LockOnPressed = false;
        }

        private void ReadGamepad(Gamepad gamepad)
        {
            ActiveDeviceName = string.IsNullOrWhiteSpace(gamepad.displayName) ? gamepad.name : gamepad.displayName;

            Move = ApplyDeadzone(gamepad.leftStick.ReadValue());
            Look = ApplyDeadzone(gamepad.rightStick.ReadValue());

            // PS5 physical layout through Unity Gamepad:
            // west = Square, south = Cross, east = Circle, north = Triangle.
            AttackPressed = gamepad.buttonWest.wasPressedThisFrame;
            AttackHeld = gamepad.buttonWest.isPressed;
            HeavyPressed = gamepad.buttonNorth.wasPressedThisFrame;
            DodgePressed = gamepad.buttonEast.wasPressedThisFrame;
            JumpPressed = gamepad.buttonSouth.wasPressedThisFrame;
            GuardHeld = gamepad.leftShoulder.isPressed;
            LockOnPressed = gamepad.rightStickButton.wasPressedThisFrame;
        }

        private void ReadKeyboardFallback()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Move = Vector2.zero;
                Look = Vector2.zero;
                AttackHeld = false;
                GuardHeld = false;
                ActiveDeviceName = "None";
                return;
            }

            ActiveDeviceName = "Keyboard";

            Vector2 move = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                move.y += 1f;
            }

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                move.y -= 1f;
            }

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                move.x += 1f;
            }

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                move.x -= 1f;
            }

            Move = move.sqrMagnitude > 1f ? move.normalized : move;
            Look = Vector2.zero;

            AttackPressed = keyboard.jKey.wasPressedThisFrame;
            AttackHeld = keyboard.jKey.isPressed;
            HeavyPressed = keyboard.kKey.wasPressedThisFrame;
            DodgePressed = keyboard.spaceKey.wasPressedThisFrame;
            JumpPressed = keyboard.enterKey.wasPressedThisFrame;
            GuardHeld = keyboard.leftShiftKey.isPressed;
            LockOnPressed = keyboard.tabKey.wasPressedThisFrame;
        }

        private Vector2 ApplyDeadzone(Vector2 value)
        {
            return value.magnitude < stickDeadzone ? Vector2.zero : value;
        }
    }
}
