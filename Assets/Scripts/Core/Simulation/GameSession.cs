using System;
using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Items;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.World;

namespace Shadowbound.Core.Simulation
{
    /// <summary>
    /// One playthrough, in progress.
    ///
    /// This is the layer that makes the individual systems one game. It owns the
    /// player, the RPG systems, the world and the running encounter, and it is the
    /// only place that knows how defeating an enemy turns into experience, loot
    /// and quest progress.
    ///
    /// Keeping that wiring here - rather than spread across combat, loot and quest
    /// code - is what allows the whole chain to be asserted in one test: kill an
    /// enemy, and check that the journal moved, the experience went up and the
    /// drops landed in the bag.
    ///
    /// Encounters themselves are deliberately not persisted. A save records the
    /// player's progress and location; the enemies for a region are respawned when
    /// it is entered. That keeps saves small and matches how the genre behaves.
    /// </summary>
    public sealed class GameSession
    {
        private readonly Dictionary<string, LootTable> _lootTables;
        private readonly List<ItemStack> _lootBuffer;

        public GameSession(
            Combatant player,
            ItemDatabase items,
            ExperienceCurve curve,
            StatGrowth[] growth,
            DeterministicRng rng,
            WorldBounds bounds = default(WorldBounds),
            IOcclusionProvider occlusion = null)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Items = items ?? throw new ArgumentNullException(nameof(items));

            Inventory = new Inventory(Items);
            Equipment = new EquipmentLoadout(Player, Items);
            Quests = new QuestLog();
            Chapters = new ChapterTracker(Quests);
            World = new WorldGraph();
            Progression = new ProgressionSystem(Player, curve ?? new ExperienceCurve(), growth);

            _lootTables = new Dictionary<string, LootTable>(StringComparer.Ordinal);
            _lootBuffer = new List<ItemStack>(8);

            Encounter = new EncounterSimulation(rng, bounds, occlusion);
            Encounter.Died += OnEncounterDeath;
        }

        public Combatant Player { get; private set; }

        public ItemDatabase Items { get; private set; }

        public Inventory Inventory { get; private set; }

        public EquipmentLoadout Equipment { get; private set; }

        public QuestLog Quests { get; private set; }

        public ChapterTracker Chapters { get; private set; }

        public WorldGraph World { get; private set; }

        public ProgressionSystem Progression { get; private set; }

        public EncounterSimulation Encounter { get; private set; }

        /// <summary>
        /// The single random source for the session, which is the encounter's own
        /// generator.
        ///
        /// Loot and combat deliberately share one stream. Keeping two would mean
        /// the saved state described only one of them, so loading a save would let
        /// combat rolls replay values that loot had already consumed, and the two
        /// would silently diverge from the run they were meant to reproduce.
        /// </summary>
        public DeterministicRng Rng
        {
            get { return Encounter.Rng; }
        }

        // ------------------------------- save metadata -----------------------------

        public string SlotId { get; set; } = "slot-1";

        public string ProfileName { get; set; } = "Wanderer";

        public string DifficultyId { get; set; } = "wanderer";

        /// <summary>Region the player is currently in.</summary>
        public string RegionId { get; set; } = "";

        public List<string> DiscoveredRegions { get; private set; } = new List<string>();

        public float PlaytimeSeconds { get; set; }

        // ---------------------------------- events --------------------------------

        /// <summary>Raised for each item stack that actually entered the bag.</summary>
        public event Action<ItemStack> LootGranted;

        /// <summary>Raised for every hostile defeated, before rewards are applied.</summary>
        public event Action<Combatant> EnemyDefeated;

        /// <summary>Raised with the number of levels gained from experience.</summary>
        public event Action<int> LevelledUp;

        // -------------------------------- configuration ---------------------------

        public void RegisterLootTable(LootTable table)
        {
            if (table == null || string.IsNullOrEmpty(table.Id))
            {
                return;
            }

            _lootTables[table.Id] = table;
        }

        /// <summary>Replaces the encounter, for moving to a new region. Subscriptions are rewired.</summary>
        public void SetEncounter(EncounterSimulation encounter)
        {
            if (encounter == null)
            {
                return;
            }

            Encounter.Died -= OnEncounterDeath;
            Encounter = encounter;
            Encounter.Died += OnEncounterDeath;
        }

