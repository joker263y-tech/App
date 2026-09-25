using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Stats;
using Shadowbound.Core.Tests.Support;
using Xunit;

namespace Shadowbound.Core.Tests.Combat
{
    public class AbilityControllerTests
    {
        private static AbilityController MakeController(
            out Combatant combatant,
            AbilityDefinition ability,
            float cooldownRate = 1f,
            float maxStamina = 100f)
        {
            combatant = CombatantFactory.Create(
                "tester",
                Faction.Player,
                maxStamina: maxStamina,
                cooldownRate: cooldownRate);

            return new AbilityController(combatant, new List<AbilityDefinition> { ability });
        }

        /// <summary>Ticks in small steps so cast phase boundaries are crossed realistically.</summary>
        private static int TickFor(AbilityController controller, float seconds, float step = 0.01f)
        {
            int landings = 0;
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                if (controller.Tick(step) >= 0)
                {
                    landings++;
                }

                elapsed += step;
            }

            return landings;
        }

        [Fact]
        public void TryActivate_SpendsStaminaUpFront()
        {
            AbilityController controller = MakeController(out Combatant combatant, CombatantFactory.Strike(staminaCost: 20f));

            bool activated = controller.TryActivate(0, out AbilityFailure failure);

            Assert.True(activated);
            Assert.Equal(AbilityFailure.None, failure);
            Assert.Equal(80f, combatant.Vitals.Stamina, 3);
        }

        [Fact]
        public void TryActivate_WithoutEnoughStamina_IsRefusedAndSpendsNothing()
        {
            AbilityController controller = MakeController(out Combatant combatant, CombatantFactory.Strike(staminaCost: 500f));

            bool activated = controller.TryActivate(0, out AbilityFailure failure);

            Assert.False(activated);
            Assert.Equal(AbilityFailure.NotEnoughStamina, failure);
            Assert.Equal(100f, combatant.Vitals.Stamina, 3);
            Assert.Equal(CastPhase.Ready, controller.Phase);
        }

