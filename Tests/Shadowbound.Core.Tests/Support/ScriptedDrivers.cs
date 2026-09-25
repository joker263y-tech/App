using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Simulation;

namespace Shadowbound.Core.Tests.Support
{
    /// <summary>
    /// A driver that closes on the nearest hostile and swings when in range.
    ///
    /// Used in place of a human, so the full runtime path - decision, movement,
    /// ability activation, windup, hit resolution, death, rewards - can be run end
    /// to end in a test with no engine and no frame timing.
    /// </summary>
    internal sealed class AggressiveMeleeDriver : ICombatantDriver
    {
        private readonly int _abilityIndex;

        public AggressiveMeleeDriver(int abilityIndex = 0, float engageRange = 2.2f)
        {
            _abilityIndex = abilityIndex;
            EngageRange = engageRange;
        }

        public float EngageRange { get; set; }

        /// <summary>How many times this driver asked to attack. Diagnostics only.</summary>
        public int AttackRequests { get; private set; }

        public CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world)
        {
            CombatIntent intent = CombatIntent.None();

            Combatant target = world.FindNearestHostile(self, float.MaxValue);
            if (target == null)
            {
                return intent;
            }

            intent.LookAtTarget = true;

            float distance = Float3.DistanceXZ(self.Position, target.Position);

            // Close the gap, but stop short so the swing can actually connect.
            if (distance > EngageRange * 0.8f)
            {
                intent.MoveDirection = (target.Position - self.Position).FlattenedXZ;
                intent.SpeedScale = 1f;
            }

            Participant participant = world.FindParticipant(self);

            if (distance <= EngageRange && participant != null && participant.Abilities.IsReady(_abilityIndex))
            {
                intent.ActivateAbility = true;
                intent.AbilityIndex = _abilityIndex;
                AttackRequests++;
            }

            return intent;
        }
    }

    /// <summary>A driver that does nothing. Used for a target dummy.</summary>
    internal sealed class PassiveDriver : ICombatantDriver
    {
        public CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world)
        {
            return CombatIntent.None();
        }
    }

    /// <summary>A driver that walks in a fixed direction until told otherwise.</summary>
    internal sealed class WanderDriver : ICombatantDriver
    {
        public Float3 Direction = new Float3(1f, 0f, 0f);
        public float SpeedScale = 1f;

        public CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world)
        {
            CombatIntent intent = CombatIntent.None();
            intent.MoveDirection = Direction;
            intent.SpeedScale = SpeedScale;
            return intent;
        }
    }

    /// <summary>Visibility that is never clear, for testing enemies that cannot see.</summary>
    internal sealed class BlindSight : IOcclusionProvider
    {
        public bool HasLineOfSight(Float3 from, Float3 to)
        {
            return false;
        }
    }
}
