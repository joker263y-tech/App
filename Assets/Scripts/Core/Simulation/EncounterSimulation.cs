using System;
using System.Collections.Generic;
using Shadowbound.Core.Ai;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Randomness;

namespace Shadowbound.Core.Simulation
{
    /// <summary>A rectangular slice of world an encounter happens inside.</summary>
    public readonly struct WorldBounds
    {
        public readonly Float3 Min;
        public readonly Float3 Max;

        public WorldBounds(Float3 min, Float3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>A square arena, which is what placeholder content is authored against.</summary>
        public static WorldBounds Square(float halfExtent, float minY = -1f, float maxY = 6f)
        {
            return new WorldBounds(
                new Float3(-halfExtent, minY, -halfExtent),
                new Float3(halfExtent, maxY, halfExtent));
        }

        /// <summary>Bounds that constrain nothing. Used by tests that want free movement.</summary>
        public static WorldBounds Unbounded()
        {
            const float limit = 1e7f;
            return new WorldBounds(new Float3(-limit, -limit, -limit), new Float3(limit, limit, limit));
        }

        public Float3 Size
        {
            get { return Max - Min; }
        }

        public bool Contains(Float3 point)
        {
            return point.X >= Min.X && point.X <= Max.X
                && point.Y >= Min.Y && point.Y <= Max.Y
                && point.Z >= Min.Z && point.Z <= Max.Z;
        }

        public Float3 Clamp(Float3 point)
        {
            return new Float3(
                FMath.Clamp(point.X, Min.X, Max.X),
                FMath.Clamp(point.Y, Min.Y, Max.Y),
                FMath.Clamp(point.Z, Min.Z, Max.Z));
        }
    }

    /// <summary>
    /// What a driven combatant wants to do this step. The player's input layer and
    /// the enemy brain both ultimately produce one of these, which is why the two
    /// share a single movement path.
    /// </summary>
    public struct CombatIntent
    {
        /// <summary>Unit direction to move, or zero to hold.</summary>
        public Float3 MoveDirection;

        /// <summary>Fraction of the combatant's effective movement speed to use, 0..1.</summary>
        public float SpeedScale;

        /// <summary>Turn to face the current target.</summary>
        public bool LookAtTarget;

        /// <summary>Turn to face a specific direction, when not facing the target.</summary>
        public Float3 LookDirection;

        /// <summary>
        /// Whether an ability should be activated this step.
        ///
        /// Stated as an explicit flag rather than inferring "no ability" from a
        /// sentinel index, because a struct's default has an index of 0: without
        /// this, a driver that forgot to set the field would silently fire ability
        /// 0, and one that legitimately wanted ability 0 while standing still
        /// would be indistinguishable from doing nothing at all.
        /// </summary>
        public bool ActivateAbility;

        /// <summary>Ability index to activate. Only read when <see cref="ActivateAbility"/> is set.</summary>
        public int AbilityIndex;

        /// <summary>Preferred target index into the participant list, or -1 to choose automatically.</summary>
        public int TargetIndex;

        public static CombatIntent None()
        {
            return new CombatIntent
            {
                ActivateAbility = false,
                AbilityIndex = -1,
                TargetIndex = -1,
                SpeedScale = 0f
            };
        }
    }

    /// <summary>
    /// Drives one combatant. The Unity layer supplies an input-driven
    /// implementation, and tests supply a scripted one, so neither the simulation
    /// nor the combat rules need to know where intent comes from.
    /// </summary>
    public interface ICombatantDriver
    {
        CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world);
    }

    /// <summary>
    /// Answers whether one point can see another. Kept behind an interface because
    /// the core has no geometry: the Unity layer supplies a physics raycast, and
    /// tests supply one that always or never sees.
    /// </summary>
    public interface IOcclusionProvider
    {
        bool HasLineOfSight(Float3 from, Float3 to);
    }

    /// <summary>Visibility that is always clear. The default when no provider is given.</summary>
    public sealed class OpenSight : IOcclusionProvider
    {
        public bool HasLineOfSight(Float3 from, Float3 to)
        {
            return true;
        }
    }

    /// <summary>One combatant in a running encounter, with whatever drives it.</summary>
    public sealed class Participant
    {
        public Combatant Combatant;
        public AbilityController Abilities;

        /// <summary>Set for enemies. Null for the player.</summary>
        public EnemyBrain Brain;

