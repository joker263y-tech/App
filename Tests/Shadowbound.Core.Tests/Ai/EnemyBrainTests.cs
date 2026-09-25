using Shadowbound.Core.Ai;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Randomness;
using Xunit;

namespace Shadowbound.Core.Tests.Ai
{
    public class EnemyBrainTests
    {
        private static readonly Float3 Origin = Float3.Zero;
        private static readonly Float3 Forward = new Float3(0f, 0f, 1f);

        private static EnemyBrain MakeBrain(EnemyBrainSettings settings = null)
        {
            return new EnemyBrain(settings, new DeterministicRng(12345));
        }

        private static AiContext Ctx(
            Float3 target,
            Float3 self = default(Float3),
            Float3 home = default(Float3),
            bool hasTarget = true,
            bool targetAlive = true,
            bool lineOfSight = true,
            bool isAlive = true,
            bool isStunned = false,
            bool attackReady = true,
            float moveSpeed = 5f)
        {
            return new AiContext(
                self,
                Forward,
                home,
                hasTarget,
                target,
                targetAlive,
                lineOfSight,
                isAlive,
                isStunned,
                attackReady,
                moveSpeed);
        }

        /// <summary>Runs the brain forward in fixed steps until it reaches a state or runs out of patience.</summary>
        private static void AdvanceUntilState(EnemyBrain brain, in AiContext context, AiState wanted, float maxSeconds = 5f)
        {
            const float step = 0.1f;
            float elapsed = 0f;

            while (elapsed < maxSeconds && brain.State != wanted)
            {
                brain.Tick(step, context);
                elapsed += step;
            }

            Assert.Equal(wanted, brain.State);
        }

