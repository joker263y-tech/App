using Shadowbound.Core.Numerics;
using Shadowbound.Core.Randomness;

namespace Shadowbound.Core.Combat
{
    /// <summary>One incoming attack, before any mitigation is resolved.</summary>
    public readonly struct DamageRequest
    {
        public readonly DamageType Type;

        /// <summary>Base damage before variance, crit and mitigation.</summary>
        public readonly float Amount;

        /// <summary>Flat armor ignored by this attack. Strong against light armor.</summary>
        public readonly float ArmorPenetration;

        /// <summary>
        /// Fraction of armor ignored, 0..1. Strong against heavy armor.
        ///
        /// The two penetration stats are not redundant. Because mitigation uses
        /// a diminishing-returns curve, flat penetration removes the most
        /// mitigation where the curve is steepest - at low armor values - while
        /// a percentage cut scales with the target's armor and therefore pays
        /// off most against heavily armoured bosses. Both are kept so that
        /// encounters can favour one or the other.
        /// </summary>
        public readonly float ArmorPenetrationPercent;

        /// <summary>Chance to crit, 0..1.</summary>
        public readonly float CritChance;

        /// <summary>Damage multiplier on a crit, e.g. 1.75.</summary>
        public readonly float CritMultiplier;

        /// <summary>Attacker level, used to scale down the value of flat armor.</summary>
        public readonly int AttackerLevel;

        /// <summary>Fraction of random spread, e.g. 0.1 for +/-10%.</summary>
        public readonly float Variance;

        public DamageRequest(
            DamageType type,
            float amount,
            float armorPenetration = 0f,
            float critChance = 0f,
            float critMultiplier = 1.5f,
            int attackerLevel = 1,
            float variance = 0f,
            float armorPenetrationPercent = 0f)
        {
            Type = type;
            Amount = amount;
            ArmorPenetration = armorPenetration;
            ArmorPenetrationPercent = FMath.Clamp(armorPenetrationPercent, 0f, 1f);
            CritChance = critChance;
            CritMultiplier = critMultiplier < 1f ? 1f : critMultiplier;
            AttackerLevel = attackerLevel < 1 ? 1 : attackerLevel;
            Variance = FMath.Clamp(variance, 0f, 0.9f);
        }
    }

    /// <summary>Every intermediate stage of a resolved hit, kept for HUD feedback and tests.</summary>
    public readonly struct DamageResult
    {
        /// <summary>Damage after variance, crit and attacker buffs, before defence.</summary>
        public readonly float Raw;

        /// <summary>Damage after armor.</summary>
        public readonly float Mitigated;

        /// <summary>Damage after resistance and defender buffs. This is what the target loses.</summary>
        public readonly float Applied;

        public readonly bool Critical;

        /// <summary>Fraction of incoming damage removed by armor and resistance combined, 0..1.</summary>
        public readonly float ReductionFraction;

        public DamageResult(float raw, float mitigated, float applied, bool critical, float reductionFraction)
        {
            Raw = raw;
            Mitigated = mitigated;
            Applied = applied;
            Critical = critical;
            ReductionFraction = reductionFraction;
        }

        public bool IsZero
        {
            get { return Applied <= 0f; }
        }

        public static readonly DamageResult None = new DamageResult(0f, 0f, 0f, false, 0f);
    }

    /// <summary>
    /// Turns a <see cref="DamageRequest"/> into a <see cref="DamageResult"/>.
    ///
    /// Resolution order, and why:
    ///   1. Variance first, so spread is visible in the crit number too.
    ///   2. Crit next, so a crit multiplies the attacker's intent, not the
    ///      defender's mitigation.
    ///   3. Attacker buffs, then armor, then resistance, then defender debuffs.
    ///      Defence is the last word, which is what makes armour feel protective
    ///      and keeps damage numbers readable.
    ///
    /// Armor uses a diminishing-returns curve. The constant grows with the
    /// attacker's level, so a fixed amount of armour is worth less against
    /// higher-level enemies and enemies must be given more armour to keep pace.
    /// This is the knob that makes progression change encounters.
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>Armor value at which mitigation reaches 50% against a level 1 attacker.</summary>
        public const float ArmorConstant = 100f;

        /// <summary>Per-level growth of the armor constant.</summary>
        public const float ArmorLevelScaling = 0.08f;

        /// <summary>Hard ceiling on armor mitigation, so armor alone can never make a target unkillable.</summary>
        public const float MaxMitigation = 0.85f;

        /// <summary>
        /// Resolves a hit.
        /// </summary>
        /// <param name="request">The incoming attack.</param>
        /// <param name="defenderArmor">Target's armor value.</param>
        /// <param name="defenderResistance">Target's resistance fraction against this school.</param>
        /// <param name="attackerDamageMultiplier">Attacker's outgoing damage multiplier, e.g. from Empowered.</param>
        /// <param name="defenderDamageTakenMultiplier">Target's incoming damage multiplier, e.g. from Warded.</param>
        /// <param name="rng">Deterministic generator for variance and crits.</param>
        public static DamageResult Resolve(
            in DamageRequest request,
            float defenderArmor,
            float defenderResistance,
            float attackerDamageMultiplier,
            float defenderDamageTakenMultiplier,
            DeterministicRng rng)
        {
            if (request.Amount <= 0f)
            {
                return DamageResult.None;
            }

            if (rng == null)
            {
                // A null generator means "no randomness": average variance, no crits.
                return ResolveDeterministic(
                    request,
                    defenderArmor,
                    defenderResistance,
                    attackerDamageMultiplier,
                    defenderDamageTakenMultiplier);
            }

            float amount = request.Amount;

            if (request.Variance > 0f)
            {
                amount *= 1f + rng.Range(-request.Variance, request.Variance);
            }

            bool critical = request.CritChance > 0f && rng.Chance(request.CritChance);
            if (critical)
            {
                amount *= request.CritMultiplier;
            }

            amount *= attackerDamageMultiplier;

            float mitigation = MitigationFraction(
                defenderArmor,
                request.ArmorPenetration,
                request.ArmorPenetrationPercent,
                request.AttackerLevel);
            float afterArmor = amount * (1f - mitigation);

            float resistance = ResistanceSet.ClampResistance(defenderResistance);
            float afterResistance = afterArmor * (1f - resistance);

            float applied = afterResistance * defenderDamageTakenMultiplier;
            if (applied < 0f)
            {
                applied = 0f;
            }

            float reduction = amount <= FMath.Epsilon ? 0f : FMath.Clamp01(1f - (applied / amount));

            return new DamageResult(amount, afterArmor, applied, critical, reduction);
        }

