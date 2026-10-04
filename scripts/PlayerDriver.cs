using Godot;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Simulation;

namespace Shadowbound.Game
{
    /// <summary>
    /// Camera-relative movement input, ported from the project's earlier player
    /// drivers.
    ///
    /// This is the only place that knows a human is playing. The core simulation
    /// cannot tell this apart from the enemy brain, which is why the player and
    /// the enemies move through exactly the same code path.
    ///
    /// Movement is camera-relative: pushing forward moves the Warden away from the
    /// camera, not along a fixed world axis. Facing is left to the core, which
    /// turns the combatant toward its movement direction.
    /// </summary>
    public sealed class PlayerDriver : ICombatantDriver
    {
        private readonly PlayerInputReader _input;

        public PlayerDriver(PlayerInputReader input)
        {
            _input = input;
        }

        public CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world)
        {
            CombatIntent intent = CombatIntent.None();

            Vector2 axis = _input.ReadMoveAxis();

            if (axis.LengthSquared() > 0.0004f)
            {
                if (axis.LengthSquared() > 1f)
                {
                    axis = axis.Normalized();
                }

                // The camera yaw IS the core's facing convention, so forward for a
                // yaw of Y degrees is exactly the core's DegreesToDirection(Y).
                Vector3 forward = CoordinateConvert.FlatDirection(_input.Yaw);
                var right = new Vector3(forward.Z, 0f, -forward.X);

                Vector3 worldDirection = (forward * axis.Y) + (right * axis.X);
                Float3 flat = new Float3(worldDirection.X, 0f, worldDirection.Z).FlattenedXZ;

                if (flat != Float3.Zero)
                {
                    intent.MoveDirection = flat;
                    intent.SpeedScale = 1f;
                }
            }

            if (_input.ConsumeAbility(out int ability))
            {
                intent.ActivateAbility = true;
                intent.AbilityIndex = ability;
            }

            return intent;
        }
    }
}
