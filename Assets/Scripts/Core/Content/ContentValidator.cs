using System;
using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Items;
using Shadowbound.Core.Quests;
using Shadowbound.Core.World;

namespace Shadowbound.Core.Content
{
    public enum ContentProblemSeverity
    {
        /// <summary>Something is odd but the game will run. Worth fixing.</summary>
        Warning = 0,

        /// <summary>The content is inconsistent. Something will silently not work.</summary>
        Error = 1
    }

    /// <summary>One problem found in authored content, with where it came from.</summary>
    public readonly struct ContentProblem
    {
        public readonly ContentProblemSeverity Severity;

        /// <summary>The content id the problem belongs to, e.g. a loot table id.</summary>
        public readonly string Location;

        public readonly string Message;

        public ContentProblem(ContentProblemSeverity severity, string location, string message)
        {
            Severity = severity;
            Location = location;
            Message = message;
        }

        public override string ToString()
        {
            string where = string.IsNullOrEmpty(Location) ? "(content)" : Location;

            return (Severity == ContentProblemSeverity.Error ? "error" : "warning") +
                   " in '" + where + "': " + Message;
        }
    }

    /// <summary>The outcome of validating a content set.</summary>
    public sealed class ContentReport
    {
        private readonly List<ContentProblem> _problems;

        public ContentReport(int capacity)
        {
            _problems = new List<ContentProblem>(capacity < 0 ? 0 : capacity);
        }

        public IReadOnlyList<ContentProblem> Problems
        {
            get { return _problems; }
        }

        public int ItemCount { get; internal set; }

        public int ArchetypeCount { get; internal set; }

        public int LootTableCount { get; internal set; }

        public int QuestCount { get; internal set; }

        public int ChapterCount { get; internal set; }

        public int RegionCount { get; internal set; }

        public int AbilityCount { get; internal set; }

        public int ErrorCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _problems.Count; i++)
                {
                    if (_problems[i].Severity == ContentProblemSeverity.Error)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int WarningCount
        {
            get { return _problems.Count - ErrorCount; }
        }

        public bool IsClean
        {
            get { return ErrorCount == 0; }
        }

        /// <summary>A one-line summary, suitable for a log or a dialog.</summary>
        public string Summary()
        {
            return ItemCount + " items, " + ArchetypeCount + " archetypes, " + LootTableCount +
                   " loot tables, " + AbilityCount + " player abilities, " + QuestCount + " quests, " +
                   ChapterCount + " chapters, " + RegionCount + " regions - " +
                   (IsClean
                       ? (WarningCount == 0 ? "no problems." : WarningCount + " warning(s).")
                       : ErrorCount + " error(s), " + WarningCount + " warning(s).");
        }

        internal void Add(ContentProblem problem)
        {
            _problems.Add(problem);
        }

        internal void Error(string location, string message)
        {
            _problems.Add(new ContentProblem(ContentProblemSeverity.Error, location, message));
        }

        internal void Warn(string location, string message)
        {
            _problems.Add(new ContentProblem(ContentProblemSeverity.Warning, location, message));
        }
    }

    /// <summary>
    /// Cross-checks authored content for references that point at nothing.
    ///
    /// The failure mode this exists to prevent is silence. A loot table naming an
    /// item that does not exist does not crash - it yields nothing, and the player
    /// concludes the creature never drops anything. A quest naming a creature that
    /// is never spawned simply never completes. Both look like game design rather
    /// than bugs, which is exactly why they need to be caught by a machine.
    ///
    /// This lives in the core rather than in the editor tooling for two reasons: it
    /// is ordinary data logic with no engine dependency, and keeping it here means
    /// it can be tested - so the validator is proven to actually catch the mistakes
    /// it claims to, rather than merely reporting a clean bill of health.
    /// </summary>
    public static class ContentValidator
    {
        /// <summary>Builds and validates the content this game ships with.</summary>
        public static ContentReport ValidateShippedContent()
        {
            return Validate(
                GameContent.BuildItems(),
                GameContent.BuildEnemyArchetypes(),
                GameContent.BuildLootTables(),
                GameContent.BuildQuests(),
                GameContent.BuildChapters(),
                GameContent.BuildRegions(),
                GameContent.BuildPlayerAbilities());
        }

