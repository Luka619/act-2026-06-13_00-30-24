using System.Collections.Generic;
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
        public IReadOnlyList<string> PressedInputTokens => pressedInputTokens;
        public IReadOnlyList<string> HeldInputTokens => heldInputTokens;

        private readonly List<string> pressedInputTokens = new List<string>();
        private readonly List<string> heldInputTokens = new List<string>();

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
            pressedInputTokens.Clear();
            heldInputTokens.Clear();
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

            AddPressed(AttackPressed, CombatInputActionNames.LightAttack);
            AddPressed(HeavyPressed, CombatInputActionNames.HeavyAttack);
            AddPressed(DodgePressed, CombatInputActionNames.Dodge);
            AddPressed(JumpPressed, CombatInputActionNames.Jump);
            AddPressed(gamepad.leftShoulder.wasPressedThisFrame, CombatInputActionNames.Guard);
            AddPressed(gamepad.rightShoulder.wasPressedThisFrame, CombatInputActionNames.ButtonR1);
            AddPressed(gamepad.leftTrigger.wasPressedThisFrame, CombatInputActionNames.ButtonL2);
            AddPressed(gamepad.rightTrigger.wasPressedThisFrame, CombatInputActionNames.ButtonR2);
            AddPressed(gamepad.leftStickButton.wasPressedThisFrame, CombatInputActionNames.ButtonL3);
            AddPressed(gamepad.rightStickButton.wasPressedThisFrame, CombatInputActionNames.ButtonR3);
            AddPressed(gamepad.dpad.up.wasPressedThisFrame, CombatInputActionNames.DPadUp);
            AddPressed(gamepad.dpad.down.wasPressedThisFrame, CombatInputActionNames.DPadDown);
            AddPressed(gamepad.dpad.left.wasPressedThisFrame, CombatInputActionNames.DPadLeft);
            AddPressed(gamepad.dpad.right.wasPressedThisFrame, CombatInputActionNames.DPadRight);

            AddHeld(gamepad.leftShoulder.isPressed, CombatInputActionNames.Guard);
            AddHeld(gamepad.rightShoulder.isPressed, CombatInputActionNames.ButtonR1);
            AddHeld(gamepad.leftTrigger.isPressed, CombatInputActionNames.ButtonL2);
            AddHeld(gamepad.rightTrigger.isPressed, CombatInputActionNames.ButtonR2);
            AddHeld(gamepad.leftStickButton.isPressed, CombatInputActionNames.ButtonL3);
            AddHeld(gamepad.rightStickButton.isPressed, CombatInputActionNames.ButtonR3);
            AddHeld(gamepad.dpad.up.isPressed, CombatInputActionNames.DPadUp);
            AddHeld(gamepad.dpad.down.isPressed, CombatInputActionNames.DPadDown);
            AddHeld(gamepad.dpad.left.isPressed, CombatInputActionNames.DPadLeft);
            AddHeld(gamepad.dpad.right.isPressed, CombatInputActionNames.DPadRight);
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

            AddPressed(AttackPressed, CombatInputActionNames.LightAttack);
            AddPressed(HeavyPressed, CombatInputActionNames.HeavyAttack);
            AddPressed(DodgePressed, CombatInputActionNames.Dodge);
            AddPressed(JumpPressed, CombatInputActionNames.Jump);
            AddPressed(keyboard.leftShiftKey.wasPressedThisFrame, CombatInputActionNames.Guard);
            AddPressed(keyboard.eKey.wasPressedThisFrame, CombatInputActionNames.ButtonR1);
            AddPressed(keyboard.qKey.wasPressedThisFrame, CombatInputActionNames.ButtonL2);
            AddPressed(keyboard.rKey.wasPressedThisFrame, CombatInputActionNames.ButtonR2);
            AddPressed(LockOnPressed, CombatInputActionNames.ButtonR3);

            AddHeld(keyboard.leftShiftKey.isPressed, CombatInputActionNames.Guard);
            AddHeld(keyboard.eKey.isPressed, CombatInputActionNames.ButtonR1);
            AddHeld(keyboard.qKey.isPressed, CombatInputActionNames.ButtonL2);
            AddHeld(keyboard.rKey.isPressed, CombatInputActionNames.ButtonR2);
            AddHeld(keyboard.tabKey.isPressed, CombatInputActionNames.ButtonR3);
        }

        private void AddPressed(bool pressed, string token)
        {
            if (pressed)
            {
                pressedInputTokens.Add(token);
            }
        }

        private void AddHeld(bool held, string token)
        {
            if (held)
            {
                heldInputTokens.Add(token);
            }
        }

        private Vector2 ApplyDeadzone(Vector2 value)
        {
            return value.magnitude < stickDeadzone ? Vector2.zero : value;
        }
    }
}
