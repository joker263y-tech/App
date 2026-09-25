using System.Collections.Generic;
using Shadowbound.Core.Ai;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.Tests.Support;
using Xunit;

namespace Shadowbound.Core.Tests.Simulation
{
    public class EncounterSimulationTests
    {
        private static readonly Float3 Forward = new Float3(0f, 0f, 1f);

        private static AbilityDefinition Melee(
            float windup = 0.25f,
            float recovery = 0.3f,
            float cooldown = 1f,
            float range = 2.5f,
            float damageMultiplier = 1f)
        {
            return new AbilityDefinition
            {
                Id = "swing",
                Kind = AbilityKind.Melee,
                DamageType = DamageType.Physical,
                WindupSeconds = windup,
                RecoverySeconds = recovery,
                CooldownSeconds = cooldown,
                Range = range,
                ConeHalfAngleDegrees = 120f,
                DamageMultiplier = damageMultiplier,
                Variance = 0f,
                MaxTargets = 1
            };
        }

        private static EnemyBrainSettings BrainSettings(
            float attackRange = 2.6f,
            float reactionTime = 0.2f)
        {
            return new EnemyBrainSettings
            {
                ViewDistance = 20f,
                ViewHalfAngleDegrees = 90f,
                ProximityRadius = 3f,
                AttackRange = attackRange,
                PreferredRange = 2f,
                ReactionTime = reactionTime,
                AttackCommitment = 0.7f,
                IdleDuration = 100f
            };
        }

        private static EncounterSimulation MakeSim(
            ulong seed = 12345,
            WorldBounds bounds = default(WorldBounds),
            IOcclusionProvider occlusion = null)
        {
            if (bounds.Size == Float3.Zero)
            {
                bounds = WorldBounds.Square(50f);
            }

            return new EncounterSimulation(new DeterministicRng(seed), bounds, occlusion);
        }

        // ------------------------------- registration ------------------------------

        [Fact]
        public void AddingParticipants_RegistersThemForTargeting()
        {
            EncounterSimulation sim = MakeSim();
            Combatant player = CombatantFactory.Create("hero", Faction.Player);
            Combatant enemy = CombatantFactory.Create("foe", Faction.Hostile);

            sim.AddDriven(player, new[] { Melee() }, new PassiveDriver(), isPlayer: true);
            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            Assert.Equal(2, sim.Participants.Count);
            Assert.Equal(1, sim.HostilesRemaining);
            Assert.NotNull(sim.Find("hero"));
            Assert.Same(enemy, sim.FindNearestHostile(player, float.MaxValue));
        }

        [Fact]
        public void AddEnemy_RequiresACombatant()
        {
            EncounterSimulation sim = MakeSim();

            Assert.Throws<System.ArgumentNullException>(
                () => sim.AddEnemy(null, new[] { Melee() }, BrainSettings()));
        }

        // --------------------------------- combat ----------------------------------

        [Fact]
        public void AnEnemyClosesOnThePlayerAndDrawsBlood()
        {
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create(
                "hero", Faction.Player, maxHealth: 1000f, attackPower: 10f,
                position: new Float3(0f, 0f, 10f));

            // The enemy starts at the origin facing +Z, so the player is in view.
            Combatant enemy = CombatantFactory.Create(
                "walker", Faction.Hostile, maxHealth: 500f, attackPower: 25f,
                moveSpeed: 5f);

            sim.AddDriven(player, new[] { Melee() }, new PassiveDriver(), isPlayer: true);
            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            sim.Advance(12f);

            Assert.True(player.Vitals.Health < 1000f, "The enemy should have landed hits.");
            Assert.True(enemy.Position.Z > 1f, "The enemy should have closed the distance.");
        }

        [Fact]
        public void AnEnemyBeyondTheArenaBoundsCannotReachIn()
        {
            // With no line of sight and no proximity, the enemy never notices.
            EncounterSimulation sim = MakeSim(occlusion: new BlindSight());

            Combatant player = CombatantFactory.Create(
                "hero", Faction.Player, maxHealth: 1000f, position: new Float3(0f, 0f, 40f));
            Combatant enemy = CombatantFactory.Create("walker", Faction.Hostile, maxHealth: 500f, moveSpeed: 5f);

            sim.AddDriven(player, new[] { Melee() }, new PassiveDriver(), isPlayer: true);
            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            sim.Advance(20f);

            Assert.Equal(1000f, player.Vitals.Health, 2);
        }