        public static ContentReport Validate(
            ItemDatabase items,
            IReadOnlyList<EnemyArchetype> archetypes,
            IReadOnlyDictionary<string, LootTable> lootTables,
            IReadOnlyList<QuestDefinition> quests,
            IReadOnlyList<ChapterDefinition> chapters,
            IReadOnlyList<RegionDefinition> regions,
            IReadOnlyList<AbilityDefinition> playerAbilities)
        {
            var report = new ContentReport(16);

            items = items ?? new ItemDatabase();
            archetypes = archetypes ?? new List<EnemyArchetype>();
            lootTables = lootTables ?? new Dictionary<string, LootTable>();
            quests = quests ?? new List<QuestDefinition>();
            chapters = chapters ?? new List<ChapterDefinition>();
            regions = regions ?? new List<RegionDefinition>();
            playerAbilities = playerAbilities ?? new List<AbilityDefinition>();

            report.ItemCount = items.Count;
            report.ArchetypeCount = archetypes.Count;
            report.LootTableCount = lootTables.Count;
            report.QuestCount = quests.Count;
            report.ChapterCount = chapters.Count;
            report.RegionCount = regions.Count;
            report.AbilityCount = playerAbilities.Count;

            var itemIds = CollectItemIds(items);
            var archetypeIds = CollectIds(archetypes, a => a.Id);
            var questIds = CollectIds(quests, q => q.Id);
            var chapterIds = CollectIds(chapters, c => c.Id);
            var regionIds = CollectIds(regions, r => r.Id);

            ValidateLootTables(report, lootTables, itemIds);
            ValidateArchetypes(report, archetypes, lootTables);
            ValidatePlayerAbilities(report, playerAbilities);
            ValidateQuests(report, quests, questIds, archetypeIds, itemIds, regionIds, chapterIds);
            ValidateChapters(report, chapters, questIds, chapterIds, regionIds);
            ValidateRegions(report, regions, regionIds, chapterIds, lootTables);

            return report;
        }

        // ------------------------------- collection -------------------------------

        private static HashSet<string> CollectItemIds(ItemDatabase items)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (ItemDefinition definition in items.All)
            {
                if (!string.IsNullOrEmpty(definition.Id))
                {
                    ids.Add(definition.Id);
                }
            }

            return ids;
        }

