using System;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Randomness;

namespace Shadowbound.Core.Ai
{
    /// <summary>
    /// What an enemy is currently doing. Read by the Unity layer for animation
    /// and by the simulation for diagnostics.
    /// </summary>
    public enum AiState
    {
        /// <summary>Standing watch. Will wander after a while.</summary>
        Idle = 0,

        /// <summary>Moving between points near its post.</summary>
        Patrol = 1,

        /// <summary>Has noticed the player and is reacting. The reaction delay.</summary>
        Alert = 2,

        /// <summary>Closing on the target.</summary>
        Chase = 3,

        /// <summary>Committing to an attack, and locked into it.</summary>
        Attack = 4,

        /// <summary>Interrupted and unable to act.</summary>
        Staggered = 5,

        /// <summary>Permanently out of the fight.</summary>
        Dead = 6
    }

    /// <summary>Tunable behaviour values for one enemy archetype.</summary>
    public sealed class EnemyBrainSettings
    {
        public float ViewDistance = 14f;
        public float ViewHalfAngleDegrees = 70f;
        public float ProximityRadius = 3f;

        /// <summary>Sight range multiplier once already alerted. An enemy that knows you exist tracks you further.</summary>
        public float AlertedViewMultiplier = 1.4f;

        /// <summary>Beyond this, an alerted enemy gives up and goes home.</summary>
        public float DeaggroRange = 30f;

        /// <summary>Seconds an enemy keeps chasing after losing sight, before giving up.</summary>
        public float LoseSightGrace = 4f;

        /// <summary>Distance at which an enemy will commit to an attack.</summary>
        public float AttackRange = 2.6f;

        /// <summary>Distance the enemy tries to hold while fighting.</summary>
        public float PreferredRange = 2f;

        /// <summary>Seconds between noticing the player and starting to move.</summary>
        public float ReactionTime = 0.45f;

        public float IdleDuration = 3f;
        public float PatrolRadius = 9f;
        public float PatrolSpeedMultiplier = 0.45f;
        public float ChaseSpeedMultiplier = 1f;
        public float BackoffSpeedMultiplier = 0.55f;

        /// <summary>
        /// Seconds locked into an attack before the enemy can move again. This is
        /// the enemy's commitment window, and is intentionally independent of the
        /// ability's own cooldown.
        /// </summary>
        public float AttackCommitment = 1.1f;

        /// <summary>How long a patrol point is pursued before a new one is chosen.</summary>
        public float PatrolWaypointTimeout = 6f;
    }

    /// <summary>What the world looks like to an enemy this tick.</summary>
    public readonly struct AiContext
    {
        public readonly Float3 SelfPosition;
        public readonly Float3 SelfForward;

        /// <summary>Where the enemy was placed. It returns here when it loses interest.</summary>
        public readonly Float3 HomePosition;

        public readonly bool HasTarget;
        public readonly Float3 TargetPosition;
        public readonly bool TargetAlive;
        public readonly bool HasLineOfSight;

        public readonly bool IsAlive;
        public readonly bool IsStunned;

        /// <summary>True when the enemy's combat controller can start an attack right now.</summary>
        public readonly bool AttackReady;

        /// <summary>Movement speed in units per second at full commitment.</summary>
        public readonly float MoveSpeed;

        public AiContext(
            Float3 selfPosition,
            Float3 selfForward,
            Float3 homePosition,
            bool hasTarget,
            Float3 targetPosition,
            bool targetAlive,
            bool hasLineOfSight,
            bool isAlive,
            bool isStunned,
            bool attackReady,
            float moveSpeed)
        {
            SelfPosition = selfPosition;
            SelfForward = selfForward;
            HomePosition = homePosition;
            HasTarget = hasTarget;
            TargetPosition = targetPosition;
            TargetAlive = targetAlive;
            HasLineOfSight = hasLineOfSight;
            IsAlive = isAlive;
            IsStunned = isStunned;
            AttackReady = attackReady;
            MoveSpeed = moveSpeed;
        }
    }