        [Fact]
        public void TryActivate_WhileAlreadyCasting_IsRefused()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike());

            controller.TryActivate(0, out _);
            bool second = controller.TryActivate(0, out AbilityFailure failure);

            Assert.False(second);
            Assert.Equal(AbilityFailure.Busy, failure);
        }

        [Fact]
        public void TryActivate_WithUnknownIndex_ReportsUnknownAbility()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike());

            Assert.False(controller.TryActivate(9, out AbilityFailure failure));
            Assert.Equal(AbilityFailure.UnknownAbility, failure);
        }

        [Fact]
        public void TryActivate_WhileStunned_IsRefused()
        {
            AbilityController controller = MakeController(out Combatant combatant, CombatantFactory.Strike());
            combatant.Statuses.Apply(StatusEffect.Modifier(StatusKind.Staggered, 1f, 5f, null));

            Assert.False(controller.TryActivate(0, out AbilityFailure failure));
            Assert.Equal(AbilityFailure.Stunned, failure);
        }

        [Fact]
        public void TryActivate_WhenDead_IsRefused()
        {
            AbilityController controller = MakeController(out Combatant combatant, CombatantFactory.Strike());
            combatant.Vitals.ApplyDamage(1000f, null);

            Assert.False(controller.TryActivate(0, out AbilityFailure failure));
            Assert.Equal(AbilityFailure.Dead, failure);
        }

        [Fact]
        public void Windup_LandsTheBlowExactlyOnce()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.25f, recovery: 0.3f));
            controller.TryActivate(0, out _);

            // 0.4 seconds is past the windup but still inside recovery.
            int landings = TickFor(controller, 0.4f);

            Assert.Equal(1, landings);
            Assert.Equal(CastPhase.Recovery, controller.Phase);
        }

        [Fact]
        public void ZeroWindupAbility_LandsOnTheFirstTick()
        {
            // An earlier implementation landed zero-windup abilities during
            // activation, where Tick then erased the flag before it could be
            // read, so the ability dealt no damage at all.
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0f, recovery: 0.3f));

            controller.TryActivate(0, out _);
            int landed = controller.Tick(0.016f);

            Assert.Equal(0, landed);
        }

        [Fact]
        public void Cast_ReturnsToReadyAfterRecovery()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.2f, recovery: 0.2f));
            controller.TryActivate(0, out _);

            TickFor(controller, 0.5f);

            Assert.Equal(CastPhase.Ready, controller.Phase);
            Assert.False(controller.IsBusy);
            Assert.Equal(-1, controller.CastingIndex);
        }

        [Fact]
        public void Cooldown_PreventsImmediateReactivation()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.1f, recovery: 0.1f, cooldown: 2f));
            controller.TryActivate(0, out _);

            // Finish the cast, then try again while the cooldown is running.
            TickFor(controller, 0.3f);

            Assert.Equal(CastPhase.Ready, controller.Phase);
            Assert.False(controller.TryActivate(0, out AbilityFailure failure));
            Assert.Equal(AbilityFailure.OnCooldown, failure);
            Assert.True(controller.CooldownRemaining(0) > 0f);
        }

        [Fact]
        public void Cooldown_ExpiresAndAllowsReuse()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.1f, recovery: 0.1f, cooldown: 1f));
            controller.TryActivate(0, out _);

            // Effective cooldown is cooldown + windup + recovery = 1.2s.
            TickFor(controller, 1.4f);

            Assert.Equal(0f, controller.CooldownRemaining(0), 3);
            Assert.True(controller.TryActivate(0, out _));
        }

        [Fact]
        public void HasteStat_SpeedsCooldownRecovery()
        {
            AbilityController fast = MakeController(
                out _,
                CombatantFactory.Strike(windup: 0.1f, recovery: 0.1f, cooldown: 1f),
                cooldownRate: 2f);

            AbilityController normal = MakeController(
                out _,
                CombatantFactory.Strike(windup: 0.1f, recovery: 0.1f, cooldown: 1f),
                cooldownRate: 1f);

            fast.TryActivate(0, out _);
            normal.TryActivate(0, out _);

            TickFor(fast, 0.5f);
            TickFor(normal, 0.5f);

            Assert.True(
                fast.CooldownRemaining(0) < normal.CooldownRemaining(0),
                "A hasted caster should have less cooldown remaining.");
        }

        [Fact]
        public void Chilled_SlowsCooldownRecovery()
        {
            AbilityController controller = MakeController(out Combatant combatant, CombatantFactory.Strike(windup: 0.1f, recovery: 0.1f, cooldown: 2f));
            controller.TryActivate(0, out _);
            TickFor(controller, 0.3f);

            float before = controller.CooldownRemaining(0);

            combatant.Statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.5f, 10f, null));
            TickFor(controller, 0.5f);
            float chilledRemaining = controller.CooldownRemaining(0);

            Assert.True(chilledRemaining > 0f);
            // One further second of chilled recovery must remove less than a
            // second of cooldown.
            Assert.True(before - chilledRemaining < 0.5f);
        }

        [Fact]
        public void CooldownFraction_ReportsProgress()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.1f, recovery: 0.1f, cooldown: 1f));

            controller.TryActivate(0, out _);
            TickFor(controller, 0.3f);

            Assert.InRange(controller.CooldownFraction(0), 0.01f, 1f);

            TickFor(controller, 2f);

            Assert.Equal(0f, controller.CooldownFraction(0), 3);
        }

        [Fact]
        public void Interrupt_CancelsTheCastWithoutRefundingCost()
        {
            AbilityController controller = MakeController(out Combatant combatant, CombatantFactory.Strike(windup: 0.5f, staminaCost: 30f));
            controller.TryActivate(0, out _);
            TickFor(controller, 0.1f);

            bool interrupted = controller.Interrupt();

            Assert.True(interrupted);
            Assert.Equal(CastPhase.Ready, controller.Phase);
            Assert.Equal(70f, combatant.Vitals.Stamina, 3);
            // The cooldown must not be refunded either, or interrupting would be
            // a free reset rather than a real cost.
            Assert.True(controller.CooldownRemaining(0) > 0f);
        }

        [Fact]
        public void InterruptedCast_DoesNotLandItsBlow()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.5f));
            controller.TryActivate(0, out _);
            TickFor(controller, 0.1f);

            controller.Interrupt();
            int landings = TickFor(controller, 1f);

            Assert.Equal(0, landings);
        }

        [Fact]
        public void Interrupt_DuringRecovery_ReportsNothingToInterrupt()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.1f, recovery: 0.5f));
            controller.TryActivate(0, out _);
            TickFor(controller, 0.15f);

            Assert.Equal(CastPhase.Recovery, controller.Phase);
            Assert.False(controller.Interrupt());
        }

        [Fact]
        public void MoveSpeedMultiplier_IsReducedDuringWindupOnly()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.4f, recovery: 0.4f));

            Assert.Equal(1f, controller.MoveSpeedMultiplier, 3);

            controller.TryActivate(0, out _);
            Assert.Equal(0.25f, controller.MoveSpeedMultiplier, 3);

            TickFor(controller, 0.5f);
            Assert.Equal(CastPhase.Recovery, controller.Phase);
            Assert.Equal(1f, controller.MoveSpeedMultiplier, 3);
        }

        [Fact]
        public void ResetCooldowns_ClearsEverything()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.1f, recovery: 0.1f, cooldown: 5f));
            controller.TryActivate(0, out _);

            controller.ResetCooldowns();

            Assert.Equal(CastPhase.Ready, controller.Phase);
            Assert.Equal(0f, controller.CooldownRemaining(0), 3);
            Assert.True(controller.IsReady(0));
        }

        [Fact]
        public void IsReady_IsFalseWhileBusy()
        {
            AbilityController controller = MakeController(out _, CombatantFactory.Strike());
            controller.TryActivate(0, out _);

            Assert.False(controller.IsReady(0));
        }

        [Fact]
        public void LongFrame_CarriesOvershootIntoRecovery()
        {
            // A hitch must not shorten the caster's commitment to the ability.
            AbilityController controller = MakeController(out _, CombatantFactory.Strike(windup: 0.2f, recovery: 0.5f));
            controller.TryActivate(0, out _);

            int landed = controller.Tick(0.5f);

            Assert.Equal(0, landed);
            Assert.Equal(CastPhase.Recovery, controller.Phase);
            // 0.2 of windup consumed, 0.3 of overshoot leaves 0.2 of recovery.
            Assert.Equal(0.2f, controller.PhaseRemaining, 3);
        }
    }
}