        private static HashSet<string> CollectIds<T>(IReadOnlyList<T> list, Func<T, string> selector)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < list.Count; i++)
            {
                string id = selector(list[i]);

                if (!string.IsNullOrEmpty(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        // -------------------------------- loot tables ------------------------------

        private static void ValidateLootTables(
            ContentReport report,
            IReadOnlyDictionary<string, LootTable> lootTables,
            HashSet<string> itemIds)
        {
            foreach (KeyValuePair<string, LootTable> entry in lootTables)
            {
                LootTable table = entry.Value;

                if (table == null)
                {
                    report.Error(entry.Key, "Loot table is null.");
                    continue;
                }

                if (string.IsNullOrEmpty(table.Id))
                {
                    report.Error(entry.Key, "Loot table has no id.");
                }
                else if (!string.Equals(table.Id, entry.Key, StringComparison.Ordinal))
                {
                    report.Error(
                        entry.Key,
                        "Loot table is registered as '" + entry.Key + "' but its id is '" + table.Id +
                        "'. Lookups use the key, so one of the two is wrong.");
                }

                if (table.MinRolls < 1 || table.MaxRolls < table.MinRolls)
                {
                    report.Error(
                        table.Id,
                        "Invalid roll range " + table.MinRolls + ".." + table.MaxRolls + ".");
                }

                ValidateLootEntries(report, table, table.Guaranteed, "guaranteed", itemIds);
                ValidateLootEntries(report, table, table.Weighted, "weighted", itemIds);

                if (table.Guaranteed.Length == 0 && table.Weighted.Length == 0)
                {
                    report.Warn(table.Id, "Loot table can never drop anything.");
                }
            }
        }

        private static void ValidateLootEntries(
            ContentReport report,
            LootTable table,
            LootEntry[] entries,
            string group,
            HashSet<string> itemIds)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                LootEntry entry = entries[i];

                if (entry == null)
                {
                    report.Error(table.Id, "Has a null " + group + " entry.");
                    continue;
                }

                if (string.IsNullOrEmpty(entry.ItemId))
                {
                    report.Error(table.Id, "Has a " + group + " entry with no item id.");
                    continue;
                }

                if (!itemIds.Contains(entry.ItemId))
                {
                    report.Error(
                        table.Id,
                        "Drops '" + entry.ItemId +
                        "', which is not a registered item. The drop would silently yield nothing.");
                }

                if (entry.MinQuantity < 1 || entry.MaxQuantity < entry.MinQuantity)
                {
                    report.Error(
                        table.Id,
                        "Entry '" + entry.ItemId + "' has an invalid quantity range " +
                        entry.MinQuantity + ".." + entry.MaxQuantity + ".");
                }

                if (string.Equals(group, "weighted", StringComparison.Ordinal) && entry.Weight <= 0f)
                {
                    report.Warn(
                        table.Id,
                        "Entry '" + entry.ItemId + "' has weight " + entry.Weight +
                        " and can never be selected.");
                }
            }
        }

        // -------------------------------- archetypes -------------------------------

        private static void ValidateArchetypes(
            ContentReport report,
            IReadOnlyList<EnemyArchetype> archetypes,
            IReadOnlyDictionary<string, LootTable> lootTables)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < archetypes.Count; i++)
            {
                EnemyArchetype archetype = archetypes[i];

                if (archetype == null)
                {
                    report.Error(string.Empty, "Null archetype at index " + i + ".");
                    continue;
                }

                string id = archetype.Id;

                if (string.IsNullOrEmpty(id))
                {
                    report.Error(string.Empty, "Archetype at index " + i + " has no id.");
                    continue;
                }

                if (!seen.Add(id))
                {
                    report.Error(id, "Duplicate archetype id. One of them is unreachable.");
                }

                if (archetype.Abilities == null || archetype.Abilities.Count == 0)
                {
                    report.Error(id, "Has no abilities, so it can never attack.");
                }
                else if (archetype.AttackAbilityIndex < 0 || archetype.AttackAbilityIndex >= archetype.Abilities.Count)
                {
                    report.Error(
                        id,
                        "Attacks with ability index " + archetype.AttackAbilityIndex +
                        " but only has " + archetype.Abilities.Count + " abilities.");
                }

                if (!string.IsNullOrEmpty(archetype.LootTableId) && !lootTables.ContainsKey(archetype.LootTableId))
                {
                    report.Error(id, "Points at loot table '" + archetype.LootTableId + "', which does not exist.");
                }

                if (archetype.MaxHealth <= 0f)
                {
                    report.Error(id, "Has MaxHealth " + archetype.MaxHealth + ", so it would spawn dead.");
                }

                if (archetype.MoveSpeed < 0f)
                {
                    report.Error(id, "Has a negative MoveSpeed.");
                }

                if (archetype.ExperienceReward < 0)
                {
                    report.Error(id, "Awards negative experience.");
                }

                if (archetype.TintRgb == null || archetype.TintRgb.Length < 3)
                {
                    report.Warn(id, "TintRgb needs three components for the placeholder colour.");
                }

                if (archetype.CritMultiplier < 1f)
                {
                    report.Warn(id, "CritMultiplier below 1 means a critical hit deals less damage than a normal one.");
                }
            }
        }

        // ------------------------------ player abilities ---------------------------

        private static void ValidatePlayerAbilities(ContentReport report, IReadOnlyList<AbilityDefinition> abilities)
        {
            if (abilities.Count == 0)
            {
                report.Error("player", "The player has no abilities, so combat is impossible.");
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityDefinition ability = abilities[i];

                if (ability == null)
                {
                    report.Error("player", "Null ability at index " + i + ".");
                    continue;
                }

                if (string.IsNullOrEmpty(ability.Id))
                {
                    report.Error("player", "Ability at index " + i + " has no id.");
                    continue;
                }

                if (!seen.Add(ability.Id))
                {
                    report.Error(ability.Id, "Duplicate ability id.");
                }

                if (ability.CooldownSeconds < 0f)
                {
                    report.Error(ability.Id, "Has a negative cooldown.");
                }

                if (ability.WindupSeconds < 0f)
                {
                    report.Error(ability.Id, "Has a negative wind-up.");
                }

                if (ability.Kind == AbilityKind.Melee || ability.Kind == AbilityKind.Bolt)
                {
                    if (ability.Range <= 0f)
                    {
                        report.Error(
                            ability.Id,
                            "Is a " + ability.Kind + " with range " + ability.Range +
                            ", so it can never reach anything.");
                    }
                }
            }
        }

        // ---------------------------------- quests ---------------------------------

        private static void ValidateQuests(
            ContentReport report,
            IReadOnlyList<QuestDefinition> quests,
            HashSet<string> questIds,
            HashSet<string> archetypeIds,
            HashSet<string> itemIds,
            HashSet<string> regionIds,
            HashSet<string> chapterIds)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < quests.Count; i++)
            {
                QuestDefinition quest = quests[i];

                if (quest == null)
                {
                    report.Error(string.Empty, "Null quest at index " + i + ".");
                    continue;
                }

                string id = quest.Id;

                if (string.IsNullOrEmpty(id))
                {
                    report.Error(string.Empty, "Quest at index " + i + " has no id.");
                    continue;
                }

                if (!seen.Add(id))
                {
                    report.Error(id, "Duplicate quest id.");
                }

                if (!string.IsNullOrEmpty(quest.ChapterId) && !chapterIds.Contains(quest.ChapterId))
                {
                    report.Error(id, "Belongs to chapter '" + quest.ChapterId + "', which does not exist.");
                }

                if (quest.Objectives == null || quest.Objectives.Length == 0)
                {
                    report.Error(id, "Has no objectives, so it can never be completed.");
                }
                else
                {
                    ValidateObjectiveIds(report, quest, archetypeIds, itemIds, regionIds);

                    if (quest.RequiredObjectiveCount == 0)
                    {
                        report.Error(id, "Every objective is optional, so the quest completes immediately.");
                    }
                }

                if (quest.PrerequisiteQuestIds != null)
                {
                    for (int j = 0; j < quest.PrerequisiteQuestIds.Length; j++)
                    {
                        string prerequisite = quest.PrerequisiteQuestIds[j];

                        if (string.IsNullOrEmpty(prerequisite))
                        {
                            continue;
                        }

                        if (!questIds.Contains(prerequisite))
                        {
                            report.Error(
                                id,
                                "Requires quest '" + prerequisite +
                                "', which does not exist. The quest could never be started.");
                        }
                        else if (string.Equals(prerequisite, id, StringComparison.Ordinal))
                        {
                            report.Error(id, "Requires itself, so it could never be started.");
                        }
                    }
                }

                ValidateRewardItems(report, quest, itemIds);
            }

            ReportPrerequisiteCycles(report, quests, questIds);
        }

        /// <summary>
        /// Checks that each objective's target actually exists in the content set
        /// the objective kind refers to. A kill objective names a creature, a collect
        /// objective names an item, a reach objective names a region.
        /// </summary>
        private static void ValidateObjectiveIds(
            ContentReport report,
            QuestDefinition quest,
            HashSet<string> archetypeIds,
            HashSet<string> itemIds,
            HashSet<string> regionIds)
        {
            for (int i = 0; i < quest.Objectives.Length; i++)
            {
                ObjectiveDefinition objective = quest.Objectives[i];

                if (objective == null)
                {
                    report.Error(quest.Id, "Has a null objective.");
                    continue;
                }

                if (objective.RequiredCount < 1)
                {
                    report.Error(
                        quest.Id,
                        "Objective '" + objective.Id + "' requires " + objective.RequiredCount +
                        " and would be satisfied before it started.");
                }

                if (string.IsNullOrEmpty(objective.TargetId))
                {
                    // Interact and Survive objectives are legitimately targetless.
                    if (objective.Kind == ObjectiveKind.Kill ||
                        objective.Kind == ObjectiveKind.DefeatBoss ||
                        objective.Kind == ObjectiveKind.Collect ||
                        objective.Kind == ObjectiveKind.Reach)
                    {
                        report.Error(quest.Id, "Objective '" + objective.Id + "' has no target.");
                    }

                    continue;
                }

                switch (objective.Kind)
                {
                    case ObjectiveKind.Kill:
                    case ObjectiveKind.DefeatBoss:
                        // Both name a creature, so both are checked against the
                        // archetypes. Missing this case is how a boss objective with
                        // a typo'd target would slip through unvalidated.
                        if (!archetypeIds.Contains(objective.TargetId))
                        {
                            report.Error(
                                quest.Id,
                                "Objective '" + objective.Id + "' targets creature '" + objective.TargetId +
                                "', which is not an archetype. The quest could never be completed.");
                        }

                        break;

                    case ObjectiveKind.Collect:
                        if (!itemIds.Contains(objective.TargetId))
                        {
                            report.Error(
                                quest.Id,
                                "Objective '" + objective.Id + "' collects '" + objective.TargetId +
                                "', which is not an item.");
                        }

                        break;

                    case ObjectiveKind.Reach:
                        if (!regionIds.Contains(objective.TargetId))
                        {
                            report.Error(
                                quest.Id,
                                "Objective '" + objective.Id + "' reaches '" + objective.TargetId +
                                "', which is not a region.");
                        }

                        break;
                }
            }
        }

        private static void ValidateRewardItems(ContentReport report, QuestDefinition quest, HashSet<string> itemIds)
        {
            ItemStack[] items = quest.Rewards.Items;

            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (string.IsNullOrEmpty(items[i].ItemId))
                {
                    report.Error(quest.Id, "Has a reward with no item id.");
                    continue;
                }

                if (!itemIds.Contains(items[i].ItemId))
                {
                    report.Error(
                        quest.Id,
                        "Rewards '" + items[i].ItemId + "', which is not an item. The reward would be lost.");
                }

                if (items[i].Quantity < 1)
                {
                    report.Error(quest.Id, "Rewards '" + items[i].ItemId + "' in a quantity below one.");
                }
            }
        }

        /// <summary>
        /// Detects prerequisite loops. A cycle would leave every quest in it
        /// permanently unstartable, which is invisible in a design document and
        /// fatal in a playthrough.
        /// </summary>
        private static void ReportPrerequisiteCycles(
            ContentReport report,
            IReadOnlyList<QuestDefinition> quests,
            HashSet<string> questIds)
        {
            var byId = new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);

            for (int i = 0; i < quests.Count; i++)
            {
                QuestDefinition quest = quests[i];

                if (quest != null && !string.IsNullOrEmpty(quest.Id))
                {
                    byId[quest.Id] = quest;
                }
            }

            // 0 = unvisited, 1 = on the current path, 2 = fully explored.
            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            var cycleReported = new HashSet<string>(StringComparer.Ordinal);

            foreach (string start in questIds)
            {
                if (!byId.ContainsKey(start))
                {
                    continue;
                }

                DetectCycle(report, byId, state, start, cycleReported);
            }
        }

        private static void DetectCycle(
            ContentReport report,
            Dictionary<string, QuestDefinition> byId,
            Dictionary<string, int> state,
            string id,
            HashSet<string> cycleReported)
        {
            state.TryGetValue(id, out int current);

            if (current == 2)
            {
                return;
            }

            if (current == 1)
            {
                if (cycleReported.Add(id))
                {
                    report.Error(id, "Is part of a prerequisite cycle, so it can never be started.");
                }

                return;
            }

            state[id] = 1;

            if (byId.TryGetValue(id, out QuestDefinition quest) && quest.PrerequisiteQuestIds != null)
            {
                for (int i = 0; i < quest.PrerequisiteQuestIds.Length; i++)
                {
                    string prerequisite = quest.PrerequisiteQuestIds[i];

                    if (!string.IsNullOrEmpty(prerequisite) && byId.ContainsKey(prerequisite))
                    {
                        DetectCycle(report, byId, state, prerequisite, cycleReported);
                    }
                }
            }

            state[id] = 2;
        }

        // --------------------------------- chapters --------------------------------

        private static void ValidateChapters(
            ContentReport report,
            IReadOnlyList<ChapterDefinition> chapters,
            HashSet<string> questIds,
            HashSet<string> chapterIds,
            HashSet<string> regionIds)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < chapters.Count; i++)
            {
                ChapterDefinition chapter = chapters[i];

                if (chapter == null)
                {
                    report.Error(string.Empty, "Null chapter at index " + i + ".");
                    continue;
                }

                string id = chapter.Id;

                if (string.IsNullOrEmpty(id))
                {
                    report.Error(string.Empty, "Chapter at index " + i + " has no id.");
                    continue;
                }

                if (!seen.Add(id))
                {
                    report.Error(id, "Duplicate chapter id.");
                }

                if (chapter.QuestIds == null || chapter.QuestIds.Length == 0)
                {
                    report.Warn(id, "Contains no quests, so it can never complete.");
                }
                else
                {
                    for (int j = 0; j < chapter.QuestIds.Length; j++)
                    {
                        if (!questIds.Contains(chapter.QuestIds[j]))
                        {
                            report.Error(
                                id,
                                "Lists quest '" + chapter.QuestIds[j] +
                                "', which does not exist. The chapter could never complete.");
                        }
                    }
                }

                if (chapter.RequiredChapterIds != null)
                {
                    for (int j = 0; j < chapter.RequiredChapterIds.Length; j++)
                    {
                        string required = chapter.RequiredChapterIds[j];

                        if (string.IsNullOrEmpty(required))
                        {
                            continue;
                        }

                        if (!chapterIds.Contains(required))
                        {
                            report.Error(
                                id,
                                "Requires chapter '" + required +
                                "', which does not exist. The chapter could never unlock.");
                        }
                        else if (string.Equals(required, id, StringComparison.Ordinal))
                        {
                            report.Error(id, "Requires itself, so it could never unlock.");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(chapter.RegionId) && !regionIds.Contains(chapter.RegionId))
                {
                    report.Error(id, "Is set in region '" + chapter.RegionId + "', which does not exist.");
                }
            }
        }

        // ---------------------------------- regions --------------------------------

        private static void ValidateRegions(
            ContentReport report,
            IReadOnlyList<RegionDefinition> regions,
            HashSet<string> regionIds,
            HashSet<string> chapterIds,
            IReadOnlyDictionary<string, LootTable> lootTables)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < regions.Count; i++)
            {
                RegionDefinition region = regions[i];

                if (region == null)
                {
                    report.Error(string.Empty, "Null region at index " + i + ".");
                    continue;
                }

                string id = region.Id;

                if (string.IsNullOrEmpty(id))
                {
                    report.Error(string.Empty, "Region at index " + i + " has no id.");
                    continue;
                }

                if (!seen.Add(id))
                {
                    report.Error(id, "Duplicate region id.");
                }

                if (region.Connections != null)
                {
                    for (int j = 0; j < region.Connections.Length; j++)
                    {
                        string connection = region.Connections[j];

                        if (string.IsNullOrEmpty(connection))
                        {
                            continue;
                        }

                        if (!regionIds.Contains(connection))
                        {
                            report.Error(
                                id,
                                "Connects to '" + connection +
                                "', which is not a region. The link would silently not exist.");
                        }
                        else if (string.Equals(connection, id, StringComparison.Ordinal))
                        {
                            report.Warn(id, "Connects to itself.");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(region.RequiredChapterId) && !chapterIds.Contains(region.RequiredChapterId))
                {
                    report.Error(
                        id,
                        "Requires chapter '" + region.RequiredChapterId +
                        "', which does not exist. The region could never be entered.");
                }

                if (!string.IsNullOrEmpty(region.LootTableId) && !lootTables.ContainsKey(region.LootTableId))
                {
                    report.Error(id, "Points at loot table '" + region.LootTableId + "', which does not exist.");
                }

                if (region.RecommendedLevel < 1)
                {
                    report.Warn(id, "RecommendedLevel is below one.");
                }
            }

            ReportUnreachableRegions(report, regions, regionIds);
        }

        /// <summary>
        /// Finds regions the player could never walk to, ignoring chapter gates.
        ///
        /// A region that is authored, populated and connected only to somewhere that
        /// does not link back is content nobody will ever see. Checking reachability
        /// over the connection graph is cheap and catches it.
        /// </summary>
        private static void ReportUnreachableRegions(
            ContentReport report,
            IReadOnlyList<RegionDefinition> regions,
            HashSet<string> regionIds)
        {
            if (regions.Count <= 1)
            {
                return;
            }

            // Prefer the camp as the origin, since that is where a playthrough
            // starts; fall back to the first region otherwise.
            string start = null;

            for (int i = 0; i < regions.Count; i++)
            {
                RegionDefinition region = regions[i];

                if (region == null || string.IsNullOrEmpty(region.Id))
                {
                    continue;
                }

                if (start == null)
                {
                    start = region.Id;
                }

                if (region.Kind == RegionKind.Camp)
                {
                    start = region.Id;
                    break;
                }
            }

            if (start == null)
            {
                return;
            }

            var neighbours = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            for (int i = 0; i < regions.Count; i++)
            {
                RegionDefinition region = regions[i];

                if (region == null || string.IsNullOrEmpty(region.Id))
                {
                    continue;
                }

                var list = new List<string>(4);

                if (region.Connections != null)
                {
                    for (int j = 0; j < region.Connections.Length; j++)
                    {
                        string connection = region.Connections[j];

                        if (!string.IsNullOrEmpty(connection) && regionIds.Contains(connection))
                        {
                            list.Add(connection);
                        }
                    }
                }

                neighbours[region.Id] = list;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal) { start };
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();

                if (!neighbours.TryGetValue(current, out List<string> links))
                {
                    continue;
                }

                for (int i = 0; i < links.Count; i++)
                {
                    if (visited.Add(links[i]))
                    {
                        queue.Enqueue(links[i]);
                    }
                }
            }

            foreach (string id in regionIds)
            {
                if (!visited.Contains(id))
                {
                    report.Error(id, "Cannot be reached from '" + start + "'. Nothing links to it.");
                }
            }
        }
    }
}