    /// <summary>What an enemy wants to do this tick. The simulation executes it.</summary>
    public struct AiIntent
    {
        /// <summary>Unit direction to move, or zero to hold position.</summary>
        public Float3 MoveDirection;

        /// <summary>Speed in units per second. Zero while holding.</summary>
        public float DesiredSpeed;

        /// <summary>True when the enemy should turn to face the target.</summary>
        public bool FaceTarget;

        /// <summary>True when the enemy wants its attack started this tick.</summary>
        public bool WantsToAttack;

        public static readonly AiIntent None = new AiIntent();

        public bool IsMoving
        {
            get { return DesiredSpeed > 0f && MoveDirection != Float3.Zero; }
        }
    }

    /// <summary>
    /// The enemy decision machine.
    ///
    /// It holds no references to the world, to Unity, or to other combatants.
    /// Each tick it is handed an <see cref="AiContext"/> describing what it can
    /// see and handed back an <see cref="AiIntent"/> describing what it wants.
    /// That makes every branch of this state machine testable by constructing a
    /// context directly, with no arena, no physics and no frame timing involved.
    ///
    /// Design notes that matter for feel:
    ///
    ///   * Hysteresis. An enemy that loses sight keeps chasing for
    ///     <see cref="EnemyBrainSettings.LoseSightGrace"/> seconds. Without this,
    ///     stepping behind a pillar would instantly reset a fight.
    ///
    ///   * Reaction time. <see cref="AiState.Alert"/> inserts a delay between
    ///     noticing and acting, which is the player's window to strike first.
    ///
    ///   * Aggro cannot be dodged by approaching from behind. The proximity
    ///     radius in <see cref="PerceptionModel"/> handles close range.
    ///
    ///   * Being hit always alerts, regardless of facing. Implemented by
    ///     <see cref="ForceAlert"/>, which the simulation calls on damage.
    ///
    ///   * Staggering remembers the interrupted state and resumes it, so an
    ///     interrupted charge resumes chasing rather than restarting from idle.
    /// </summary>
    public sealed class EnemyBrain
    {
        private readonly EnemyBrainSettings _settings;
        private readonly DeterministicRng _rng;

        private AiState _stateBeforeStagger;
        private float _timeSinceLastSeen;
        private Float3 _patrolTarget;
        private bool _hasPatrolTarget;
        private bool _pendingAlert;

        public EnemyBrain(EnemyBrainSettings settings, DeterministicRng rng)
        {
            _settings = settings ?? new EnemyBrainSettings();
            _rng = rng ?? new DeterministicRng(1);

            State = AiState.Idle;
            TimeInState = 0f;
            _stateBeforeStagger = AiState.Idle;
            _timeSinceLastSeen = float.MaxValue;
            _hasPatrolTarget = false;
        }

        public AiState State { get; private set; }

        public float TimeInState { get; private set; }

        public float TimeSinceLastSeen
        {
            get { return _timeSinceLastSeen; }
        }

        public EnemyBrainSettings Settings
        {
            get { return _settings; }
        }

        /// <summary>True while the enemy is aware of the player for any reason.</summary>
        public bool IsAlerted
        {
            get { return State == AiState.Alert || State == AiState.Chase || State == AiState.Attack; }
        }

        /// <summary>
        /// Forces the enemy to react on its next tick, ignoring facing and sight.
        /// Called when the enemy takes damage, so a player cannot backstab an
        /// enemy into eternity from outside its vision cone.
        /// </summary>
        public void ForceAlert()
        {
            _pendingAlert = true;
            _timeSinceLastSeen = 0f;
        }

        /// <summary>Resets the brain to a standing watch. Used when an encounter resets.</summary>
        public void Reset()
        {
            State = AiState.Idle;
            TimeInState = 0f;
            _stateBeforeStagger = AiState.Idle;
            _timeSinceLastSeen = float.MaxValue;
            _hasPatrolTarget = false;
            _pendingAlert = false;
        }

