using System;
using Godot;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Content;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.Simulation;

namespace Shadowbound.Tests
{
    /// <summary>
    /// A headless end-to-end check that runs inside Godot.
    ///
    /// It exercises the engine-free core through the engine: the deterministic
    /// RNG must keep its exact parity, a session must boot, a fight must actually
    /// resolve, and a save must round-trip. Run with:
    ///
    ///     godot --headless --path . res://Tests/Godot/GodotSmoke.tscn
    ///
    /// Exits 0 when every check passes and 1 otherwise, so CI can classify on it.
    /// </summary>
    public partial class GodotSmoke : Node
    {
        private int _failures;

        private sealed class AggressiveDriver : ICombatantDriver
        {
            public CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world)
            {
                CombatIntent intent = CombatIntent.None();

                Combatant target = world.FindNearestHostile(self, 60f);
                if (target == null)
                {
                    intent.MoveDirection = new Float3(0f, 0f, 1f);
                    intent.SpeedScale = 1f;
                    return intent;
                }

                intent.MoveDirection = (target.Position - self.Position).FlattenedXZ;
                intent.SpeedScale = 1f;
                intent.ActivateAbility = true;
                intent.AbilityIndex = 0;
                return intent;
            }
        }

        public override void _Ready()
        {
            CheckRngParity();
            CheckSessionBootsAndFights();
            CheckSaveRoundTrip();

            if (_failures == 0)
            {
                GD.Print("godot-smoke: OK (all checks passed)");
                GetTree().Quit(0);
            }
            else
            {
                GD.PrintErr($"godot-smoke: FAIL ({_failures} check(s) failed)");
                GetTree().Quit(1);
            }
        }

        private void Check(bool condition, string label)
        {
            if (condition)
            {
                GD.Print("  ok   - " + label);
                return;
            }

            _failures++;
            GD.PrintErr("  FAIL - " + label);
        }

        // ---------------------------------------------------------------- rng ---

        private void CheckRngParity()
        {
            var rng = new DeterministicRng(42);
            Check(rng.NextUInt() == 492690617u, "DeterministicRng(42) uint #1");
            Check(rng.NextUInt() == 1919685028u, "DeterministicRng(42) uint #2");
            Check(rng.NextUInt() == 3561993920u, "DeterministicRng(42) uint #3");

            var floatRng = new DeterministicRng(42);
            Check(Math.Abs(floatRng.NextFloat() - 0.1147134f) < 1e-5f, "DeterministicRng(42) first float");

            Check(DeterministicRng.StableHash("a") == 0xaf63dc4c8601ec8cUL, "StableHash(\"a\")");
        }

        // ------------------------------------------------------------ session ---

        private GameSession BuildSession()
        {
            var items = GameContent.BuildItems();
            Combatant player = GameContent.CreatePlayer();

            var session = new GameSession(
                player,
                items,
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(20250925),
                WorldBounds.Square(38f));

            GameContent.Populate(session);
            session.EnterRegion(GameContent.RegionWilds);
            session.Quests.TryStart(GameContent.QuestArrival);
            return session;
        }

        private void CheckSessionBootsAndFights()
        {
            GameSession session = BuildSession();
            Check(session.World.Count == 5, "world graph has five regions");

            session.Encounter.AddDriven(
                session.Player,
                GameContent.BuildPlayerAbilities(),
                new AggressiveDriver(),
                isPlayer: true);

            // The Grey Wilds' opening encounter, from the content's spawn plan.
            SpawnRegion(session, GameContent.RegionWilds);

            Check(session.Encounter.HostilesRemaining == 5, "wilds starts with five hostiles");

            float elapsed = 0f;
            while (elapsed < 90f && session.Encounter.HostilesRemaining > 0)
            {
                session.Update(1f / 60f);
                elapsed += 1f / 60f;
            }

            Check(session.Encounter.HostilesRemaining < 5, "a fight actually resolved (hostiles fell)");

            // The driver above is deliberately naive (it charges every hostile at
            // once), so the meaningful invariant is that the encounter reached a
            // conclusion, not that the Warden won.
            Check(!session.Player.IsAlive || session.Encounter.HostilesRemaining == 0,
                "the opening fight reached a conclusion");
            Check(session.Player.Vitals.Health < session.Player.Vitals.MaxHealth, "the Warden took damage");
            Check(session.Progression.TotalExperience > 0, "defeats granted experience");

            GD.Print($"  info - outcome: hostiles {session.Encounter.HostilesRemaining}, warden alive {session.Player.IsAlive}, xp {session.Progression.TotalExperience}");
        }

        private static void SpawnRegion(GameSession session, string regionId)
        {
            (string Archetype, float X, float Z, int Level)[] plan =
            {
                (GameContent.ArchetypeHollowWalker, 8f, 12f, 1),
                (GameContent.ArchetypeHollowWalker, -9f, 14f, 1),
                (GameContent.ArchetypeHollowWalker, 0f, 18f, 2),
                (GameContent.ArchetypeCinderHound, 14f, -6f, 3),
                (GameContent.ArchetypeCinderHound, -14f, -8f, 3)
            };

            for (int i = 0; i < plan.Length; i++)
            {
                EnemyArchetype archetype = GameContent.FindArchetype(plan[i].Archetype);
                if (archetype == null)
                {
                    continue;
                }

                var position = new Float3(plan[i].X, 0f, plan[i].Z);
                Combatant combatant = archetype.Create(regionId + "/" + plan[i].Archetype + "-" + i, position, plan[i].Level);
                session.Encounter.AddEnemy(combatant, archetype.Abilities, archetype.Brain, archetype.AttackAbilityIndex);
            }
        }

        // -------------------------------------------------------------- saves ---

        private void CheckSaveRoundTrip()
        {
            GameSession session = BuildSession();
            session.Player.SetPosition(new Float3(1f, 0f, 2f));
            session.GrantItem(GameContent.ItemAsh, 7);
            session.Progression.AddExperience(500);

            string json = SaveSerializer.Serialize(session.CreateSave());
            Check(!string.IsNullOrEmpty(json), "save serialises to JSON");

            Check(SaveSerializer.TryDeserialize(json, out SaveGame loaded, out string error),
                "save deserialises" + (error == null ? "" : " (" + error + ")"));

            GameSession restored = BuildSession();
            restored.ApplySave(loaded);

            Check(restored.Progression.TotalExperience == session.Progression.TotalExperience, "experience survives a save round-trip");
            Check(restored.Inventory.Count(GameContent.ItemAsh) == 7, "inventory survives a save round-trip");
            Check(restored.Player.Position.X == session.Player.Position.X, "position survives a save round-trip");
        }
    }
}