        /// <summary>Set for the player. Null for enemies.</summary>
        public ICombatantDriver Driver;

        /// <summary>Where this combatant belongs. Enemies return here when they lose interest.</summary>
        public Float3 HomePosition;

        public float TurnSpeedDegreesPerSecond = 540f;

        /// <summary>Which ability the AI should attack with.</summary>
        public int AttackAbilityIndex;

        public bool IsPlayer;

        /// <summary>Latest AI state, for animation and diagnostics.</summary>
        public AiState LastAiState;

        /// <summary>Index of the ability that landed on the most recent step, or -1.</summary>
        public int LastLandedAbility = -1;

        /// <summary>Intent decided this step, applied during the movement phase.</summary>
        public CombatIntent PendingIntent;

        public bool IsAlive
        {
            get { return Combatant != null && Combatant.IsAlive; }
        }

        public bool IsPlayerControlled
        {
            get { return IsPlayer; }
        }

        public Participant(Combatant combatant, IReadOnlyList<AbilityDefinition> abilities, int attackAbilityIndex)
        {
            Combatant = combatant;
            Abilities = new AbilityController(combatant, abilities);
            HomePosition = combatant.Position;
            AttackAbilityIndex = attackAbilityIndex;
            PendingIntent = CombatIntent.None();
        }
    }

    /// <summary>
    /// Runs one encounter: a fixed-timestep loop that advances abilities, status
    /// effects, AI decisions, movement and damage in a defined order.
    ///
    /// The whole encounter is deterministic. Given the same seed, the same
    /// combatants and the same inputs, every step produces identical results, so a
    /// failing fight can be replayed from a seed rather than guessed at.
    ///
    /// Step order is fixed and deliberate:
    ///   1. Advance ability controllers; collect blows that land this step.
    ///   2. Interrupt any cast whose caster has just been staggered.
    ///   3. Advance statuses, regeneration and knockback decay.
    ///   4. Decide intent for every living combatant.
    ///   5. Apply movement and turning.
    ///   6. Resolve the blows collected in step 1, at the positions reached in
    ///      step 5.
    ///
    /// Resolving after movement is what makes a swing land where the target
    /// actually is at the moment of impact, rather than where it was when the
    /// animation started.
    /// </summary>
    public sealed class EncounterSimulation
    {
        private struct Landing
        {
            public Participant Participant;
            public int AbilityIndex;
        }

        private readonly List<Participant> _participants;
        private readonly List<Combatant> _combatants;
        private readonly List<Combatant> _hitBuffer;
        private readonly List<Landing> _landings;

        private float _accumulator;

        public EncounterSimulation(
            DeterministicRng rng,
            WorldBounds bounds,
            IOcclusionProvider occlusion = null)
        {
            Rng = rng ?? new DeterministicRng(1);
            Bounds = bounds;
            Occlusion = occlusion ?? new OpenSight();

            _participants = new List<Participant>(16);
            _combatants = new List<Combatant>(16);
            _hitBuffer = new List<Combatant>(8);
            _landings = new List<Landing>(8);

            FixedDeltaTime = 1f / 60f;
            MaxStepsPerUpdate = 6;
        }

        public DeterministicRng Rng { get; private set; }

        public WorldBounds Bounds { get; set; }

        public IOcclusionProvider Occlusion { get; set; }

        /// <summary>Simulation timestep. Combat tuning is expressed against this.</summary>
        public float FixedDeltaTime { get; set; }

        /// <summary>
        /// Most steps a single Update may run. Prevents a frame hitch from
        /// triggering a spiral where catching up takes longer than the hitch.
        /// </summary>
        public int MaxStepsPerUpdate { get; set; }

        /// <summary>Accumulated simulation time in seconds.</summary>
        public float Time { get; private set; }

        public int StepCount { get; private set; }

        /// <summary>How many enemies are still standing.</summary>
        public int HostilesRemaining { get; private set; }

        public IReadOnlyList<Participant> Participants
        {
            get { return _participants; }
        }

        public IReadOnlyList<Combatant> Combatants
        {
            get { return _combatants; }
        }

        /// <summary>Raised for every applied hit. Arguments: attacker, victim, result.</summary>
        public event Action<Participant, Combatant, DamageResult> DamageDealt;

        /// <summary>Raised once per combatant, when it dies.</summary>
        public event Action<Combatant, Participant> Died;

