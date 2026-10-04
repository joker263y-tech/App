using Godot;

namespace Shadowbound.Game
{
    /// <summary>
    /// The player's input authority, ported from the project's earlier input
    /// drivers.
    ///
    /// It owns every input decision - move axis, queued ability, held attack, look
    /// and touch - and exposes them as plain queries. <see cref="PlayerDriver"/>
    /// reads those queries once per simulation step and turns them into a
    /// CombatIntent, so this class never touches the core and never moves anything.
    ///
    /// Camera angles live here, not on the camera, because movement is
    /// camera-relative: the input layer has to know which way the camera faces to
    /// decide what "forward" means, and the rig reads the same angles back.
    /// </summary>
    public partial class PlayerInputReader : Node
    {
        /// <summary>Degrees of yaw/pitch per pixel of look input.</summary>
        public float LookSensitivity = 0.12f;

        /// <summary>Keyboard camera-orbit speed, degrees per second, for Q/E.</summary>
        public float CameraTurnSpeed = 140f;

        public float MinPitchDegrees = -35f;
        public float MaxPitchDegrees = 60f;

        /// <summary>Enables keyboard and mouse. Turned off on a pure touch build.</summary>
        public bool DeviceInputEnabled = true;

        private Vector2 _touchMoveAxis;
        private Vector2 _lookDeltaPixels;
        private int _queuedAbility = -1;
        private int _heldAbility = -1;

        private float _yaw;
        private float _pitch = 12f;

        /// <summary>Current camera yaw in core degrees around Y.</summary>
        public float Yaw => _yaw;

        /// <summary>Current camera pitch in degrees; positive looks up.</summary>
        public float Pitch => _pitch;

        public void SetTouchMoveAxis(Vector2 axis)
        {
            _touchMoveAxis = axis.LengthSquared() > 1f ? axis.Normalized() : axis;
        }

        /// <summary>Camera drag from the on-screen look area, in pixels.</summary>
        public void AddLookDelta(Vector2 deltaPixels)
        {
            _lookDeltaPixels += deltaPixels;
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

        public Vector2 ReadMoveAxis()
        {
            Vector2 axis = _touchMoveAxis;

            if (!DeviceInputEnabled)
            {
                return axis;
            }

            // Keyboard overrides touch when it is actually being used.
            Vector2 keys = Input.GetVector("move_left", "move_right", "move_back", "move_forward");
            if (keys.LengthSquared() > 0.0004f)
            {
                axis = keys.LengthSquared() > 1f ? keys.Normalized() : keys;
            }

            return axis;
        }

        /// <summary>Pops the queued ability if there is one, otherwise the held attack.</summary>
        public bool ConsumeAbility(out int index)
        {
            if (_queuedAbility >= 0)
            {
                index = _queuedAbility;
                _queuedAbility = -1;
                return true;
            }

            if (_heldAbility >= 0)
            {
                index = _heldAbility;
                return true;
            }

            index = -1;
            return false;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!DeviceInputEnabled)
            {
                return;
            }

            if (@event is InputEventMouseMotion motion)
            {
                // The camera turns on mouse motion. On-screen UI consumes its own
                // events first, so a click on a menu button does not also spin the view.
                AddLookDelta(motion.Relative);
            }
        }

        /// <summary>Reads the device once per frame, before the session is stepped.</summary>
        public void Poll(double delta)
        {
            if (DeviceInputEnabled)
            {
                PollKeyboard();
                PollMouse();

                if (Input.IsActionPressed("camera_turn_left")) { _yaw -= (float)(CameraTurnSpeed * delta); }
                if (Input.IsActionPressed("camera_turn_right")) { _yaw += (float)(CameraTurnSpeed * delta); }
            }

            ApplyLookDelta();
        }

        private void ApplyLookDelta()
        {
            Vector2 delta = _lookDeltaPixels;
            _lookDeltaPixels = Vector2.Zero;

            if (delta.LengthSquared() <= 0f)
            {
                return;
            }

            _yaw += delta.X * LookSensitivity;
            _pitch = Mathf.Clamp(_pitch + (delta.Y * LookSensitivity), MinPitchDegrees, MaxPitchDegrees);
        }

        private void PollKeyboard()
        {
            if (Input.IsActionJustPressed("ability_1")) { RequestAbility(0); }
            if (Input.IsActionJustPressed("ability_2")) { RequestAbility(1); }
            if (Input.IsActionJustPressed("ability_3")) { RequestAbility(2); }
            if (Input.IsActionJustPressed("ability_4")) { RequestAbility(3); }
            if (Input.IsActionJustPressed("ability_5")) { RequestAbility(4); }

            // Space is Ashstep, the mobility option (index 2).
            if (Input.IsActionJustPressed("dash")) { RequestAbility(2); }
        }

        private void PollMouse()
        {
            // Holding the left button keeps the basic attack swinging, which is what
            // makes the melee loop bearable without mashing.
            SetHeldAbility(Input.IsActionPressed("attack") ? 0 : -1);
        }
    }
}
