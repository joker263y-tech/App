using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shadowbound.Game.Player
{
    /// <summary>
    /// Turns player input into <see cref="CombatIntent"/>.
    ///
    /// This is the only place that knows a human is playing. The core simulation
    /// cannot tell this apart from the enemy brain, which is why the player and
    /// the enemies move through exactly the same code path.
    ///
    /// Movement is camera-relative: pushing forward moves the Warden away from the
    /// camera, not along a fixed world axis. Facing is left to the core, which
    /// turns the combatant toward its movement direction, so the character always
    /// faces where it is going without a second source of truth here.
    ///
    /// Device input is polled directly rather than through an Input Action asset,
    /// so this works with no authored asset to import or wire up. The on-screen
    /// controls feed in through <see cref="SetMoveAxis"/> and
    /// <see cref="RequestAbility"/>, which is what makes the same code drive both
    /// a keyboard in the editor and a touchscreen on a phone.
    /// </summary>
    public sealed class PlayerInputDriver : ICombatantDriver
    {
        /// <summary>Camera used for movement and looking. Set by the bootstrap.</summary>
        public Transform CameraTransform;

        /// <summary>How fast the camera turns per unit of drag. Radians.</summary>
        public float LookSensitivity = 0.12f;

        public float MinPitchDegrees = -35f;
        public float MaxPitchDegrees = 60f;

        /// <summary>Enables keyboard and mouse. Disable on a pure touch build.</summary>
        public bool DeviceInputEnabled = true;

        private Vector2 _touchMoveAxis;
        private int _queuedAbility = -1;
        private int _heldAbility = -1;
        private Vector2 _lookDelta;

        private float _yaw;
        private float _pitch = 12f;

        /// <summary>Current camera yaw in degrees around Y.</summary>
        public float Yaw
        {
            get { return _yaw; }
        }

        public float Pitch
        {
            get { return _pitch; }
        }

        /// <summary>Movement axis from the on-screen stick, in -1..1 on each axis.</summary>
        public void SetMoveAxis(Vector2 axis)
        {
            _touchMoveAxis = Vector2.ClampMagnitude(axis, 1f);
        }

        /// <summary>Camera drag from the on-screen look area, in pixels.</summary>
        public void AddLookDelta(Vector2 deltaPixels)
        {
            _lookDelta += deltaPixels;
        }

        /// <summary>Queues an ability press from an on-screen button or a key.</summary>
        public void RequestAbility(int index)
        {
            if (index >= 0)
            {
                _queuedAbility = index;
            }
        }

        /// <summary>Marks an ability as held, for a held basic attack.</summary>
        public void SetHeldAbility(int index)
        {
            _heldAbility = index;
        }

        public void SetYawPitch(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, MinPitchDegrees, MaxPitchDegrees);
        }

        /// <summary>Reads devices for this frame. Called once per frame before the session is ticked.</summary>
        public void Poll()
        {
            if (DeviceInputEnabled)
            {
                PollKeyboard();
                PollMouse();
            }

            ApplyLookDelta();
        }

        public CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world)
        {
            CombatIntent intent = CombatIntent.None();

            Vector2 move = ReadMoveAxis();

            if (move.sqrMagnitude > 1f)
            {
                move = move.normalized;
            }

            Float3 direction = ToWorldDirection(move);
            if (direction != Float3.Zero)
            {
                intent.MoveDirection = direction;
                intent.SpeedScale = 1f;
            }

            int ability = ConsumeAbilityRequest();
            if (ability >= 0)
            {
                intent.ActivateAbility = true;
                intent.AbilityIndex = ability;
            }

            return intent;
        }

        private Vector2 ReadMoveAxis()
        {
            Vector2 move = _touchMoveAxis;

            if (!DeviceInputEnabled)
            {
                return move;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return move;
            }

            float x = 0f;
            float y = 0f;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                x -= 1f;
            }

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                x += 1f;
            }

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                y -= 1f;
            }

            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                y += 1f;
            }

            if (x == 0f && y == 0f)
            {
                return move;
            }

            var device = new Vector2(x, y);
            return device.sqrMagnitude > 1f ? device.normalized : device;
        }

        /// <summary>
        /// Converts a stick or key axis into a world direction relative to where the
        /// camera is looking, flattened onto the ground plane.
        /// </summary>
        private Float3 ToWorldDirection(Vector2 axis)
        {
            if (axis.sqrMagnitude <= 0.0004f)
            {
                return Float3.Zero;
            }

            float yawRadians = _yaw * Mathf.Deg2Rad;
            var cameraForward = new Vector3(Mathf.Sin(yawRadians), 0f, Mathf.Cos(yawRadians));
            var cameraRight = new Vector3(cameraForward.z, 0f, -cameraForward.x);

            Vector3 world = (cameraForward * axis.y) + (cameraRight * axis.x);

            if (world.sqrMagnitude <= 0.000001f)
            {
                return Float3.Zero;
            }

            world.Normalize();
            return new Float3(world.x, 0f, world.z);
        }

        private int ConsumeAbilityRequest()
        {
            int queued = _queuedAbility;
            _queuedAbility = -1;

            if (queued >= 0)
            {
                return queued;
            }

            // A held attack keeps swinging without needing repeated presses.
            return _heldAbility;
        }

        private void ApplyLookDelta()
        {
            Vector2 delta = _lookDelta;
            _lookDelta = Vector2.zero;

            if (delta.sqrMagnitude <= 0f)
            {
                return;
            }

            _yaw += delta.x * LookSensitivity;
            _pitch = Mathf.Clamp(_pitch - (delta.y * LookSensitivity), MinPitchDegrees, MaxPitchDegrees);
        }

        private void PollKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame) { RequestAbility(0); }
            if (keyboard.digit2Key.wasPressedThisFrame) { RequestAbility(1); }
            if (keyboard.digit3Key.wasPressedThisFrame) { RequestAbility(2); }
            if (keyboard.digit4Key.wasPressedThisFrame) { RequestAbility(3); }
            if (keyboard.digit5Key.wasPressedThisFrame) { RequestAbility(4); }

            // Space is Ashstep, the mobility option.
            if (keyboard.spaceKey.wasPressedThisFrame) { RequestAbility(2); }

            // Camera turning from the keyboard, for testing without a mouse.
            float turn = 0f;
            if (keyboard.qKey.isPressed) { turn -= 1f; }
            if (keyboard.eKey.isPressed) { turn += 1f; }

            if (turn != 0f)
            {
                _lookDelta += new Vector2(turn * 260f * Time.deltaTime, 0f);
            }
        }

        private void PollMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            // Holding the left button keeps the basic attack swinging, which is
            // what makes the melee loop bearable without mashing.
            SetHeldAbility(mouse.leftButton.isPressed ? 0 : -1);

            // The camera turns on a right-button drag only, so that clicking to
            // attack does not also spin the view out from under the player.
            Vector2 delta = mouse.delta.ReadValue();
            if (mouse.rightButton.isPressed && delta.sqrMagnitude > 0f)
            {
                _lookDelta += delta;
            }
        }
    }
}