        [Fact]
        public void APlayerDriverKillsAnEnemyWithinRange()
        {
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create(
                "hero", Faction.Player, maxHealth: 500f, attackPower: 500f);
            player.FaceImmediately(Forward);

            Combatant enemy = CombatantFactory.Create(
                "walker", Faction.Hostile, maxHealth: 100f,
                position: new Float3(0f, 0f, 1.5f));

            sim.AddDriven(player, new[] { Melee() }, new AggressiveMeleeDriver(), isPlayer: true);
            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            sim.Advance(3f);

            Assert.False(enemy.IsAlive);
            Assert.Equal(0, sim.HostilesRemaining);
        }

        [Fact]
        public void DeathRaisesTheHostileCountDownAndFiresOnce()
        {
            EncounterSimulation sim = MakeSim();
            var deaths = new List<string>();
            sim.Died += (victim, _) => deaths.Add(victim.Id);

            Combatant player = CombatantFactory.Create("hero", Faction.Player, attackPower: 900f);
            player.FaceImmediately(Forward);
            Combatant enemy = CombatantFactory.Create(
                "walker", Faction.Hostile, maxHealth: 50f, position: new Float3(0f, 0f, 1f));

            sim.AddDriven(player, new[] { Melee() }, new AggressiveMeleeDriver(), isPlayer: true);
            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            sim.Advance(5f);

            Assert.Equal(new[] { "walker" }, deaths);
            Assert.Equal(0, sim.HostilesRemaining);
        }

        [Fact]
        public void DamageEvents_AttributeTheBlowToTheAttacker()
        {
            EncounterSimulation sim = MakeSim();
            Combatant reportedAttacker = null;
            sim.DamageDealt += (attacker, _, _) => reportedAttacker = attacker?.Combatant;

            Combatant player = CombatantFactory.Create("hero", Faction.Player, attackPower: 20f);
            player.FaceImmediately(Forward);
            Combatant enemy = CombatantFactory.Create(
                "walker", Faction.Hostile, maxHealth: 500f, position: new Float3(0f, 0f, 1.5f));

            sim.AddDriven(player, new[] { Melee() }, new AggressiveMeleeDriver(), isPlayer: true);
            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            sim.Advance(2f);

            Assert.Same(player, reportedAttacker);
        }

        [Fact]
        public void ADeadCombatantStopsActing()
        {
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create("hero", Faction.Player, attackPower: 900f);
            player.FaceImmediately(Forward);
            Combatant enemy = CombatantFactory.Create(
                "walker", Faction.Hostile, maxHealth: 50f, moveSpeed: 5f, position: new Float3(0f, 0f, 1f));

            sim.AddDriven(player, new[] { Melee() }, new AggressiveMeleeDriver(), isPlayer: true);
            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            sim.Advance(2f);
            Assert.False(enemy.IsAlive);

            Float3 deathPosition = enemy.Position;
            sim.Advance(3f);

            Assert.Equal(deathPosition, enemy.Position);
        }

        // -------------------------------- interruption -----------------------------

        [Fact]
        public void StaggeringACasterMidWindupCancelsTheBlow()
        {
            // Without this, staggering would only delay a blow that still landed,
            // and interrupts would have no purpose.
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create("hero", Faction.Player, maxHealth: 500f, attackPower: 50f);
            player.FaceImmediately(Forward);
            Combatant enemy = CombatantFactory.Create(
                "walker", Faction.Hostile, maxHealth: 500f, position: new Float3(0f, 0f, 1.5f));

            Participant playerParticipant = sim.AddDriven(
                player, new[] { Melee(windup: 1f, recovery: 0.5f) }, new AggressiveMeleeDriver());

            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            // Get the player into its windup.
            sim.Advance(0.3f);
            Assert.Equal(CastPhase.Windup, playerParticipant.Abilities.Phase);

            player.Statuses.Apply(StatusEffect.Modifier(StatusKind.Staggered, 1f, 0.5f, null));
            sim.Step();

            Assert.Equal(CastPhase.Ready, playerParticipant.Abilities.Phase);
            Assert.Equal(500f, enemy.Vitals.Health, 2);
        }

        [Fact]
        public void AStunnedCombatantDoesNotMove()
        {
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create("hero", Faction.Player, moveSpeed: 8f);
            Participant participant = sim.AddDriven(player, new[] { Melee() }, new WanderDriver());

            sim.Advance(0.5f);
            float movedDistance = player.Position.Magnitude;
            Assert.True(movedDistance > 0.5f);

            player.Statuses.Apply(StatusEffect.Modifier(StatusKind.Staggered, 1f, 2f, null));
            Float3 frozen = player.Position;
            sim.Advance(0.5f);

            Assert.Equal(frozen, player.Position);
            Assert.NotNull(participant);
        }

