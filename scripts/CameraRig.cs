using Godot;

namespace Shadowbound.Game
{
    /// <summary>
    /// Third-person follow camera.
    ///
    /// The camera is driven by yaw and pitch that live on the input reader rather
    /// than on the camera itself, because movement is camera-relative: the input
    /// layer needs the camera's facing to decide what "forward" means. Keeping the
    /// angles in one place stops the two from disagreeing.
    ///
    /// It pushes in when arena geometry intrudes between the camera and the Warden
    /// (a Godot space-state ray query) and recentres on a smoothed target, so the
    /// view does not judder against the step of a fixed-timestep simulation.
    /// </summary>
    public partial class CameraRig : Node3D
    {
        public Node3D Target;
        public PlayerInputReader Input;
        public Camera3D Camera;

        public float Distance = 6.5f;
        public float MinDistance = 2f;
        public float Height = 1.6f;
        public float FollowDamping = 12f;

        /// <summary>Layers the camera must not see through: the world and camera blockers.</summary>
        public uint ObstructionMask = Arena.WorldLayer | (1u << 2);

        private Vector3 _smoothedTarget;
        private float _currentDistance;
        private float _shake;
        private bool _initialised;

        /// <summary>Points the rig at a new target, e.g. after a region change.</summary>
        public void SetTarget(Node3D target)
        {
            Target = target;

            if (target != null)
            {
                _smoothedTarget = target.Position;
                _currentDistance = Distance;
                _initialised = true;
            }
        }

        /// <summary>Adds an impulse to the camera, for hit feedback.</summary>
        public void Shake(float amount)
        {
            _shake = Mathf.Max(_shake, Mathf.Clamp(amount, 0f, 1f));
        }

        public override void _PhysicsProcess(double deltaSeconds)
        {
            float delta = (float)deltaSeconds;

            if (Target == null)
            {
                return;
            }

            if (!_initialised)
            {
                SetTarget(Target);
            }

            Vector3 focus = Target.Position + new Vector3(0f, Height, 0f);
            _smoothedTarget = _smoothedTarget.Lerp(focus, 1f - Mathf.Exp(-FollowDamping * delta));

            float yawRadians = Mathf.DegToRad(Input?.Yaw ?? 0f);
            float pitchRadians = Mathf.DegToRad(Input?.Pitch ?? 12f);
            float cosPitch = Mathf.Cos(pitchRadians);

            var lookDirection = new Vector3(
                Mathf.Sin(yawRadians) * cosPitch,
                Mathf.Sin(pitchRadians),
                Mathf.Cos(yawRadians) * cosPitch);

            float desiredDistance = Distance;
            Vector3 desiredPosition = _smoothedTarget - (lookDirection * desiredDistance);

            World3D world = GetWorld3D();
            if (world != null)
            {
                PhysicsDirectSpaceState3D space = world.DirectSpaceState;
                if (space != null)
                {
                    var query = new PhysicsRayQueryParameters3D
                    {
                        From = _smoothedTarget,
                        To = desiredPosition,
                        CollisionMask = ObstructionMask,
                        CollideWithAreas = false,
                        CollideWithBodies = true
                    };

                    var hit = space.IntersectRay(query);
                    if (hit.Count > 0)
                    {
                        var point = (Vector3)hit["position"];
                        desiredDistance = Mathf.Max(MinDistance, _smoothedTarget.DistanceTo(point) - 0.2f);
                    }
                }
            }

            // Pull in instantly when something intrudes, ease back out afterwards.
            _currentDistance = desiredDistance < _currentDistance
                ? desiredDistance
                : Mathf.Lerp(_currentDistance, desiredDistance, 1f - Mathf.Exp(-6f * delta));

            Vector3 position = _smoothedTarget - (lookDirection * _currentDistance);

            if (_shake > 0f)
            {
                float magnitude = _shake * 0.12f;
                position += new Vector3(
                    (float)GD.RandRange(-magnitude, magnitude),
                    (float)GD.RandRange(-magnitude, magnitude),
                    0f);
                _shake = Mathf.Max(0f, _shake - (delta * 4f));
            }

            GlobalPosition = position;

            if (position.DistanceTo(_smoothedTarget) > 0.05f)
            {
                LookAt(_smoothedTarget, Vector3.Up);
            }
        }
    }
}
