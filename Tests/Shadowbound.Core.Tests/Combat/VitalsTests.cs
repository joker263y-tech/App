using Shadowbound.Core.Combat;
using Shadowbound.Core.Stats;
using Xunit;

namespace Shadowbound.Core.Tests.Combat
{
    public class VitalsTests
    {
        private static Vitals MakeVitals(float maxHealth = 100f, float maxStamina = 50f)
        {
            var stats = new StatSet();
            stats.SetBase(StatId.MaxHealth, maxHealth);
            stats.SetBase(StatId.MaxStamina, maxStamina);

            var vitals = new Vitals(stats, new ResistanceSet());
            vitals.ResetToFull();
            return vitals;
        }

        [Fact]
        public void BeforeInitialisation_IsEmptyAndNotAlive()
        {
            var stats = new StatSet();
            stats.SetBase(StatId.MaxHealth, 100f);
            var vitals = new Vitals(stats, new ResistanceSet());

            Assert.False(vitals.IsAlive);
            Assert.Equal(0f, vitals.Health);
        }

        [Fact]
        public void ResetToFull_FillsBothPools()
        {
            Vitals vitals = MakeVitals(120f, 40f);

            Assert.Equal(120f, vitals.Health);
            Assert.Equal(40f, vitals.Stamina);
            Assert.Equal(1f, vitals.HealthFraction, 3);
        }

        [Fact]
        public void ApplyDamage_ReturnsTheAmountActuallyApplied()
        {
            Vitals vitals = MakeVitals(100f);

            float applied = vitals.ApplyDamage(30f, null);

            Assert.Equal(30f, applied);
            Assert.Equal(70f, vitals.Health);
        }

        [Fact]
        public void ApplyDamage_CannotDriveHealthBelowZero()
        {
            Vitals vitals = MakeVitals(100f);

            float applied = vitals.ApplyDamage(500f, null);

            Assert.Equal(100f, applied);
            Assert.Equal(0f, vitals.Health);
        }

        [Fact]
        public void ApplyDamage_OnACorpse_ReturnsZero()
        {
            // Without this, a second attacker in the same frame would count
            // kill credit for damage that was never dealt.
            Vitals vitals = MakeVitals(100f);
            vitals.ApplyDamage(100f, null);

            float overkill = vitals.ApplyDamage(50f, null);

            Assert.Equal(0f, overkill);
        }

        [Fact]
        public void Died_FiresExactlyOnce()
        {
            Vitals vitals = MakeVitals(50f);
            int deaths = 0;
            vitals.Died += () => deaths++;

            vitals.ApplyDamage(25f, null);
            vitals.ApplyDamage(25f, null);
            vitals.ApplyDamage(25f, null);
            vitals.Tick(1f);

            Assert.Equal(1, deaths);
        }

        [Fact]
        public void Damaged_ReportsAmountAndSource()
        {
            Vitals vitals = MakeVitals(100f);
            var attacker = new object();
            float reported = -1f;
            object reportedSource = null;

            vitals.Damaged += (amount, source) =>
            {
                reported = amount;
                reportedSource = source;
            };

            vitals.ApplyDamage(12f, attacker);

            Assert.Equal(12f, reported);
            Assert.Same(attacker, reportedSource);
        }

        [Fact]
        public void Heal_IsCappedAtMaximum()
        {
            Vitals vitals = MakeVitals(100f);
            vitals.ApplyDamage(30f, null);

            float restored = vitals.Heal(1000f);

            Assert.Equal(30f, restored);
            Assert.Equal(100f, vitals.Health);
        }

        [Fact]
        public void Heal_OnACorpse_DoesNothing()
        {
            Vitals vitals = MakeVitals(100f);
            vitals.ApplyDamage(100f, null);

            Assert.Equal(0f, vitals.Heal(50f));
        }

        [Fact]
        public void TrySpendStamina_IsAllOrNothing()
        {
            Vitals vitals = MakeVitals(100f, 30f);

            bool first = vitals.TrySpendStamina(20f);
            bool second = vitals.TrySpendStamina(20f);

            Assert.True(first);
            Assert.False(second);
            // The failed attempt must not have partially drained the pool.
            Assert.Equal(10f, vitals.Stamina);
        }

        [Fact]
        public void TrySpendStamina_WhenInsufficient_RaisesDepleted()
        {
            Vitals vitals = MakeVitals(100f, 5f);
            bool raised = false;
            vitals.StaminaDepleted += () => raised = true;

            vitals.TrySpendStamina(50f);

            Assert.True(raised);
        }

        [Fact]
        public void Tick_RegeneratesHealthAndStaminaOverTime()
        {
            var stats = new StatSet();
            stats.SetBase(StatId.MaxHealth, 100f);
            stats.SetBase(StatId.MaxStamina, 100f);
            stats.SetBase(StatId.HealthRegen, 10f);
            stats.SetBase(StatId.StaminaRegen, 20f);

            var vitals = new Vitals(stats, new ResistanceSet());
            vitals.ResetToFull();
            vitals.ApplyDamage(50f, null);
            vitals.TrySpendStamina(50f);

            vitals.Tick(2f);

            Assert.Equal(70f, vitals.Health, 3);
            Assert.Equal(90f, vitals.Stamina, 3);
        }

        [Fact]
        public void Tick_WhenMaximumShrinks_ClampsCurrentHealth()
        {
            // Removing armour drops MaxHealth; health must not stay above it.
            var stats = new StatSet();
            stats.SetBase(StatId.MaxHealth, 200f);
            var vitals = new Vitals(stats, new ResistanceSet());
            vitals.ResetToFull();

            stats.SetBase(StatId.MaxHealth, 80f);
            vitals.Tick(0.016f);

            Assert.Equal(80f, vitals.Health, 3);
        }

        [Fact]
        public void Tick_OnCorpse_DoesNotResurrectViaRegeneration()
        {
            var stats = new StatSet();
            stats.SetBase(StatId.MaxHealth, 100f);
            stats.SetBase(StatId.HealthRegen, 100f);
            var vitals = new Vitals(stats, new ResistanceSet());
            vitals.ResetToFull();

            vitals.ApplyDamage(100f, null);
            vitals.Tick(5f);

            Assert.False(vitals.IsAlive);
            Assert.Equal(0f, vitals.Health);
        }

        [Fact]
        public void HealthFraction_TracksDamage()
        {
            Vitals vitals = MakeVitals(200f);

            vitals.ApplyDamage(50f, null);

            Assert.Equal(0.75f, vitals.HealthFraction, 3);
        }
    }
}