        // ---------------------------------- bounds ---------------------------------

        [Fact]
        public void MovementIsClampedToTheArena()
        {
            EncounterSimulation sim = MakeSim(bounds: WorldBounds.Square(5f));

            Combatant player = CombatantFactory.Create("hero", Faction.Player, moveSpeed: 20f);
            sim.AddDriven(player, new[] { Melee() }, new WanderDriver { Direction = new Float3(1f, 0f, 0f) });

            sim.Advance(5f);

            Assert.Equal(5f, player.Position.X, 3);
        }

        [Fact]
        public void KnockbackIsAppliedAndDecays()
        {
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create("hero", Faction.Player);
            sim.AddDriven(player, new[] { Melee() }, new PassiveDriver());

            player.ApplyKnockback(new Float3(0f, 0f, 1f), 10f);
            Assert.Equal(10f, player.KnockbackSpeed, 3);

            sim.Advance(2f);

            Assert.Equal(0f, player.KnockbackSpeed, 3);
            Assert.True(player.Position.Z > 0f);
        }

        // --------------------------------- stealth ---------------------------------

        [Fact]
        public void AnEnemyIsUnaffectedByATargetItCannotSeeOrFeel()
        {
            EncounterSimulation sim = MakeSim(occlusion: new BlindSight());

            Combatant player = CombatantFactory.Create(
                "hero", Faction.Player, maxHealth: 500f, position: new Float3(0f, 0f, 12f));
            Combatant enemy = CombatantFactory.Create("walker", Faction.Hostile, maxHealth: 500f, moveSpeed: 5f);

            sim.AddDriven(player, new[] { Melee() }, new PassiveDriver(), isPlayer: true);
            Participant enemyParticipant = sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            sim.Advance(5f);

            Assert.False(enemyParticipant.Brain.IsAlerted);
        }

        [Fact]
        public void HurtingOneEnemyAlertsItsNearbyAllies()
        {
            // Otherwise a group can be picked off one at a time from the edge of
            // the fight while the rest stand and watch.
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create(
                "hero", Faction.Player, position: new Float3(0f, 0f, 400f));

            Combatant first = CombatantFactory.Create("first", Faction.Hostile, maxHealth: 500f);
            Combatant second = CombatantFactory.Create(
                "second", Faction.Hostile, maxHealth: 500f, position: new Float3(0f, 0f, 5f));

            sim.AddDriven(player, new[] { Melee() }, new PassiveDriver(), isPlayer: true);
            Participant firstParticipant = sim.AddEnemy(first, new[] { Melee() }, BrainSettings());
            Participant secondParticipant = sim.AddEnemy(second, new[] { Melee() }, BrainSettings());

            Assert.False(secondParticipant.Brain.IsAlerted);

            first.Vitals.ApplyDamage(10f, player);
            sim.Step();

            Assert.True(
                secondParticipant.Brain.IsAlerted,
                "An ally within the propagation radius should be alerted by a hurt comrade.");
            Assert.NotNull(firstParticipant);
        }

        // ------------------------------- determinism -------------------------------

        [Fact]
        public void TheSameSeedProducesTheSameEncounter()
        {
            float[] outcomes = new float[2];

            for (int run = 0; run < 2; run++)
            {
                EncounterSimulation sim = MakeSim(seed: 424242);

                Combatant player = CombatantFactory.Create(
                    "hero", Faction.Player, maxHealth: 800f, attackPower: 30f, critChance: 0.3f);
                player.FaceImmediately(Forward);

                Combatant enemy = CombatantFactory.Create(
                    "walker", Faction.Hostile, maxHealth: 600f, attackPower: 20f,
                    critChance: 0.2f, position: new Float3(0f, 0f, 4f));

                sim.AddDriven(player, new[] { Melee(cooldown: 0.8f) }, new AggressiveMeleeDriver(), isPlayer: true);
                sim.AddEnemy(enemy, new[] { Melee(cooldown: 1.2f) }, BrainSettings());

                sim.Advance(20f);
                outcomes[run] = player.Vitals.Health + (enemy.Vitals.Health * 1000f);
            }

            Assert.Equal(outcomes[0], outcomes[1], 3);
        }