        public AiIntent Tick(float deltaTime, in AiContext context)
        {
            var intent = AiIntent.None;

            if (!context.IsAlive)
            {
                State = AiState.Dead;
                TimeInState = 0f;
                return intent;
            }

            if (deltaTime > 0f)
            {
                TimeInState += deltaTime;
            }

            // Staggering overrides everything and remembers what it interrupted.
            if (context.IsStunned)
            {
                if (State != AiState.Staggered)
                {
                    _stateBeforeStagger = IsAlerted ? AiState.Chase : State;
                    TimeInState = 0f;
                }

                State = AiState.Staggered;
                return intent;
            }

            if (State == AiState.Staggered)
            {
                // Resume the interrupted behaviour, not from scratch.
                State = _stateBeforeStagger;
                TimeInState = 0f;
            }

            if (State == AiState.Dead)
            {
                State = AiState.Idle;
                TimeInState = 0f;
            }

            bool perceives = Perceives(context);

            if (perceives)
            {
                _timeSinceLastSeen = 0f;
            }
            else if (_timeSinceLastSeen < float.MaxValue)
            {
                _timeSinceLastSeen += deltaTime;
            }

            if (_pendingAlert && context.HasTarget && context.TargetAlive)
            {
                _pendingAlert = false;
                TransitionTo(AiState.Alert);
                _timeSinceLastSeen = 0f;
            }

            switch (State)
            {
                case AiState.Idle:
                    TickIdle(deltaTime, context, perceives, ref intent);
                    break;
                case AiState.Patrol:
                    TickPatrol(deltaTime, context, perceives, ref intent);
                    break;
                case AiState.Alert:
                    TickAlert(deltaTime, context, perceives, ref intent);
                    break;
                case AiState.Chase:
                    TickChase(deltaTime, context, perceives, ref intent);
                    break;
                case AiState.Attack:
                    TickAttack(deltaTime, context, ref intent);
                    break;
            }

            return intent;
        }

        /// <summary>
        /// Whether the enemy currently senses the target.
        ///
        /// Awareness comes from exactly two places, both inside
        /// <see cref="PerceptionModel"/>: proximity at grappling distance, and
        /// sight within the cone. There is deliberately no third "notices
        /// anything within N units regardless of facing" rule, because that
        /// would silently override both and make the vision cone and the
        /// proximity radius decorative - an enemy would effectively have
        /// perfect 360-degree awareness inside the largest radius in the set.
        /// </summary>
        public bool Perceives(in AiContext context)
        {
            // An alerted enemy tracks further, but still only within line of sight.
            float viewDistance = IsAlerted
                ? _settings.ViewDistance * _settings.AlertedViewMultiplier
                : _settings.ViewDistance;

            var query = new PerceptionQuery(
                context.SelfPosition,
                context.SelfForward,
                context.TargetPosition,
                viewDistance,
                _settings.ViewHalfAngleDegrees,
                _settings.ProximityRadius,
                context.HasTarget,
                context.TargetAlive,
                context.HasLineOfSight);

            return PerceptionModel.CanPerceive(query);
        }

        private void TickIdle(float deltaTime, in AiContext context, bool perceives, ref AiIntent intent)
        {
            if (perceives)
            {
                // Turn to look on the same tick it notices, rather than burning
                // a frame in the new state with an empty intent.
                TransitionTo(AiState.Alert);
                intent.FaceTarget = true;
                return;
            }

            if (ShouldReturnHome(context))
            {
                intent.MoveDirection = (context.HomePosition - context.SelfPosition).FlattenedXZ;
                intent.DesiredSpeed = context.MoveSpeed * _settings.PatrolSpeedMultiplier;
                return;
            }

            if (TimeInState >= _settings.IdleDuration)
            {
                PickPatrolTarget(context.HomePosition);
                TransitionTo(AiState.Patrol);
            }
        }