        /// <summary>Advances the running encounter.</summary>
        public int Update(float deltaTime)
        {
            int steps = Encounter.Update(deltaTime);

            if (steps > 0)
            {
                PlaytimeSeconds += deltaTime;
            }

            return steps;
        }

        // ------------------------------ reward plumbing ---------------------------

        /// <summary>
        /// Applies everything that follows from defeating a combatant: loot, then
        /// experience, then quest progress, then chapter re-evaluation.
        ///
        /// Loot is granted before experience so that a level-up gained from the kill
        /// cannot change the loot roll for that same kill.
        /// </summary>
        public void ReportDefeat(Combatant victim)
        {
            if (victim == null || victim.Faction != Faction.Hostile)
            {
                return;
            }

            EnemyDefeated?.Invoke(victim);

            GrantLoot(victim);

            int levels = Progression.AddExperience(victim.ExperienceReward);
            if (levels > 0)
            {
                LevelledUp?.Invoke(levels);
            }

            if (!string.IsNullOrEmpty(victim.ArchetypeId))
            {
                Quests.Report(QuestEvent.Kill(victim.ArchetypeId));

                if (victim.IsBoss)
                {
                    Quests.Report(QuestEvent.DefeatBoss(victim.ArchetypeId));
                }
            }

            Chapters.Refresh();
        }

        /// <summary>Rolls a victim's loot table and puts what it yields into the bag.</summary>
        public int GrantLoot(Combatant victim)
        {
            if (victim == null || string.IsNullOrEmpty(victim.LootTableId))
            {
                return 0;
            }

            if (!_lootTables.TryGetValue(victim.LootTableId, out LootTable table))
            {
                return 0;
            }

            table.Roll(Rng, Progression.Level, 0f, _lootBuffer);

            int granted = 0;
            for (int i = 0; i < _lootBuffer.Count; i++)
            {
                granted += GrantItem(_lootBuffer[i].ItemId, _lootBuffer[i].Quantity);
            }

            return granted;
        }

        /// <summary>
        /// Adds items to the bag and syncs any collection objectives.
        ///
        /// Items that will not fit are dropped rather than queued: the return value
        /// reports how many were accepted, so the Unity layer can leave a world
        /// pickup on the ground for the player to come back for.
        /// </summary>
        public int GrantItem(string itemId, int quantity)
        {
            int accepted = Inventory.Add(itemId, quantity);

            if (accepted <= 0)
            {
                return 0;
            }

            LootGranted?.Invoke(new ItemStack(itemId, accepted));
            SyncCollectionObjectives();
            return accepted;
        }