        [Fact]
        public void DifferentSeedsProduceDifferentEncounters()
        {
            float[] outcomes = new float[2];
            ulong[] seeds = { 1UL, 999999UL };

            for (int run = 0; run < 2; run++)
            {
                EncounterSimulation sim = MakeSim(seed: seeds[run]);

                Combatant player = CombatantFactory.Create(
                    "hero", Faction.Player, maxHealth: 800f, attackPower: 30f, critChance: 0.5f);
                player.FaceImmediately(Forward);

                Combatant enemy = CombatantFactory.Create(
                    "walker", Faction.Hostile, maxHealth: 600f, attackPower: 20f,
                    critChance: 0.5f, position: new Float3(0f, 0f, 4f));

                sim.AddDriven(player, new[] { Melee(cooldown: 0.8f) }, new AggressiveMeleeDriver(), isPlayer: true);
                sim.AddEnemy(enemy, new[] { Melee(cooldown: 1.2f) }, BrainSettings());

                sim.Advance(20f);
                outcomes[run] = player.Vitals.Health;
            }

            Assert.NotEqual(outcomes[0], outcomes[1]);
        }

        // --------------------------------- stepping --------------------------------

        [Fact]
        public void Update_RunsFixedStepsToMatchElapsedTime()
        {
            EncounterSimulation sim = MakeSim();
            sim.FixedDeltaTime = 1f / 60f;

            int steps = sim.Update(1f / 60f);

            Assert.Equal(1, steps);
            Assert.Equal(1f / 60f, sim.Time, 5);
        }

        [Fact]
        public void Update_DropsTheBacklogRatherThanSpiralling()
        {
            // A long hitch must not queue up minutes of simulation.
            EncounterSimulation sim = MakeSim();
            sim.FixedDeltaTime = 1f / 60f;
            sim.MaxStepsPerUpdate = 4;

            int steps = sim.Update(100f);

            Assert.Equal(4, steps);
        }

        [Fact]
        public void Update_WithNoElapsedTime_DoesNothing()
        {
            EncounterSimulation sim = MakeSim();

            Assert.Equal(0, sim.Update(0f));
            Assert.Equal(0, sim.Update(-1f));
            Assert.Equal(0, sim.StepCount);
        }

        // ---------------------------------- reset ---------------------------------

        [Fact]
        public void Reset_RevivesEveryoneIncludingTheDead()
        {
            // Only restoring survivors would leave a wiped boss attempt
            // permanently unresettable.
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create("hero", Faction.Player, attackPower: 900f);
            player.FaceImmediately(Forward);
            Combatant enemy = CombatantFactory.Create(
                "walker", Faction.Hostile, maxHealth: 50f, position: new Float3(0f, 0f, 1f));

            sim.AddDriven(player, new[] { Melee() }, new AggressiveMeleeDriver(), isPlayer: true);
            sim.AddEnemy(enemy, new[] { Melee() }, BrainSettings());

            sim.Advance(3f);
            Assert.False(enemy.IsAlive);

            sim.Reset();

            Assert.True(enemy.IsAlive);
            Assert.Equal(50f, enemy.Vitals.Health, 2);
            Assert.Equal(1, sim.HostilesRemaining);

            // Time and step count describe the simulation object's lifetime rather
            // than one attempt, so a reset deliberately leaves them running.
            Assert.True(sim.StepCount > 0);
        }

        [Fact]
        public void Reset_ClearsStatusEffectsAndCooldowns()
        {
            EncounterSimulation sim = MakeSim();

            Combatant player = CombatantFactory.Create("hero", Faction.Player);
            Participant participant = sim.AddDriven(player, new[] { Melee() }, new PassiveDriver());

            participant.Abilities.TryActivate(0, out _);
            player.Statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.5f, 10f, null));

            sim.Reset();

            Assert.Equal(0, player.Statuses.ActiveCount);
            Assert.Equal(0f, participant.Abilities.CooldownRemaining(0), 3);
            Assert.False(participant.Abilities.IsBusy);
        }

        [Fact]
        public void RestoreRng_ContinuesTheSequenceFromTheGivenState()
        {
            EncounterSimulation sim = MakeSim(seed: 7);
            ulong state = sim.Rng.State;
            ulong increment = sim.Rng.Increment;

            uint fromOriginal = sim.Rng.NextUInt();

            sim.RestoreRng(state, increment);
            uint fromRestored = sim.Rng.NextUInt();

            Assert.Equal(fromOriginal, fromRestored);
            Assert.Equal(increment, sim.Rng.Increment);
        }
    }
}