        /// <summary>
        /// How close an ally must be to be alerted when one of its number is hurt.
        /// Without this, a player can pick off a group one at a time from the edge
        /// of the fight while the rest stand and watch.
        /// </summary>
        public float AggroPropagationRadius { get; set; } = 14f;

        /// <summary>
        /// Spawns a combatant with a scripted or input driver, for the player or
        /// for anything else that should not have an enemy brain.
        /// </summary>
        public Participant AddDriven(
            Combatant combatant,
            IReadOnlyList<AbilityDefinition> abilities,
            ICombatantDriver driver,
            bool isPlayer = false)
        {
            Participant participant = Attach(combatant, abilities, 0);
            participant.Driver = driver;
            participant.IsPlayer = isPlayer;
            return participant;
        }

        /// <summary>Spawns an enemy with its own brain.</summary>
        public Participant AddEnemy(
            Combatant combatant,
            IReadOnlyList<AbilityDefinition> abilities,
            EnemyBrainSettings brainSettings,
            int attackAbilityIndex = 0)
        {
            Participant participant = Attach(combatant, abilities, attackAbilityIndex);

            // Each enemy gets its own generator stream, so changing one enemy's
            // behaviour cannot shift the patrol route of another.
            ulong stream = (ulong)combatant.Id.GetHashCode() | 1UL;
            participant.Brain = new EnemyBrain(brainSettings, Rng.Fork(stream));

            return participant;
        }

        public Participant Find(string combatantId)
        {
            for (int i = 0; i < _participants.Count; i++)
            {
                if (_participants[i].Combatant.Id == combatantId)
                {
                    return _participants[i];
                }
            }

            return null;
        }

        public Participant FindParticipant(Combatant combatant)
        {
            for (int i = 0; i < _participants.Count; i++)
            {
                if (ReferenceEquals(_participants[i].Combatant, combatant))
                {
                    return _participants[i];
                }
            }

            return null;
        }

