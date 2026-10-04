using System;
using System.Collections.Generic;
using Godot;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Content;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.World;

namespace Shadowbound.Game
{
    /// <summary>
    /// The entry point and the assembly of the running game - the Godot
    /// replacement for the project's earlier bootstraps.
    ///
    /// Everything the game needs is created here: content, session, player,
    /// enemies, camera and UI. The scene authored in scenes/Main.tscn holds the
    /// environment, the arena, the view container and the UI anchors; the bodies
    /// are spawned into it, so no binary asset can drift out of step with the
    /// layout the core is tuned against.
    ///
    /// The simulation runs first each physics step: input becomes intent, intent
    /// becomes movement, and blows land at the positions reached. Only then does
    /// anything copy the result onto a transform. Views never write back to the
    /// core.
    /// </summary>
    public partial class GameRoot : Node3D
    {
        private struct SpawnEntry
        {
            public string Archetype;
            public float X;
            public float Z;
            public int Level;
        }

        private static readonly SpawnEntry[] WildsPlan =
        {
            new SpawnEntry { Archetype = GameContent.ArchetypeHollowWalker, X = 8f, Z = 12f, Level = 1 },
            new SpawnEntry { Archetype = GameContent.ArchetypeHollowWalker, X = -9f, Z = 14f, Level = 1 },
            new SpawnEntry { Archetype = GameContent.ArchetypeHollowWalker, X = 0f, Z = 18f, Level = 2 },
            new SpawnEntry { Archetype = GameContent.ArchetypeCinderHound, X = 14f, Z = -6f, Level = 3 },
            new SpawnEntry { Archetype = GameContent.ArchetypeCinderHound, X = -14f, Z = -8f, Level = 3 }
        };

        private static readonly SpawnEntry[] RuinsPlan =
        {
            new SpawnEntry { Archetype = GameContent.ArchetypeHollowWalker, X = 6f, Z = 20f, Level = 4 },
            new SpawnEntry { Archetype = GameContent.ArchetypeHollowWalker, X = -6f, Z = 20f, Level = 4 },
            new SpawnEntry { Archetype = GameContent.ArchetypeVeilwarden, X = 9f, Z = -20f, Level = 6 },
            new SpawnEntry { Archetype = GameContent.ArchetypeVeilwarden, X = -9f, Z = -20f, Level = 6 }
        };

        private static readonly SpawnEntry[] WardPlan =
        {
            new SpawnEntry { Archetype = GameContent.ArchetypeCinderHound, X = 12f, Z = 8f, Level = 7 },
            new SpawnEntry { Archetype = GameContent.ArchetypeCinderHound, X = -12f, Z = 8f, Level = 7 },
            new SpawnEntry { Archetype = GameContent.ArchetypeVeilwarden, X = 0f, Z = -18f, Level = 8 }
        };

        private static readonly SpawnEntry[] SanctumPlan =
        {
            new SpawnEntry { Archetype = GameContent.ArchetypeAshenSentinel, X = 0f, Z = -18f, Level = 10 }
        };

        private static readonly SpawnEntry[] EmptyPlan = Array.Empty<SpawnEntry>();

        [Export] public int Seed = 20250925;
        [Export] public float ArenaHalfExtent = 38f;
        [Export] public string StartingRegion = GameContent.RegionWilds;
        [Export] public string SaveSlot = "slot-1";
        [Export] public bool LoadSaveOnStart = false;
        [Export] public bool AutoSaveEnabled = true;
        [Export] public float AutoSaveIntervalSeconds = 60f;

        public GameSession Session { get; private set; }
        public PlayerInputReader InputReader { get; private set; }
        public PlayerDriver Driver { get; private set; }
        public SaveSlotManager SaveManager { get; private set; }

        private PackedScene _playerScene;
        private PackedScene _enemyScene;

        private Node3D _viewsRoot;
        private CameraRig _cameraRig;
        private Camera3D _camera;
        private Hud _hud;
        private GameMenu _menu;

        private PlayerView _playerView;
        private readonly List<CombatantView> _views = new List<CombatantView>(16);

        private readonly List<CombatantView> _deadViews = new List<CombatantView>(4);
        private readonly List<float> _deadTimers = new List<float>(4);

        private OcclusionProvider _occlusion;
        private EncounterSimulation _encounter;
        private float _autoSaveTimer;
        private bool _gateArmed = true;

        private const float CorpseLingerSeconds = 1.6f;