        /// <summary>Applies a quest reward: experience, attribute points and items.</summary>
        public void GrantReward(in QuestReward reward)
        {
            int levels = Progression.AddExperience(reward.Experience);
            if (levels > 0)
            {
                LevelledUp?.Invoke(levels);
            }

            if (reward.AttributePoints > 0)
            {
                Progression.GrantAttributePoints(reward.AttributePoints);
            }

            ItemStack[] items = reward.Items;
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                GrantItem(items[i].ItemId, items[i].Quantity);
            }
        }

        /// <summary>
        /// Pulls every active collection objective's progress from the inventory.
        ///
        /// Collection objectives track what the player currently holds rather than
        /// a tally of pickups, so they must be re-synced whenever the bag changes.
        /// Reporting pickups cumulatively would let a player pick an item up and
        /// drop it repeatedly to finish the quest.
        /// </summary>
        public void SyncCollectionObjectives()
        {
            IReadOnlyList<QuestState> all = Quests.All;

            for (int i = 0; i < all.Count; i++)
            {
                QuestState state = all[i];
                if (state.Status != QuestStatus.Active)
                {
                    continue;
                }

                ObjectiveDefinition[] objectives = state.Definition.Objectives;

                for (int j = 0; j < objectives.Length; j++)
                {
                    ObjectiveDefinition objective = objectives[j];
                    if (objective == null || objective.Kind != ObjectiveKind.Collect)
                    {
                        continue;
                    }

                    Quests.SetProgress(state.Definition.Id, objective.Id, Inventory.Count(objective.TargetId));
                }
            }
        }

        // -------------------------------- collection ------------------------------

        /// <summary>True when the encounter has no living hostiles left.</summary>
        public bool EncounterCleared
        {
            get { return Encounter.HostilesRemaining <= 0; }
        }

        /// <summary>Records that the player has reached a region, and reports it to quests.</summary>
        public bool EnterRegion(string regionId)
        {
            AccessFailure access = World.CanEnter(regionId, Chapters);
            if (access != AccessFailure.None)
            {
                return false;
            }

            RegionId = regionId;

            if (!DiscoveredRegions.Contains(regionId))
            {
                DiscoveredRegions.Add(regionId);
            }

            Quests.Report(QuestEvent.Reach(regionId));
            Chapters.Refresh();
            return true;
        }

        // --------------------------------- saving ---------------------------------

        public SaveGame CreateSave()
        {
            var save = new SaveGame
            {
                SlotId = SlotId,
                ProfileName = ProfileName,
                DifficultyId = DifficultyId,
                Version = SaveGame.CurrentVersion,
                PlaytimeSeconds = PlaytimeSeconds,
                RegionId = RegionId,
                Position = Player.Position,
                FacingDegrees = Player.FacingDegrees,
                TotalExperience = Progression.TotalExperience,
                UnspentAttributePoints = Progression.UnspentAttributePoints,
                Inventory = Inventory.ToStacks(),
                Equipment = Equipment.ToStacks(),
                Quests = CollectQuestSnapshots(),
                DiscoveredRegions = new List<string>(DiscoveredRegions),
                RngState = Rng.State,
                RngIncrement = Rng.Increment
            };

            return save;
        }

        /// <summary>
        /// Restores a session from a save.
        ///
        /// Intended to be called on a freshly constructed session before play
        /// begins. Progression growth is applied as a difference, so calling this
        /// after the character has already levelled does not double-count anything,
        /// but the player's vitals are still reset rather than carrying damage from
        /// whatever was happening when the save was written.
        /// </summary>
        public void ApplySave(SaveGame save)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            SlotId = save.SlotId;
            ProfileName = save.ProfileName;
            DifficultyId = save.DifficultyId;
            PlaytimeSeconds = save.PlaytimeSeconds;
            RegionId = save.RegionId;

            Player.SetPosition(save.Position);
            Player.SetFacing(save.FacingDegrees);

            Progression.LoadFrom(save.TotalExperience, save.UnspentAttributePoints);
            Inventory.LoadFrom(save.Inventory, out _);
            Equipment.LoadFrom(save.Equipment);
            ApplyQuestSnapshots(save.Quests);

            DiscoveredRegions = save.DiscoveredRegions == null
                ? new List<string>()
                : new List<string>(save.DiscoveredRegions);

            // The generator is restored exactly, so subsequent loot and combat
            // rolls continue the sequence rather than restarting it. Restoring it
            // on the encounter is enough, because that IS the session's generator.
            Encounter.RestoreRng(save.RngState, save.RngIncrement);

            Player.Vitals.ResetToFull();
            Player.Statuses.Clear();

            SyncCollectionObjectives();
            Chapters.Refresh();
        }

        private List<QuestSnapshot> CollectQuestSnapshots()
        {
            IReadOnlyList<QuestState> all = Quests.All;
            var snapshots = new List<QuestSnapshot>(all.Count);

            for (int i = 0; i < all.Count; i++)
            {
                QuestState state = all[i];
                if (state.Status == QuestStatus.Locked)
                {
                    continue;
                }

                snapshots.Add(new QuestSnapshot(
                    state.Definition.Id,
                    state.Status,
                    state.ToProgressArray()));
            }

            return snapshots;
        }

        private void ApplyQuestSnapshots(List<QuestSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                QuestSnapshot snapshot = snapshots[i];
                QuestState state = Quests.State(snapshot.QuestId);

                // A quest whose content has since been removed is skipped rather
                // than failing the whole load.
                if (state == null)
                {
                    continue;
                }

                state.LoadProgress(snapshot.Progress);
                state.Status = snapshot.Status;
            }
        }

        private void OnEncounterDeath(Combatant victim, Participant participant)
        {
            ReportDefeat(victim);
        }
    }
}
