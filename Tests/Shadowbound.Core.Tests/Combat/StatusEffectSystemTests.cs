using Shadowbound.Core.Combat;
using Shadowbound.Core.Stats;
using Xunit;

namespace Shadowbound.Core.Tests.Combat
{
    public class StatusEffectSystemTests
    {
        private static StatusEffectSystem MakeSystem(
            out Vitals vitals,
            float maxHealth = 1000f,
            float statusResistance = 0f)
        {
            var stats = new StatSet();
            stats.SetBase(StatId.MaxHealth, maxHealth);
            stats.SetBase(StatId.MaxStamina, 100f);
            stats.SetBase(StatId.StatusResistance, statusResistance);

            vitals = new Vitals(stats, new ResistanceSet());
            vitals.ResetToFull();

            return new StatusEffectSystem(stats, vitals);
        }

        // ---------------------------- damage over time ----------------------------

        [Fact]
        public void Bleeding_TicksDamageOnItsInterval()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Dot(StatusKind.Bleeding, DamageType.Physical, 5f, 5f, 1f, null, 1));
            statuses.Tick(1f);

            Assert.Equal(995f, vitals.Health, 3);
        }

        [Fact]
        public void DamageOverTime_StacksSumTheirDamage()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Dot(StatusKind.Bleeding, DamageType.Physical, 5f, 5f, 1f, null, 3));
            statuses.Apply(StatusEffect.Dot(StatusKind.Bleeding, DamageType.Physical, 5f, 5f, 1f, null, 3));

            Assert.Equal(2, statuses.StackCount(StatusKind.Bleeding));
            Assert.Equal(10f, statuses.Magnitude(StatusKind.Bleeding), 3);

            statuses.Tick(1f);

            Assert.Equal(990f, vitals.Health, 3);
        }

        [Fact]
        public void DamageOverTime_StopsAddingStacksAtItsCap()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals, 10000f);

            for (int i = 0; i < 10; i++)
            {
                statuses.Apply(StatusEffect.Dot(StatusKind.Bleeding, DamageType.Physical, 5f, 5f, 1f, null, 2));
            }

            Assert.Equal(2, statuses.StackCount(StatusKind.Bleeding));
        }

        [Fact]
        public void DamageOverTime_ExpiresAfterItsDuration()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Dot(StatusKind.Burning, DamageType.Ember, 5f, 2f, 1f, null, 1));

            statuses.Tick(1f);
            statuses.Tick(1f);

            Assert.Equal(0, statuses.ActiveCount);
            Assert.Equal(990f, vitals.Health, 3);

            // Further ticks must not deal damage.
            statuses.Tick(5f);
            Assert.Equal(990f, vitals.Health, 3);
        }

        [Fact]
        public void DamageOverTime_CatchesUpAfterALongFrame()
        {
            // A hitch must not silently swallow ticks.
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Dot(StatusKind.Burning, DamageType.Ember, 10f, 10f, 1f, null, 1));
            statuses.Tick(3f);

            Assert.Equal(970f, vitals.Health, 3);
        }

        [Fact]
        public void DamageOverTime_CannotKillBeyondZeroHealth()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals, 12f);

            statuses.Apply(StatusEffect.Dot(StatusKind.Burning, DamageType.Ember, 100f, 10f, 1f, null, 1));
            statuses.Tick(5f);

            Assert.Equal(0f, vitals.Health);
            Assert.False(vitals.IsAlive);
        }

        [Fact]
        public void DamageOverTime_IsAttributedToItsSource()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);
            var caster = new object();
            object observed = null;
            vitals.Damaged += (amount, source) => observed = source;

            statuses.Apply(StatusEffect.Dot(StatusKind.Bleeding, DamageType.Physical, 5f, 5f, 1f, caster, 1));
            statuses.Tick(1f);

            Assert.Same(caster, observed);
        }

        // ------------------------------ modifier effects --------------------------

        [Fact]
        public void ModifierEffects_TakeTheStrongestInstanceRatherThanSumming()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.2f, 5f, null));
            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.3f, 5f, null));

            // Summing would give 0.5 and let groups of enemies freeze the player.
            Assert.Equal(0.3f, statuses.Magnitude(StatusKind.Chilled), 3);
            Assert.Equal(1, statuses.StackCount(StatusKind.Chilled));
        }

        [Fact]
        public void Chilled_ReducesMovementAndCooldownRecovery()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.4f, 5f, null));

            Assert.Equal(0.6f, statuses.MoveSpeedMultiplier, 3);
            // Cooldown is only half as sensitive as movement.
            Assert.Equal(0.8f, statuses.CooldownRateMultiplier, 3);
        }

        [Fact]
        public void Chilled_CannotReduceMovementBelowZero()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 1.5f, 5f, null));

            Assert.Equal(0f, statuses.MoveSpeedMultiplier, 3);
        }

        [Fact]
        public void Staggered_ReportsStunned()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            Assert.False(statuses.IsStunned);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Staggered, 1f, 1.5f, null));

            Assert.True(statuses.IsStunned);
        }

        [Fact]
        public void Empowered_IncreasesOutgoingDamage()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Empowered, 0.3f, 5f, null));

            Assert.Equal(1.3f, statuses.DamageDealtMultiplier, 3);
        }

        [Fact]
        public void Warded_AndMarked_ComposeRatherThanCancel()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Warded, 0.5f, 5f, null));
            statuses.Apply(StatusEffect.Modifier(StatusKind.Marked, 0.5f, 5f, null));

            // (1 - 0.5) * (1 + 0.5) = 0.75
            Assert.Equal(0.75f, statuses.DamageTakenMultiplier, 3);
        }

        [Fact]
        public void Ward_CannotInvertDamageIntoHealing()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Warded, 5f, 5f, null));

            Assert.Equal(0f, statuses.DamageTakenMultiplier, 3);
        }

        // -------------------------------- duration --------------------------------

        [Fact]
        public void Resolve_ShortensIncomingDurations()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals, statusResistance: 0.5f);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.3f, 10f, null));

            Assert.True(statuses.TryGet(StatusKind.Chilled, out StatusEffect effect));
            Assert.Equal(5f, effect.Remaining, 3);
        }

        [Fact]
        public void Resolve_CannotMakeATargetFullyImmune()
        {
            // 100% resistance would make statuses a no-op and break boss design.
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals, statusResistance: 1f);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.3f, 10f, null));

            Assert.True(statuses.TryGet(StatusKind.Chilled, out StatusEffect effect));
            Assert.True(effect.Remaining > 0f);
            Assert.Equal(1f, effect.Remaining, 3);
        }

        [Fact]
        public void Duration_IsCappedAtTheSafetyCeiling()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Warded, 0.2f, 10000f, null));

            Assert.True(statuses.TryGet(StatusKind.Warded, out StatusEffect effect));
            Assert.Equal(StatusEffectSystem.MaxDuration, effect.Duration, 3);
        }

        [Fact]
        public void Refresh_ExtendsDurationButKeepsTheStrongerMagnitude()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Empowered, 0.5f, 2f, null));
            statuses.Apply(StatusEffect.Modifier(StatusKind.Empowered, 0.1f, 6f, null));

            Assert.True(statuses.TryGet(StatusKind.Empowered, out StatusEffect effect));
            Assert.Equal(0.5f, effect.Magnitude, 3);
            Assert.Equal(6f, effect.Remaining, 3);
        }

        [Fact]
        public void Apply_OnADeadTarget_IsIgnored()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals, 10f);
            vitals.ApplyDamage(10f, null);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.3f, 5f, null));

            Assert.Equal(0, statuses.ActiveCount);
        }

        [Fact]
        public void Apply_WithZeroDurationAfterResistance_IsIgnored()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals, statusResistance: 1f);

            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.3f, 0f, null));

            Assert.Equal(0, statuses.ActiveCount);
        }

        // --------------------------------- queries --------------------------------

        [Fact]
        public void RemoveAllDamageOverTime_LeavesModifiersInPlace()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);
            statuses.Apply(StatusEffect.Dot(StatusKind.Burning, DamageType.Ember, 5f, 5f, 1f, null, 1));
            statuses.Apply(StatusEffect.Dot(StatusKind.Bleeding, DamageType.Physical, 5f, 5f, 1f, null, 1));
            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.2f, 5f, null));

            int removed = statuses.RemoveAllDamageOverTime();

            Assert.Equal(2, removed);
            Assert.Equal(1, statuses.ActiveCount);
            Assert.True(statuses.Has(StatusKind.Chilled));
        }

        [Fact]
        public void Expired_FiresWhenAnEffectEnds()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);
            int expired = 0;
            statuses.Expired += _ => expired++;

            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.2f, 1f, null));
            statuses.Tick(2f);

            Assert.Equal(1, expired);
        }

        [Fact]
        public void Clear_RemovesEverything()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);
            statuses.Apply(StatusEffect.Dot(StatusKind.Burning, DamageType.Ember, 5f, 5f, 1f, null, 1));
            statuses.Apply(StatusEffect.Modifier(StatusKind.Chilled, 0.2f, 5f, null));

            statuses.Clear();

            Assert.Equal(0, statuses.ActiveCount);
            Assert.Equal(1f, statuses.MoveSpeedMultiplier, 3);
        }

        [Fact]
        public void Magnitude_OfAnAbsentEffect_IsZero()
        {
            StatusEffectSystem statuses = MakeSystem(out Vitals vitals);

            Assert.Equal(0f, statuses.Magnitude(StatusKind.Burning));
            Assert.False(statuses.Has(StatusKind.Burning));
        }
    }
}
