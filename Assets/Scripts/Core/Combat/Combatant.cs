using System;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Combat
{
    /// <summary>Which side a combatant fights for. Determines valid targets.</summary>
    public enum Faction
    {
        /// <summary>The player character.</summary>
        Player = 0,

        /// <summary>Enemies. All hostiles are mutually non-aggressive unless provoked.</summary>
        Hostile = 1,

        /// <summary>Does not fight and cannot be targeted by default.</summary>
        Neutral = 2
    }

    /// <summary>
    /// Everything that can fight: the player and every enemy. This is the shared
    /// state that combat, AI, abilities and status effects all operate on.
    ///
    /// The player and enemies differ only in what drives them. Movement and
    /// ability decisions arrive as an <see cref="CombatIntent"/>, produced
    /// either by the input layer or by <see cref="Ai.EnemyBrain"/>. Nothing in
    /// this class knows which of the two it is talking to.
    /// </summary>
    public sealed class Combatant
    {
        private Float3 _position;
        private DamageResult? _pendingResult;
        private float _facingDegrees;
        private float _knockbackSpeed;
        private Float3 _knockbackDirection;
        private bool _deathAnnounced;

        public Combatant(string id, Faction faction, int level = 1)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Combatant requires a stable id.", nameof(id));
            }

            Id = id;
            Faction = faction;
            Level = level < 1 ? 1 : level;

            Stats = new StatSet();
            Resistances = new ResistanceSet();
            Vitals = new Vitals(Stats, Resistances);
            Statuses = new StatusEffectSystem(Stats, Vitals);

            // Health can be removed through more than one path: a resolved attack
            // via ReceiveDamage, or a damage-over-time tick inside the status
            // system reaching straight for the vitals pool. Observing the pool
            // itself means every one of those paths raises Damaged, so burning to
            // death alerts allies exactly like being struck does.
            Vitals.Damaged += OnVitalsDamaged;

            _position = Float3.Zero;
            _facingDegrees = 0f;
        }

        /// <summary>Stable identifier. Used for target locking, saves and kill attribution.</summary>
        public string Id { get; private set; }

        public Faction Faction { get; private set; }

        public int Level { get; set; }

        /// <summary>Archetype name shown in the HUD, e.g. "Hollow Sentinel".</summary>
        public string DisplayName { get; set; }

        public StatSet Stats { get; private set; }

        /// <summary>
        /// This combatant's resistance profile.
        ///
        /// Read-only by reference on purpose: the health pool holds the same
        /// instance, so replacing it would leave damage resolution reading the old
        /// one and silently ignore the change. Use
        /// <see cref="CopyResistancesFrom"/> to change the values.
        /// </summary>
        public ResistanceSet Resistances { get; private set; }

        /// <summary>Copies a resistance profile's values into this combatant's own profile.</summary>
        public void CopyResistancesFrom(ResistanceSet source)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < DamageTypes.All.Length; i++)
            {
                DamageType type = DamageTypes.All[i];
                Resistances.Set(type, source.Get(type));
            }
        }

        public Vitals Vitals { get; private set; }

        public StatusEffectSystem Statuses { get; private set; }

        /// <summary>Set on the player and on enemies that can be permanently killed.</summary>
        public bool IsPersistent { get; set; }

        /// <summary>
        /// Archetype identifier, such as "hollow-walker". Used as the target id in
        /// quest kill objectives and in encounter bookkeeping, so two instances of
        /// the same creature can share an archetype while keeping distinct ids.
        /// </summary>
        public string ArchetypeId { get; set; } = "";

        /// <summary>Experience granted to whoever defeats this combatant.</summary>
        public int ExperienceReward { get; set; }

        /// <summary>Loot table rolled on defeat. Empty means nothing drops.</summary>
        public string LootTableId { get; set; } = "";

        /// <summary>
        /// Bosses additionally satisfy DefeatBoss objectives, so a chapter can
        /// require a specific named encounter rather than a tally of kills.
        /// </summary>
        public bool IsBoss { get; set; }

        /// <summary>Fired once, the first time this combatant's health reaches zero.</summary>
        public event Action<Combatant> Died;

        /// <summary>
        /// Raised with the victim, the attacker and the resolved hit. The attacker
        /// is included because kill credit, aggro propagation and loot all need to
        /// know who dealt the blow, and the attacker cannot be recovered after the
        /// fact from the result alone. Null for environmental damage.
        /// </summary>
        public event Action<Combatant, Combatant, DamageResult> Damaged;

        public Float3 Position
        {
            get { return _position; }
        }

        /// <summary>Facing in degrees around Y, where 0 faces +Z.</summary>
        public float FacingDegrees
        {
            get { return _facingDegrees; }
        }

        public bool IsAlive
        {
            get { return Vitals.IsAlive; }
        }

        public bool IsStunned
        {
            get { return Statuses.IsStunned; }
        }

        /// <summary>Current knockback velocity, decayed by <see cref="Tick"/>.</summary>
        public float KnockbackSpeed
        {
            get { return _knockbackSpeed; }
        }

        public Float3 KnockbackDirection
        {
            get { return _knockbackDirection; }
        }

        public void SetPosition(Float3 position)
        {
            _position = position;
        }

        public void SetFacing(float degrees)
        {
            _facingDegrees = FMath.Repeat(degrees, 360f);
        }

        /// <summary>Turns toward a direction, limited to <paramref name="degreesPerSecond"/>.</summary>
        public void TurnTowards(Float3 direction, float degreesPerSecond, float deltaTime)
        {
            Float3 flat = direction.FlattenedXZ;
            if (flat == Float3.Zero)
            {
                return;
            }

            float desired = DirectionToDegrees(flat);
            float delta = FMath.DeltaAngle(_facingDegrees, desired);
            float step = degreesPerSecond * deltaTime;
            if (delta > step)
            {
                delta = step;
            }
            else if (delta < -step)
            {
                delta = -step;
            }

            _facingDegrees = FMath.Repeat(_facingDegrees + delta, 360f);
        }

        /// <summary>Snaps facing to a direction with no turn rate limit.</summary>
        public void FaceImmediately(Float3 direction)
        {
            Float3 flat = direction.FlattenedXZ;
            if (flat == Float3.Zero)
            {
                return;
            }

            _facingDegrees = FMath.Repeat(DirectionToDegrees(flat), 360f);
        }

        /// <summary>Converts a facing angle in degrees to a unit direction on the XZ plane.</summary>
        public Float3 Forward
        {
            get { return DegreesToDirection(_facingDegrees); }
        }

        /// <summary>Applies a knockback impulse. The strongest impulse in a frame wins.</summary>
        public void ApplyKnockback(Float3 direction, float speed)
        {
            Float3 flat = direction.FlattenedXZ;
            if (flat == Float3.Zero || speed <= 0f)
            {
                return;
            }

            if (speed >= _knockbackSpeed)
            {
                _knockbackSpeed = speed;
                _knockbackDirection = flat;
            }
        }

        /// <summary>
        /// Applies a pre-resolved hit. Damage has already been calculated by
        /// <see cref="DamageCalculator"/>, so this only mutates state and raises
        /// events. Returns the health actually lost.
        /// </summary>
        public float ReceiveDamage(in DamageResult result, Combatant source)
        {
            if (!IsAlive)
            {
                return 0f;
            }

            // The full result is carried through to the health pool, so the
            // Damaged event can report armour and crit detail rather than just a
            // number. The pool raises the event, which is what makes every damage
            // path - attack or damage over time - report identically.
            _pendingResult = result;
            float applied = Vitals.ApplyDamage(result.Applied, source);
            _pendingResult = null;

            if (applied <= 0f)
            {
                return 0f;
            }

            if (!Vitals.IsAlive)
            {
                AnnounceDeath();
            }

            return applied;
        }

        /// <summary>
        /// Advances time-based state: statuses, vitals regeneration, facing
        /// -independent knockback decay, and death announcement.
        ///
        /// Position is NOT integrated here. Movement is a decision made by the
        /// controller, so the controller moves the combatant and then calls Tick.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Statuses.Tick(deltaTime);
            Vitals.Tick(deltaTime);

            if (_knockbackSpeed > 0f)
            {
                _knockbackSpeed = FMath.MoveTowardsZero(_knockbackSpeed, KnockbackDecayPerSecond * deltaTime);
            }

            if (!Vitals.IsAlive && !_deathAnnounced)
            {
                AnnounceDeath();
            }
        }

        /// <summary>
        /// Raised for every damage path, whether it arrived through
        /// <see cref="ReceiveDamage"/> or bypassed it. Reports the full result when
        /// one is known, and a synthesised equivalent for damage that reached the
        /// health pool directly, such as a damage-over-time tick.
        /// </summary>
        private void OnVitalsDamaged(float amount, object source)
        {
            DamageResult result = _pendingResult
                ?? new DamageResult(amount, amount, amount, false, 0f);

            _pendingResult = null;
            Damaged?.Invoke(this, source as Combatant, result);
        }

        /// <summary>How quickly knockback bleeds off, in units per second squared.</summary>
        public float KnockbackDecayPerSecond { get; set; } = 18f;

        /// <summary>Reads a stat, applying the status multiplier that governs it.</summary>
        public float EffectiveMoveSpeed
        {
            get { return Stats.Get(StatId.MoveSpeed) * Statuses.MoveSpeedMultiplier; }
        }

        public float EffectiveCooldownRate
        {
            get { return Stats.Get(StatId.CooldownRate) * Statuses.CooldownRateMultiplier; }
        }

        public float EffectiveAttackPower
        {
            get { return Stats.Get(StatId.AttackPower) * Statuses.DamageDealtMultiplier; }
        }

        public float EffectiveShadowPower
        {
            get { return Stats.Get(StatId.ShadowPower) * Statuses.DamageDealtMultiplier; }
        }

        /// <summary>Revives the combatant at a position with full vitals. Used by respawn and encounter resets.</summary>
        public void Revive(Float3 position, float facingDegrees)
        {
            _deathAnnounced = false;
            _knockbackSpeed = 0f;
            Statuses.Clear();
            Vitals.ResetToFull();
            _position = position;
            _facingDegrees = FMath.Repeat(facingDegrees, 360f);
        }

        private void AnnounceDeath()
        {
            if (_deathAnnounced)
            {
                return;
            }

            _deathAnnounced = true;
            DeathAnnounced = true;

            if (Died != null)
            {
                Died(this);
            }
        }

        /// <summary>True once death has been broadcast, so listeners cannot double-handle it.</summary>
        public bool DeathAnnounced { get; private set; }

        /// <summary>Distance between two combatants on the horizontal plane.</summary>
        public float DistanceTo(Combatant other)
        {
            return other == null ? float.MaxValue : Float3.DistanceXZ(_position, other._position);
        }

        public bool IsHostileTo(Combatant other)
        {
            if (other == null || ReferenceEquals(this, other))
            {
                return false;
            }

            if (Faction == Faction.Player)
            {
                return other.Faction == Faction.Hostile;
            }

            if (Faction == Faction.Hostile)
            {
                return other.Faction == Faction.Player;
            }

            return false;
        }

        /// <summary>Converts a direction on the XZ plane to an angle in degrees, where 0 faces +Z.</summary>
        public static float DirectionToDegrees(Float3 direction)
        {
            // atan2(x, z) because 0 degrees is +Z and the angle grows toward +X.
            double degrees = System.Math.Atan2(direction.X, direction.Z) * FMath.Rad2Deg;
            return FMath.Repeat((float)degrees, 360f);
        }

        /// <summary>Converts an angle in degrees to a unit direction on the XZ plane.</summary>
        public static Float3 DegreesToDirection(float degrees)
        {
            double radians = degrees * FMath.Deg2Rad;
            return new Float3(
                (float)System.Math.Sin(radians),
                0f,
                (float)System.Math.Cos(radians));
        }
    }
}