        private void TickPatrol(float deltaTime, in AiContext context, bool perceives, ref AiIntent intent)
        {
            if (perceives)
            {
                TransitionTo(AiState.Alert);
                intent.FaceTarget = true;
                return;
            }

            if (ShouldReturnHome(context))
            {
                intent.MoveDirection = (context.HomePosition - context.SelfPosition).FlattenedXZ;
                intent.DesiredSpeed = context.MoveSpeed * _settings.PatrolSpeedMultiplier;
                return;
            }

            if (!_hasPatrolTarget)
            {
                PickPatrolTarget(context.HomePosition);
            }

            Float3 toTarget = _patrolTarget - context.SelfPosition;
            float distance = toTarget.Magnitude;

            if (distance <= 0.6f || TimeInState >= _settings.PatrolWaypointTimeout)
            {
                _hasPatrolTarget = false;
                TransitionTo(AiState.Idle);
                return;
            }

            intent.MoveDirection = toTarget.FlattenedXZ;
            intent.DesiredSpeed = context.MoveSpeed * _settings.PatrolSpeedMultiplier;
        }

        private void TickAlert(float deltaTime, in AiContext context, bool perceives, ref AiIntent intent)
        {
            // Stop and stare. This is the player's window to act first.
            intent.FaceTarget = true;

            if (!perceives && _timeSinceLastSeen > _settings.LoseSightGrace)
            {
                TransitionTo(AiState.Idle);
                return;
            }

            if (TimeInState >= _settings.ReactionTime)
            {
                TransitionTo(AiState.Chase);
            }
        }

        private void TickChase(float deltaTime, in AiContext context, bool perceives, ref AiIntent intent)
        {
            intent.FaceTarget = true;

            float distance = Float3.DistanceXZ(context.SelfPosition, context.TargetPosition);

            // Give up only when out of range AND sight has been lost long enough.
            if (distance > _settings.DeaggroRange && _timeSinceLastSeen > _settings.LoseSightGrace)
            {
                TransitionTo(AiState.Idle);
                return;
            }

            if (!perceives && _timeSinceLastSeen > _settings.LoseSightGrace)
            {
                // Lost the player but still close by: sweep back toward home.
                intent.MoveDirection = (context.HomePosition - context.SelfPosition).FlattenedXZ;
                intent.DesiredSpeed = context.MoveSpeed * _settings.PatrolSpeedMultiplier;

                if (Float3.DistanceXZ(context.SelfPosition, context.HomePosition) <= 1f)
                {
                    TransitionTo(AiState.Idle);
                }

                return;
            }

            // The only gate on attacking is whether the combat controller can
            // actually start one. The brain deliberately keeps no attack timer
            // of its own: the ability's windup, recovery and cooldown already
            // define the rhythm, and a second timer here duplicated that and
            // made the enemy swing again the instant its previous swing ended.
            if (distance <= _settings.AttackRange && context.AttackReady)
            {
                intent.WantsToAttack = true;
                intent.FaceTarget = true;
                TransitionTo(AiState.Attack);
                return;
            }

            if (distance > _settings.PreferredRange)
            {
                intent.MoveDirection = (context.TargetPosition - context.SelfPosition).FlattenedXZ;
                intent.DesiredSpeed = context.MoveSpeed * _settings.ChaseSpeedMultiplier;
                return;
            }

            if (distance < _settings.PreferredRange * 0.7f)
            {
                // Too close. Back off so attacks do not overlap in a scrum.
                intent.MoveDirection = (context.SelfPosition - context.TargetPosition).FlattenedXZ;
                intent.DesiredSpeed = context.MoveSpeed * _settings.BackoffSpeedMultiplier;
            }
        }

        private void TickAttack(float deltaTime, in AiContext context, ref AiIntent intent)
        {
            intent.FaceTarget = true;

            if (TimeInState >= _settings.AttackCommitment)
            {
                TransitionTo(AiState.Chase);
            }
        }

        private bool ShouldReturnHome(in AiContext context)
        {
            float distanceFromHome = Float3.DistanceXZ(context.SelfPosition, context.HomePosition);
            return distanceFromHome > _settings.PatrolRadius * 1.5f;
        }

        private void TransitionTo(AiState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            TimeInState = 0f;
        }

        private void PickPatrolTarget(Float3 home)
        {
            float angle = _rng.Range(0f, 360f);
            float distance = _rng.Range(_settings.PatrolRadius * 0.35f, _settings.PatrolRadius);
            _patrolTarget = home + (Combatant.DegreesToDirection(angle) * distance);
            _hasPatrolTarget = true;
        }
    }
}