        /// <summary>
        /// Variance-free, crit-free resolution. Used by AI threat evaluation,
        /// which needs an expected value rather than a roll, and by tests that
        /// assert on exact numbers.
        /// </summary>
        public static DamageResult ResolveDeterministic(
            in DamageRequest request,
            float defenderArmor,
            float defenderResistance,
            float attackerDamageMultiplier,
            float defenderDamageTakenMultiplier)
        {
            if (request.Amount <= 0f)
            {
                return DamageResult.None;
            }

            float amount = request.Amount * attackerDamageMultiplier;

            float mitigation = MitigationFraction(
                defenderArmor,
                request.ArmorPenetration,
                request.ArmorPenetrationPercent,
                request.AttackerLevel);
            float afterArmor = amount * (1f - mitigation);

            float resistance = ResistanceSet.ClampResistance(defenderResistance);
            float afterResistance = afterArmor * (1f - resistance);

            float applied = afterResistance * defenderDamageTakenMultiplier;
            if (applied < 0f)
            {
                applied = 0f;
            }

            float reduction = amount <= FMath.Epsilon ? 0f : FMath.Clamp01(1f - (applied / amount));

            return new DamageResult(amount, afterArmor, applied, false, reduction);
        }

        /// <summary>
        /// Fraction of physical damage removed by armor, 0..<see cref="MaxMitigation"/>.
        /// Penetration is subtracted before the curve is evaluated, so it is most
        /// valuable against heavily armoured targets.
        /// </summary>
        public static float MitigationFraction(float armor, float penetration, int attackerLevel)
        {
            return MitigationFraction(armor, penetration, 0f, attackerLevel);
        }

        /// <summary>
        /// As above, but also removing a fraction of the target's armor first.
        /// Percentage is applied before flat, so a percentage cut is measured
        /// against the armor the target actually has.
        /// </summary>
        public static float MitigationFraction(
            float armor,
            float penetration,
            float penetrationPercent,
            int attackerLevel)
        {
            float percent = FMath.Clamp(penetrationPercent, 0f, 1f);
            float effectiveArmor = (armor * (1f - percent)) - penetration;
            if (effectiveArmor <= 0f)
            {
                return 0f;
            }

            int level = attackerLevel < 1 ? 1 : attackerLevel;
            float denominator = effectiveArmor + (ArmorConstant * (1f + (ArmorLevelScaling * level)));
            float mitigation = effectiveArmor / denominator;
            return mitigation > MaxMitigation ? MaxMitigation : mitigation;
        }

        /// <summary>
        /// Expected damage of a request, ignoring variance but including the
        /// probability-weighted value of a crit. Used for AI decisions and for
        /// displaying a preview of an ability's damage.
        /// </summary>
        public static float ExpectedDamage(
            in DamageRequest request,
            float defenderArmor,
            float defenderResistance,
            float attackerDamageMultiplier,
            float defenderDamageTakenMultiplier)
        {
            DamageResult plain = ResolveDeterministic(
                request,
                defenderArmor,
                defenderResistance,
                attackerDamageMultiplier,
                defenderDamageTakenMultiplier);

            float critChance = FMath.Clamp01(request.CritChance);
            if (critChance <= 0f)
            {
                return plain.Applied;
            }

            // The crit is resolved as its own hit rather than by scaling the
            // plain result, so that the intermediate stages (Raw, Mitigated)
            // stay meaningful and so the model keeps behaving under future
            // non-multiplicative defence such as flat damage reduction.
            //
            // Note that today this is arithmetically equivalent to scaling the
            // plain result, because every defence term is multiplicative: armor
            // and resistance scale a crit and a normal hit by the same factor,
            // so a crit's *relative* advantage is independent of how armoured
            // the target is. That is a deliberate property - it keeps crit and
            // armor as independent build axes - and it is asserted by tests.
            var critRequest = new DamageRequest(
                request.Type,
                request.Amount * request.CritMultiplier,
                request.ArmorPenetration,
                critChance: 0f,
                critMultiplier: 1f,
                attackerLevel: request.AttackerLevel,
                variance: 0f,
                armorPenetrationPercent: request.ArmorPenetrationPercent);

            DamageResult critical = ResolveDeterministic(
                critRequest,
                defenderArmor,
                defenderResistance,
                attackerDamageMultiplier,
                defenderDamageTakenMultiplier);

            // Probability-weighted blend of the two outcomes.
            return plain.Applied + (critChance * (critical.Applied - plain.Applied));
        }
    }
}
