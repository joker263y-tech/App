using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using UnityEngine;

namespace Shadowbound.Game.Player
{
    /// <summary>
    /// Third-person follow camera.
    ///
    /// The camera is driven by yaw and pitch that live on the input driver rather
    /// than on the camera itself, because movement is camera-relative: the input
    /// layer needs to know which way the camera is facing in order to decide what
    /// "forward" means. Keeping the angles in one place stops the two from
    /// disagreeing and sending the player in an unintended direction.
    ///
    /// The camera also pushes in when something is between it and the player, and
    /// recentres its eye height on a smoothed target, so the view does not judder
    /// against the step of a fixed-timestep simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        public Transform Target;
        public PlayerInputDriver Driver;
        public Camera Camera;

        public float Distance = 6.5f;
        public float MinDistance = 2f;
        public float Height = 1.6f;
        public float FollowDamping = 12f;
        public float CollisionRadius = 0.3f;

        /// <summary>Layers the camera should not see through. Empty disables collision.</summary>
        public LayerMask ObstructionMask;

        private Vector3 _smoothedTarget;
        private float _currentDistance;

        private void Awake()
        {
            if (Camera == null)
            {
                Camera = GetComponent<Camera>();
            }

            _currentDistance = Distance;

            if (Target != null)
            {
                _smoothedTarget = Target.position;
            }
        }

        /// <summary>Points the rig at a new target, e.g. after a respawn or region change.</summary>
        public void SetTarget(Transform target, Combatant combatant)
        {
            Target = target;

            if (target != null)
            {
                _smoothedTarget = target.position;
            }

            LastTarget = combatant;
        }

        /// <summary>The core combatant being followed, if any. Used for aim and lock-on.</summary>
        public Combatant LastTarget { get; private set; }

        /// <summary>Adds an impulse to the camera, for hit feedback.</summary>
        public void Shake(float amount)
        {
            _shake = Mathf.Max(_shake, Mathf.Clamp01(amount));
        }

        private float _shake;

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            Vector3 desiredTarget = Target.position + (Vector3.up * Height);
            _smoothedTarget = Vector3.Lerp(
                _smoothedTarget,
                desiredTarget,
                1f - Mathf.Exp(-FollowDamping * deltaTime));

            float yaw = Driver != null ? Driver.Yaw : 0f;
            float pitch = Driver != null ? Driver.Pitch : 12f;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.back;

            float targetDistance = Distance;

            if (ObstructionMask.value != 0)
            {
                if (Physics.SphereCast(
                        _smoothedTarget,
                        CollisionRadius,
                        direction,
                        out RaycastHit hit,
                        Distance,
                        ObstructionMask,
                        QueryTriggerInteraction.Ignore))
                {
                    targetDistance = Mathf.Max(MinDistance, hit.distance);
                }
            }

            // Pull in instantly when something intrudes, ease back out afterwards.
            _currentDistance = targetDistance < _currentDistance
                ? targetDistance
                : Mathf.Lerp(_currentDistance, targetDistance, 1f - Mathf.Exp(-6f * deltaTime));

            Vector3 position = _smoothedTarget + (direction * _currentDistance);

            if (_shake > 0f)
            {
                float magnitude = _shake * 0.12f;
                position += new Vector3(
                    Random.Range(-magnitude, magnitude),
                    Random.Range(-magnitude, magnitude),
                    0f);

                _shake = Mathf.Max(0f, _shake - (deltaTime * 4f));
            }

            transform.position = position;
            transform.rotation = Quaternion.LookRotation(_smoothedTarget - position, Vector3.up);
        }
    }
}