        /// <summary>Nearest living hostile within range, or null.</summary>
        public Combatant FindNearestHostile(Combatant self, float maxRange)
        {
            if (self == null)
            {
                return null;
            }

            Combatant best = null;
            float bestDistance = maxRange;

            for (int i = 0; i < _combatants.Count; i++)
            {
                Combatant candidate = _combatants[i];
                if (!candidate.IsAlive || !self.IsHostileTo(candidate))
                {
                    continue;
                }

                float distance = Float3.DistanceXZ(self.Position, candidate.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        public bool HasLineOfSight(Float3 from, Combatant target)
        {
            if (target == null)
            {
                return false;
            }

            return Occlusion == null || Occlusion.HasLineOfSight(from, target.Position);
        }

        /// <summary>
        /// Advances the simulation by real elapsed time, running as many fixed
        /// steps as the accumulator allows. Returns the number of steps taken.
        /// </summary>
        public int Update(float deltaTime)
        {
            if (deltaTime <= 0f || FixedDeltaTime <= 0f)
            {
                return 0;
            }

            _accumulator += deltaTime;

            int steps = 0;
            while (_accumulator >= FixedDeltaTime && steps < MaxStepsPerUpdate)
            {
                _accumulator -= FixedDeltaTime;
                Step();
                steps++;
            }

            if (steps >= MaxStepsPerUpdate)
            {
                // Drop the backlog rather than trying to catch up forever.
                _accumulator = 0f;
            }

            return steps;
        }

        /// <summary>Runs a fixed number of steps. Used by tests and by offline simulation.</summary>
        public void Advance(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            int steps = (int)(seconds / FixedDeltaTime);
            for (int i = 0; i < steps; i++)
            {
                Step();
            }
        }

        /// <summary>Runs exactly one fixed timestep.</summary>
        public void Step()
        {
            float deltaTime = FixedDeltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            Time += deltaTime;
            StepCount++;

            CollectLandings(deltaTime);
            InterruptStaggeredCasts();
            AdvanceCombatants(deltaTime);
            Decide();
            Move(deltaTime);
            ResolveLandings();
            RecountHostiles();
        }

        /// <summary>
        /// Returns every participant to its starting state: full vitals, no status
        /// effects, cleared cooldowns and a reset brain.
        ///
        /// Dead participants are revived too. This is the boss-retry path, and
        /// only restoring the survivors would leave a wiped attempt permanently
        /// unresettable.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < _participants.Count; i++)
            {
                Participant participant = _participants[i];

                float facing = participant.Combatant.FacingDegrees;
                participant.Combatant.Revive(participant.HomePosition, facing);
                participant.Abilities.ResetCooldowns();
                participant.PendingIntent = CombatIntent.None();
                participant.LastLandedAbility = -1;

                if (participant.Brain != null)
                {
                    participant.Brain.Reset();
                }
            }

            _accumulator = 0f;
            RecountHostiles();
        }

        private Participant Attach(Combatant combatant, IReadOnlyList<AbilityDefinition> abilities, int attackAbilityIndex)
        {
            if (combatant == null)
            {
                throw new ArgumentNullException(nameof(combatant));
            }

            var participant = new Participant(combatant, abilities, attackAbilityIndex);

            combatant.Damaged += OnCombatantDamaged;
            combatant.Died += OnCombatantDied;

            _participants.Add(participant);
            _combatants.Add(combatant);
            RecountHostiles();

            return participant;
        }

        private void OnCombatantDamaged(Combatant victim, Combatant attacker, DamageResult result)
        {
            Participant participant = FindParticipant(victim);

            // Being hit always alerts, regardless of facing, so an enemy cannot be
            // killed from behind its vision cone without ever reacting.
            if (participant != null && participant.Brain != null && victim.IsAlive)
            {
                participant.Brain.ForceAlert();
            }

            PropagateAggro(victim);

            DamageDealt?.Invoke(FindParticipant(attacker), victim, result);
        }

        private void OnCombatantDied(Combatant victim)
        {
            Died?.Invoke(victim, FindParticipant(victim));
        }

        /// <summary>Alerts nearby allies when a combatant is hurt.</summary>
        private void PropagateAggro(Combatant victim)
        {
            if (AggroPropagationRadius <= 0f)
            {
                return;
            }

            for (int i = 0; i < _participants.Count; i++)
            {
                Participant ally = _participants[i];

                if (ally.Brain == null || !ally.Combatant.IsAlive || ReferenceEquals(ally.Combatant, victim))
                {
                    continue;
                }

                if (ally.Combatant.Faction != victim.Faction)
                {
                    continue;
                }

                if (Float3.DistanceXZ(ally.Combatant.Position, victim.Position) <= AggroPropagationRadius)
                {
                    ally.Brain.ForceAlert();
                }
            }
        }

        private void CollectLandings(float deltaTime)
        {
            _landings.Clear();

            for (int i = 0; i < _participants.Count; i++)
            {
                Participant participant = _participants[i];
                int landed = participant.Abilities.Tick(deltaTime);
                participant.LastLandedAbility = landed;

                if (landed >= 0)
                {
                    _landings.Add(new Landing { Participant = participant, AbilityIndex = landed });
                }
            }
        }

        /// <summary>
        /// A caster staggered mid-windup loses the ability. This is what makes
        /// interrupts meaningful: without it, staggering would only delay a blow
        /// that still lands a moment later.
        /// </summary>
        private void InterruptStaggeredCasts()
        {
            for (int i = 0; i < _participants.Count; i++)
            {
                Participant participant = _participants[i];

                if (participant.Combatant.IsStunned && participant.Abilities.Phase == CastPhase.Windup)
                {
                    participant.Abilities.Interrupt();
                }
            }
        }

        private void AdvanceCombatants(float deltaTime)
        {
            for (int i = 0; i < _combatants.Count; i++)
            {
                _combatants[i].Tick(deltaTime);
            }
        }

        private void Decide()
        {
            for (int i = 0; i < _participants.Count; i++)
            {
                Participant participant = _participants[i];

                if (!participant.IsAlive)
                {
                    continue;
                }

                CombatIntent intent = DecideFor(participant);
                participant.PendingIntent = intent;

                if (intent.ActivateAbility && intent.AbilityIndex >= 0)
                {
                    participant.Abilities.TryActivate(intent.AbilityIndex, out _);
                }
            }
        }

        private CombatIntent DecideFor(Participant participant)
        {
            var intent = CombatIntent.None();
            Combatant self = participant.Combatant;

            Combatant target = ResolveTarget(participant);

            if (participant.Driver != null)
            {
                // The driver states explicitly whether it wants an ability, so
                // there is nothing to infer here.
                return participant.Driver.Decide(FixedDeltaTime, self, this);
            }

            if (participant.Brain == null)
            {
                return intent;
            }

            float moveSpeed = self.EffectiveMoveSpeed;

            var context = new AiContext(
                self.Position,
                self.Forward,
                participant.HomePosition,
                target != null,
                target != null ? target.Position : Float3.Zero,
                target != null && target.IsAlive,
                HasLineOfSight(self.Position, target),
                self.IsAlive,
                self.IsStunned,
                participant.Abilities.IsReady(participant.AttackAbilityIndex),
                moveSpeed);

            AiIntent ai = participant.Brain.Tick(FixedDeltaTime, context);
            participant.LastAiState = participant.Brain.State;

            intent.MoveDirection = ai.MoveDirection;

            // The brain expresses speed against the combatant's effective movement
            // speed, so converting back to a 0..1 scale lets both drivers share one
            // movement path without slowing the enemy twice.
            intent.SpeedScale = moveSpeed <= FMath.Epsilon
                ? 0f
                : ai.DesiredSpeed / moveSpeed;
            intent.LookAtTarget = ai.FaceTarget;

            intent.ActivateAbility = ai.WantsToAttack;
            intent.AbilityIndex = participant.AttackAbilityIndex;

            return intent;
        }

        /// <summary>Nearest living hostile, used both for facing and for AI context.</summary>
        private Combatant ResolveTarget(Participant participant)
        {
            return FindNearestHostile(participant.Combatant, float.MaxValue);
        }

        private void Move(float deltaTime)
        {
            for (int i = 0; i < _participants.Count; i++)
            {
                Participant participant = _participants[i];

                if (!participant.IsAlive)
                {
                    continue;
                }

                ApplyMovement(participant, participant.PendingIntent, deltaTime);
            }
        }

        private void ApplyMovement(Participant participant, in CombatIntent intent, float deltaTime)
        {
            Combatant combatant = participant.Combatant;
            Float3 delta = Float3.Zero;

            if (!combatant.IsStunned)
            {
                Float3 direction = intent.MoveDirection.FlattenedXZ;

                if (direction != Float3.Zero)
                {
                    float scale = FMath.Clamp(intent.SpeedScale, 0f, 2f);
                    float speed = combatant.EffectiveMoveSpeed * scale * participant.Abilities.MoveSpeedMultiplier;

                    if (speed > 0f)
                    {
                        delta += direction * (speed * deltaTime);
                    }
                }
            }

            if (combatant.KnockbackSpeed > 0f)
            {
                delta += combatant.KnockbackDirection * (combatant.KnockbackSpeed * deltaTime);
            }

            if (delta != Float3.Zero)
            {
                combatant.SetPosition(Bounds.Clamp(combatant.Position + delta));
            }

            if (intent.LookAtTarget)
            {
                Combatant target = FindNearestHostile(combatant, float.MaxValue);
                if (target != null)
                {
                    combatant.TurnTowards(
                        target.Position - combatant.Position,
                        participant.TurnSpeedDegreesPerSecond,
                        deltaTime);
                }

                return;
            }

            if (intent.LookDirection != Float3.Zero)
            {
                combatant.TurnTowards(intent.LookDirection, participant.TurnSpeedDegreesPerSecond, deltaTime);
                return;
            }

            if (intent.MoveDirection != Float3.Zero)
            {
                combatant.TurnTowards(intent.MoveDirection, participant.TurnSpeedDegreesPerSecond, deltaTime);
            }
        }

        private void ResolveLandings()
        {
            for (int i = 0; i < _landings.Count; i++)
            {
                Landing landing = _landings[i];
                Participant participant = landing.Participant;

                if (!participant.Combatant.IsAlive)
                {
                    continue;
                }

                AbilityDefinition ability = participant.Abilities[landing.AbilityIndex];
                if (ability == null)
                {
                    continue;
                }

                AttackResolver.Resolve(
                    participant.Combatant,
                    ability,
                    _combatants,
                    Rng,
                    _hitBuffer);
            }
        }

        private void RecountHostiles()
        {
            int alive = 0;

            for (int i = 0; i < _combatants.Count; i++)
            {
                Combatant combatant = _combatants[i];

                if (combatant.IsAlive && combatant.Faction == Faction.Hostile)
                {
                    alive++;
                }
            }

            HostilesRemaining = alive;
        }

        /// <summary>
        /// Replaces the generator, for loading a save mid-encounter. The state is
        /// restored exactly so subsequent rolls continue the same sequence.
        /// </summary>
        public void RestoreRng(ulong state, ulong increment)
        {
            Rng = DeterministicRng.Restore(state, increment);
        }
    }
}