        private static void TickFor(EnemyBrain brain, in AiContext context, float seconds, float step = 0.1f)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                brain.Tick(step, context);
                elapsed += step;
            }
        }

        // ------------------------------- basic states ------------------------------

        [Fact]
        public void NewBrain_StartsIdle()
        {
            Assert.Equal(AiState.Idle, MakeBrain().State);
        }

        [Fact]
        public void DeadBrain_EntersDeadStateAndDoesNothing()
        {
            EnemyBrain brain = MakeBrain();

            AiIntent intent = brain.Tick(0.1f, Ctx(new Float3(0f, 0f, 5f), isAlive: false));

            Assert.Equal(AiState.Dead, brain.State);
            Assert.False(intent.WantsToAttack);
            Assert.False(intent.IsMoving);
        }

        [Fact]
        public void NoTarget_StaysIdle()
        {
            EnemyBrain brain = MakeBrain();

            TickFor(brain, Ctx(Origin, hasTarget: false), 1f);

            Assert.Equal(AiState.Idle, brain.State);
        }

        [Fact]
        public void DeadTarget_IsNeverPerceived()
        {
            EnemyBrain brain = MakeBrain();

            TickFor(brain, Ctx(new Float3(0f, 0f, 2f), targetAlive: false), 1f);

            Assert.Equal(AiState.Idle, brain.State);
        }

        // -------------------------------- perception -------------------------------

        [Fact]
        public void TargetAheadInSight_TriggersAlert()
        {
            EnemyBrain brain = MakeBrain();

            AiIntent intent = brain.Tick(0.1f, Ctx(new Float3(0f, 0f, 8f)));

            Assert.Equal(AiState.Alert, brain.State);
            // Alert means stop and look, not charge.
            Assert.True(intent.FaceTarget);
            Assert.False(intent.IsMoving);
        }

        [Fact]
        public void TargetBehindAWall_IsNotNoticed()
        {
            EnemyBrain brain = MakeBrain();

            TickFor(brain, Ctx(new Float3(0f, 0f, 8f), lineOfSight: false), 2f);

            Assert.Equal(AiState.Idle, brain.State);
        }

        [Fact]
        public void TargetDirectlyBehindAtDistance_IsNotNoticed()
        {
            // Distance 5 is outside the proximity radius and behind the cone.
            EnemyBrain brain = MakeBrain();

            TickFor(brain, Ctx(new Float3(0f, 0f, -5f)), 2f);

            Assert.Equal(AiState.Idle, brain.State);
        }

        [Fact]
        public void TargetBehindButAdjacent_IsNoticed()
        {
            // Proximity sensing ignores facing, so standing behind an enemy at
            // grappling distance does not make the player invisible.
            EnemyBrain brain = MakeBrain();

            brain.Tick(0.1f, Ctx(new Float3(0f, 0f, -2f)));

            Assert.Equal(AiState.Alert, brain.State);
        }

        [Fact]
        public void ForceAlert_MakesTheEnemyReactDespiteFacingAway()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, -6f));

            brain.ForceAlert();
            brain.Tick(0.1f, context);

            Assert.Equal(AiState.Alert, brain.State);
        }

        [Fact]
        public void ForceAlert_WithoutATargetStillDoesNothing()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(Origin, hasTarget: false);

            brain.ForceAlert();
            TickFor(brain, context, 1f);

            Assert.Equal(AiState.Idle, brain.State);
        }

        // --------------------------------- reactions -------------------------------

        [Fact]
        public void Alert_WaitsForTheReactionTimeBeforeChasing()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, 8f));

            TickFor(brain, context, 0.4f);
            Assert.Equal(AiState.Alert, brain.State);

            TickFor(brain, context, 0.3f);
            Assert.Equal(AiState.Chase, brain.State);
        }

        [Fact]
        public void Chase_MovesTowardADistantTarget()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, 10f));
            AdvanceUntilState(brain, context, AiState.Chase);

            AiIntent intent = brain.Tick(0.1f, context);

            Assert.True(intent.IsMoving);
            Assert.Equal(5f, intent.DesiredSpeed, 3);
            Assert.Equal(new Float3(0f, 0f, 1f), intent.MoveDirection);
        }

        [Fact]
        public void Chase_RequestsAnAttackOnceInRange()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, 2f));
            AdvanceUntilState(brain, context, AiState.Chase);

            AiIntent intent = brain.Tick(0.1f, context);

            Assert.True(intent.WantsToAttack);
            Assert.Equal(AiState.Attack, brain.State);
        }

        [Fact]
        public void Attack_DoesNotStartWhileTheAbilityIsUnavailable()
        {
            EnemyBrain brain = MakeBrain();
            AiContext inRange = Ctx(new Float3(0f, 0f, 2f));
            AdvanceUntilState(brain, inRange, AiState.Chase);

            AiIntent intent = brain.Tick(0.1f, Ctx(new Float3(0f, 0f, 2f), attackReady: false));

            Assert.False(intent.WantsToAttack);
            Assert.Equal(AiState.Chase, brain.State);
        }

        [Fact]
        public void Attack_ReturnsToChaseAfterItsCommitment()
        {
            EnemyBrain brain = MakeBrain();
            AiContext inRange = Ctx(new Float3(0f, 0f, 2f));
            AdvanceUntilState(brain, inRange, AiState.Chase);
            brain.Tick(0.1f, inRange);
            Assert.Equal(AiState.Attack, brain.State);

            // While the swing plays out the combat controller is busy, so the
            // enemy is not able to start another one yet.
            TickFor(brain, Ctx(new Float3(0f, 0f, 2f), attackReady: false), 1.2f);

            Assert.Equal(AiState.Chase, brain.State);
        }

        [Fact]
        public void Attack_DoesNotRestartWhileTheAbilityIsUnavailable()
        {
            EnemyBrain brain = MakeBrain();
            AiContext inRange = Ctx(new Float3(0f, 0f, 2f));
            AdvanceUntilState(brain, inRange, AiState.Chase);
            brain.Tick(0.1f, inRange);

            // Still in range after the commitment, but the ability is on
            // cooldown, so it must not swing again.
            AiContext notReady = Ctx(new Float3(0f, 0f, 2f), attackReady: false);
            TickFor(brain, notReady, 1.5f);
            AiIntent intent = brain.Tick(0.1f, notReady);

            Assert.False(intent.WantsToAttack);
            Assert.NotEqual(AiState.Attack, brain.State);
        }

        [Fact]
        public void Chase_BacksOffWhenTooCloseAndUnableToAttack()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, 2f));
            AdvanceUntilState(brain, context, AiState.Chase);

            AiIntent intent = brain.Tick(0.1f, Ctx(new Float3(0f, 0f, 0.5f), attackReady: false));

            Assert.True(intent.IsMoving);
            // Backing away means moving along -Z, away from a target at +Z.
            Assert.Equal(-1f, intent.MoveDirection.Z, 3);
        }

        // -------------------------------- hysteresis -------------------------------

        [Fact]
        public void Chase_KeepsPursuingBrieflyAfterLosingSight()
        {
            // Without this grace period, stepping behind a pillar would reset a
            // fight instantly.
            EnemyBrain brain = MakeBrain();
            AiContext visible = Ctx(new Float3(0f, 0f, 5f));
            AdvanceUntilState(brain, visible, AiState.Chase);

            AiContext hidden = Ctx(new Float3(0f, 0f, 20f), lineOfSight: false);
            TickFor(brain, hidden, 3f);

            Assert.Equal(AiState.Chase, brain.State);
        }

        [Fact]
        public void Chase_GivesUpOnceSightHasBeenLostForTooLong()
        {
            EnemyBrain brain = MakeBrain();
            AiContext visible = Ctx(new Float3(0f, 0f, 5f));
            AdvanceUntilState(brain, visible, AiState.Chase);

            AiContext hidden = Ctx(new Float3(0f, 0f, 20f), lineOfSight: false);
            TickFor(brain, hidden, 6f);

            Assert.Equal(AiState.Idle, brain.State);
        }

        [Fact]
        public void Chase_KeepsFightingWhileTheTargetStaysVisible()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, 6f));
            AdvanceUntilState(brain, context, AiState.Chase);

            TickFor(brain, context, 10f);

            Assert.True(brain.IsAlerted);
        }

        // -------------------------------- staggering -------------------------------

        [Fact]
        public void Stunned_EntersStaggeredAndStopsActing()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, 2f));
            AdvanceUntilState(brain, context, AiState.Chase);

            AiIntent intent = brain.Tick(0.1f, Ctx(new Float3(0f, 0f, 2f), isStunned: true));

            Assert.Equal(AiState.Staggered, brain.State);
            Assert.False(intent.IsMoving);
            Assert.False(intent.WantsToAttack);
        }

        [Fact]
        public void Staggered_ResumesChasingRatherThanRestartingFromIdle()
        {
            // An interrupted charge should resume, not forget the player.
            // Distance 5 keeps it out of attack range so the resumed state is
            // unambiguous.
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, 5f));
            AdvanceUntilState(brain, context, AiState.Chase);

            brain.Tick(0.1f, Ctx(new Float3(0f, 0f, 5f), isStunned: true));
            Assert.Equal(AiState.Staggered, brain.State);

            brain.Tick(0.1f, Ctx(new Float3(0f, 0f, 5f), isStunned: false));

            Assert.Equal(AiState.Chase, brain.State);
        }

        [Fact]
        public void Staggered_PatrollingEnemyResumesPatrolling()
        {
            EnemyBrain brain = MakeBrain(new EnemyBrainSettings { IdleDuration = 0.2f });
            AiContext noTarget = Ctx(Origin, hasTarget: false);
            AdvanceUntilState(brain, noTarget, AiState.Patrol);

            brain.Tick(0.1f, Ctx(Origin, hasTarget: false, isStunned: true));
            Assert.Equal(AiState.Staggered, brain.State);

            brain.Tick(0.1f, Ctx(Origin, hasTarget: false, isStunned: false));

            Assert.Equal(AiState.Patrol, brain.State);
        }

        // ---------------------------------- patrol ---------------------------------

        [Fact]
        public void Idle_BeginsPatrollingAfterItsIdleDuration()
        {
            EnemyBrain brain = MakeBrain(new EnemyBrainSettings { IdleDuration = 0.5f });
            AiContext context = Ctx(Origin, hasTarget: false);

            TickFor(brain, context, 0.6f);
            Assert.Equal(AiState.Patrol, brain.State);
        }

        [Fact]
        public void Patrol_MovesTowardItsWaypoint()
        {
            EnemyBrain brain = MakeBrain(new EnemyBrainSettings { IdleDuration = 0.2f });
            AiContext context = Ctx(Origin, hasTarget: false);

            AdvanceUntilState(brain, context, AiState.Patrol);
            AiIntent intent = brain.Tick(0.1f, context);
            TickFor(brain, context, 0f);

            AiIntent moving = brain.State == AiState.Patrol ? intent : brain.Tick(0.1f, context);

            Assert.True(moving.IsMoving);
            Assert.True(moving.DesiredSpeed > 0f);
        }

        [Fact]
        public void Patrol_StaysNearItsPost()
        {
            EnemyBrain brain = MakeBrain(new EnemyBrainSettings { IdleDuration = 0.2f, PatrolRadius = 4f });
            var home = new Float3(0f, 0f, 0f);
            AiContext context = Ctx(Origin, home: home, hasTarget: false);

            for (int i = 0; i < 200; i++)
            {
                AiIntent intent = brain.Tick(0.1f, context);
                if (intent.IsMoving)
                {
                    // The simulation would move the enemy; emulate that here.
                    context = Ctx(
                        Origin,
                        self: context.SelfPosition + (intent.MoveDirection * intent.DesiredSpeed * 0.1f),
                        home: home,
                        hasTarget: false);
                }
            }

            float distanceFromHome = Float3.DistanceXZ(context.SelfPosition, home);

            // 1.5x the patrol radius is the documented leash.
            Assert.True(distanceFromHome <= 4f * 1.5f + 0.5f,
                "An idle enemy should not wander far from its post.");
        }

        // ---------------------------------- reset ----------------------------------

        [Fact]
        public void Reset_ReturnsTheBrainToIdle()
        {
            EnemyBrain brain = MakeBrain();
            AiContext context = Ctx(new Float3(0f, 0f, 5f));
            AdvanceUntilState(brain, context, AiState.Chase);

            brain.Reset();

            Assert.Equal(AiState.Idle, brain.State);
            Assert.Equal(0f, brain.TimeInState);
        }

        [Fact]
        public void SameSeed_ProducesTheSamePatrolRoute()
        {
            var settings = new EnemyBrainSettings { IdleDuration = 0.2f };
            EnemyBrain first = new EnemyBrain(settings, new DeterministicRng(99));
            EnemyBrain second = new EnemyBrain(settings, new DeterministicRng(99));

            AiContext context = Ctx(Origin, hasTarget: false);
            for (int i = 0; i < 100; i++)
            {
                AiIntent a = first.Tick(0.1f, context);
                AiIntent b = second.Tick(0.1f, context);

                Assert.Equal(a.MoveDirection, b.MoveDirection);
                Assert.Equal(a.DesiredSpeed, b.DesiredSpeed, 5);
            }
        }
    }
}