        public override void _Ready()
        {
            _viewsRoot = GetNode<Node3D>("Views");
            _cameraRig = GetNode<CameraRig>("CameraRig");
            _camera = GetNode<Camera3D>("CameraRig/Camera");
            _hud = GetNode<Hud>("UI/Hud");
            _menu = GetNode<GameMenu>("UI/GameMenu");
            InputReader = GetNode<PlayerInputReader>("InputReader");

            _menu.Root = this;
            _menu.SetOpen(false);

            _playerScene = GD.Load<PackedScene>("res://scenes/Player.tscn");
            _enemyScene = GD.Load<PackedScene>("res://scenes/Enemy.tscn");

            // The key light's angle is part of the arena's look; it is kept in code
            // rather than a hand-written basis so the intent (-48 pitch, 145 yaw)
            // stays readable.
            var keyLight = GetNodeOrNull<DirectionalLight3D>("KeyLight");
            if (keyLight != null)
            {
                keyLight.RotationDegrees = new Vector3(-48f, 145f, 0f);
            }

            if (OS.HasFeature("mobile") || OS.HasFeature("android") || OS.HasFeature("ios"))
            {
                InputReader.DeviceInputEnabled = false;
            }

            Build();

            GD.Print($"Shadowbound ready: region '{Session.RegionId}', " +
                $"{Session.Encounter.HostilesRemaining} hostiles, " +
                $"{Session.Quests.All.Count} quests, level {Session.Progression.Level}");
        }

        /// <summary>Builds a complete playable session. Safe to call again to restart.</summary>
        public void Build()
        {
            BuildSession();
            BuildPlayerView();
            BuildRegionViews();

            _hud.Bind(Session, InputReader, _camera);
            _hud.MenuRequested += _menu.Toggle;

            if (LoadSaveOnStart)
            {
                TryLoad(out _);
            }
        }

        private void BuildSession()
        {
            var items = GameContent.BuildItems();
            Combatant player = GameContent.CreatePlayer();

            _occlusion = new OcclusionProvider(this);

            Session = new GameSession(
                player,
                items,
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng((ulong)Seed),
                WorldBounds.Square(ArenaHalfExtent),
                _occlusion);

            GameContent.Populate(Session);

            SaveManager = new SaveSlotManager(
                new GodotSaveStorage(),
                () => DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            Driver = new PlayerDriver(InputReader);

            // Move out of the safe hub into the first combat region, which also
            // reports the reach objective so the opening quest can complete.
            if (!string.IsNullOrEmpty(StartingRegion))
            {
                Session.EnterRegion(StartingRegion);
            }

            Session.Encounter.AddDriven(
                Session.Player,
                GameContent.BuildPlayerAbilities(),
                Driver,
                isPlayer: true);

            AttachEncounter();

            // The opening quest is offered immediately, so there is always
            // something to do the moment the game starts.
            Session.Quests.TryStart(GameContent.QuestArrival);
        }

        private void BuildPlayerView()
        {
            _playerView = _playerScene.Instantiate<PlayerView>();
            _playerView.Name = "PlayerView";
            _viewsRoot.AddChild(_playerView);

            // The Warden's placeholder tint and scale, from the project's bootstrap.
            _playerView.Bind(Session.Player, new Color(0.86f, 0.78f, 0.62f), 1.05f);

            _views.Add(_playerView);

            Arrive(Session.Player);
            _cameraRig.SetTarget(_playerView);
            _cameraRig.Input = InputReader;
        }

        private void BuildRegionViews()
        {
            PopulateRegion(Session.Encounter, Session.RegionId, ViewPrefix());
        }

        private void PopulateRegion(EncounterSimulation encounter, string regionId, string idPrefix)
        {
            if (encounter == null)
            {
                return;
            }

            SpawnEntry[] plan = PlanFor(regionId);

            for (int i = 0; i < plan.Length; i++)
            {
                SpawnEntry entry = plan[i];

                EnemyArchetype archetype = GameContent.FindArchetype(entry.Archetype);
                if (archetype == null)
                {
                    GD.PushWarning("Spawn plan names unknown archetype '" + entry.Archetype + "'.");
                    continue;
                }

                var position = new Float3(entry.X, 0f, entry.Z);

                // Instance ids are namespaced by region, so the same archetype in two
                // regions gets two independent RNG streams rather than one shared roll.
                Combatant combatant = archetype.Create(idPrefix + entry.Archetype + "-" + i, position, entry.Level);

                encounter.AddEnemy(combatant, archetype.Abilities, archetype.Brain, archetype.AttackAbilityIndex);

                var view = _enemyScene.Instantiate<EnemyView>();
                view.Name = "EnemyView " + combatant.Id;
                _viewsRoot.AddChild(view);

                view.SetBossShape(archetype.IsBoss);
                view.Bind(combatant, ResolveTint(archetype.TintRgb), archetype.BodyScale);

                CombatantView captured = view;
                float shake = archetype.IsBoss ? 0.5f : 0.12f;
                view.Hit += result => _cameraRig.Shake(shake);
                view.Died += () => OnViewDied(captured);

                _views.Add(view);
            }
        }

        private static SpawnEntry[] PlanFor(string regionId)
        {
            if (string.Equals(regionId, GameContent.RegionWilds, StringComparison.Ordinal)) { return WildsPlan; }
            if (string.Equals(regionId, GameContent.RegionRuins, StringComparison.Ordinal)) { return RuinsPlan; }
            if (string.Equals(regionId, GameContent.RegionWard, StringComparison.Ordinal)) { return WardPlan; }
            if (string.Equals(regionId, GameContent.RegionSanctum, StringComparison.Ordinal)) { return SanctumPlan; }

            // The camp and anything unknown spawn nothing.
            return EmptyPlan;
        }

        private static Color ResolveTint(float[] rgb)
        {
            if (rgb == null || rgb.Length < 3)
            {
                return new Color(0.5f, 0.5f, 0.5f);
            }

            return new Color(rgb[0], rgb[1], rgb[2]);
        }

        private string ViewPrefix()
        {
            return (Session?.RegionId ?? string.Empty) + "/";
        }

        // -------------------------------- travel ---------------------------------

        public bool TravelTo(string regionId, out string error)
        {
            error = null;

            if (Session == null)
            {
                error = "There is no game in progress.";
                return false;
            }

            // The travel rule lives in the session: the chapter gate, the adjacency
            // check and the fast-travel allowance are game rules, and rules that
            // live in the view layer cannot be tested.
            if (!Session.TryTravelTo(regionId, out AccessFailure failure))
            {
                error = DescribeAccess(failure);
                return false;
            }

            ulong regionSeed = DeterministicRng.StableHash(regionId) ^ (ulong)Seed;

            var encounter = new EncounterSimulation(
                new DeterministicRng(regionSeed),
                WorldBounds.Square(ArenaHalfExtent),
                _occlusion);

            encounter.AddDriven(Session.Player, GameContent.BuildPlayerAbilities(), Driver, isPlayer: true);

            // Order matters. The old region's bodies are removed first, and only
            // then are the new ones created - clearing afterwards would destroy the
            // very creatures just spawned, leaving an empty region.
            DetachEncounter();
            ClearEnemyViews();
            PopulateRegion(encounter, regionId, regionId + "/");

            Session.SetEncounter(encounter);
            AttachEncounter();

            Arrive(Session.Player);

            _hud.ShowMessage("\u2014 " + regionId + " \u2014");
            return true;
        }

        public bool TravelTo(string regionId)
        {
            return TravelTo(regionId, out _);
        }

        private void Arrive(Combatant player)
        {
            if (player == null)
            {
                return;
            }

            // Walked in from the south, so the region opens up ahead.
            player.SetPosition(new Float3(0f, 0f, -(ArenaHalfExtent - 8f)));
            player.FaceImmediately(new Float3(0f, 0f, 1f));

            _playerView?.ClearFeedback();
            _playerView?.Sync(0f);

            if (_playerView != null && _cameraRig.Target == null)
            {
                _cameraRig.SetTarget(_playerView);
            }

            // The gate is behind the player on arrival, so it must not fire again
            // until they have stepped away and come back.
            _gateArmed = false;
        }

        private static string DescribeAccess(AccessFailure failure)
        {
            switch (failure)
            {
                case AccessFailure.UnknownRegion: return "There is no such place.";
                case AccessFailure.ChapterIncomplete: return "The way is closed for now.";
                case AccessFailure.NotConnected: return "Nothing leads there from here.";
                default: return "You cannot go there yet.";
            }
        }

        private void ClearEnemyViews()
        {
            for (int i = _views.Count - 1; i >= 0; i--)
            {
                CombatantView view = _views[i];

                if (view != null && ReferenceEquals(view.Combatant, Session.Player))
                {
                    continue;
                }

                _views.RemoveAt(i);
                view?.QueueFree();
            }

            _deadViews.Clear();
            _deadTimers.Clear();
        }

        private void OnViewDied(CombatantView view)
        {
            // A boss is the centre of the fight; its body stays. The player is
            // persistent - removing its body would take the camera with it.
            if (view?.Combatant == null || view.Combatant.IsBoss || view.Combatant.IsPersistent)
            {
                return;
            }

            _deadViews.Add(view);
            _deadTimers.Add(CorpseLingerSeconds);
        }

        // -------------------------------- gate -----------------------------------

        private void CheckGate()
        {
            if (Session?.Player == null)
            {
                return;
            }

            Float3 position = Session.Player.Position;
            float gateZ = ArenaHalfExtent - 6f;
            float dx = position.X;
            float dz = position.Z - gateZ;

            if ((dx * dx) + (dz * dz) > 12.25f)
            {
                _gateArmed = true;
                return;
            }

            if (!_gateArmed)
            {
                return;
            }

            string destination = NextRegionFrom(Session.RegionId);
            if (destination == null)
            {
                return;
            }

            _gateArmed = false;

            if (!TravelTo(destination, out string error))
            {
                GD.Print("Shadowbound: the gate would not open. " + error);
            }
        }

        private string NextRegionFrom(string regionId)
        {
            IReadOnlyList<string> neighbours = Session.World.Neighbours(regionId);
            string fallback = null;

            for (int i = 0; i < neighbours.Count; i++)
            {
                string neighbour = neighbours[i];

                if (string.IsNullOrEmpty(neighbour) || string.Equals(neighbour, regionId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (Session.World.CanEnter(neighbour, Session.Chapters) != AccessFailure.None)
                {
                    continue;
                }

                if (!string.Equals(neighbour, GameContent.RegionCamp, StringComparison.Ordinal))
                {
                    return neighbour;
                }

                if (fallback == null)
                {
                    fallback = neighbour;
                }
            }

            return fallback;
        }

        // --------------------------------- saving --------------------------------

        public bool TrySave(out string error)
        {
            error = null;

            if (Session == null)
            {
                error = "There is no game in progress.";
                return false;
            }

            Session.SlotId = SaveSlot;
            SaveGame save = Session.CreateSave();
            SaveResult result = SaveManager.Save(save);

            if (!result.Success)
            {
                error = result.Error;
                return false;
            }

            return true;
        }

        public bool TryLoad(out string error)
        {
            error = null;

            if (Session == null)
            {
                error = "There is no game in progress.";
                return false;
            }

            SaveResult result = SaveManager.Load(SaveSlot, out SaveGame save);

            if (!result.Success || save == null)
            {
                error = result.Error ?? "There is no save in that slot.";
                return false;
            }

            Session.ApplySave(save);
            Arrive(Session.Player);
            return true;
        }

        // -------------------------------- frame loop -----------------------------

        public override void _Process(double deltaSeconds)
        {
            if (Input.IsActionJustPressed("menu") && _menu != null)
            {
                _menu.Toggle();
            }
        }

        public override void _PhysicsProcess(double deltaSeconds)
        {
            if (Session == null)
            {
                return;
            }

            float delta = (float)deltaSeconds;
            bool paused = _menu != null && _menu.IsOpen;

            if (_hud != null)
            {
                _hud.InputEnabled = !paused;
            }

            if (paused)
            {
                return;
            }

            InputReader.Poll(delta);
            Session.Update(delta);

            SyncViews(delta);
            ExpireDeadViews(delta);
            CheckGate();
            TickAutoSave(delta);
        }

        private void SyncViews(float delta)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                _views[i].Sync(delta);
            }
        }

        private void ExpireDeadViews(float delta)
        {
            for (int i = _deadViews.Count - 1; i >= 0; i--)
            {
                _deadTimers[i] -= delta;

                if (_deadTimers[i] > 0f)
                {
                    continue;
                }

                CombatantView view = _deadViews[i];
                _views.Remove(view);
                view.QueueFree();

                _deadViews.RemoveAt(i);
                _deadTimers.RemoveAt(i);
            }
        }

        private void TickAutoSave(float delta)
        {
            if (!AutoSaveEnabled || Session == null)
            {
                return;
            }

            _autoSaveTimer += delta;

            if (_autoSaveTimer < AutoSaveIntervalSeconds)
            {
                return;
            }

            _autoSaveTimer = 0f;
            TrySave(out _);
        }

        // ------------------------------ encounter events -------------------------

        private void AttachEncounter()
        {
            _encounter = Session.Encounter;
            _encounter.DamageDealt += OnDamageDealt;
            _encounter.Died += OnEncounterDied;
        }

        private void DetachEncounter()
        {
            if (_encounter == null)
            {
                return;
            }

            _encounter.DamageDealt -= OnDamageDealt;
            _encounter.Died -= OnEncounterDied;
            _encounter = null;
        }

        private void OnDamageDealt(Participant attacker, Combatant victim, DamageResult result)
        {
            if (victim == null || result.Applied <= 0f || _hud == null)
            {
                return;
            }

            Float3 position = victim.Position;
            _hud.ReportDamage(new Vector3(position.X, position.Y + 1.8f, position.Z), result.Applied, result.Critical);
        }

        private void OnEncounterDied(Combatant victim, Participant participant)
        {
            // The session grants loot, experience and quest progress; the HUD only
            // announces a boss falling, because that is a moment worth reading.
            if (victim != null && victim.IsBoss)
            {
                _hud?.ShowMessage(victim.DisplayName + " falls");
            }
        }

        public override void _ExitTree()
        {
            DetachEncounter();
        }
    }
}
