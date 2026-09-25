using System;
using System.Collections.Generic;
using Shadowbound.Core.Items;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Quests;

namespace Shadowbound.Core.Serialization
{
    /// <summary>Saved state for one quest.</summary>
    public struct QuestSnapshot
    {
        public string QuestId;
        public QuestStatus Status;
        public int[] Progress;

        public QuestSnapshot(string questId, QuestStatus status, int[] progress)
        {
            QuestId = questId;
            Status = status;
            Progress = progress ?? Array.Empty<int>();
        }
    }

    /// <summary>
    /// Everything persisted between sessions.
    ///
    /// Flat and id-based on purpose: quest and item state are stored as ids plus
    /// progress, not as serialised objects from the content tables. That means a
    /// save stays valid when content is tweaked, and a save file is readable by
    /// hand when something needs diagnosing.
    ///
    /// Deliberately holds no reference to anything live. Reading a save should
    /// never mutate the running game as a side effect of parsing it.
    /// </summary>
    public sealed class SaveGame
    {
        /// <summary>First shipped format. Bump when the shape of the data changes.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Marker written into every file, so a foreign JSON document is rejected before it is interpreted.</summary>
        public const string FormatId = "shadowbound.save";

        public string SlotId = "";

        /// <summary>Character name shown on the slot list.</summary>
        public string ProfileName = "Wanderer";

        /// <summary>Difficulty preset id.</summary>
        public string DifficultyId = "wanderer";

        public int Version = CurrentVersion;

        /// <summary>Unix timestamp of the last write.</summary>
        public long SavedAtUnixSeconds;

        public float PlaytimeSeconds;

        /// <summary>Region the player was standing in.</summary>
        public string RegionId = "";

        public Float3 Position;

        public float FacingDegrees;

        public int TotalExperience;

        public int UnspentAttributePoints;

        public List<ItemStack> Inventory = new List<ItemStack>();

        public List<ItemStack> Equipment = new List<ItemStack>();

        public List<QuestSnapshot> Quests = new List<QuestSnapshot>();

        /// <summary>Regions the player has discovered, for the map.</summary>
        public List<string> DiscoveredRegions = new List<string>();

        /// <summary>
        /// Saved generator state, so loot and combat rolls continue from where they
        /// left off rather than restarting. Named for the value it belongs to.
        /// </summary>
        public ulong RngState;

        public ulong RngIncrement;

        /// <summary>A save with no slot is not yet ready to be written.</summary>
        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(SlotId); }
        }

        /// <summary>Deep copy. Used by tests and by the slot manager before stamping metadata.</summary>
        public SaveGame Clone()
        {
            var copy = new SaveGame
            {
                SlotId = SlotId,
                ProfileName = ProfileName,
                DifficultyId = DifficultyId,
                Version = Version,
                SavedAtUnixSeconds = SavedAtUnixSeconds,
                PlaytimeSeconds = PlaytimeSeconds,
                RegionId = RegionId,
                Position = Position,
                FacingDegrees = FacingDegrees,
                TotalExperience = TotalExperience,
                UnspentAttributePoints = UnspentAttributePoints,
                RngState = RngState,
                RngIncrement = RngIncrement,
                Inventory = new List<ItemStack>(Inventory),
                Equipment = new List<ItemStack>(Equipment),
                DiscoveredRegions = new List<string>(DiscoveredRegions),
                Quests = new List<QuestSnapshot>(Quests.Count)
            };

            for (int i = 0; i < Quests.Count; i++)
            {
                QuestSnapshot snapshot = Quests[i];
                copy.Quests.Add(new QuestSnapshot(
                    snapshot.QuestId,
                    snapshot.Status,
                    snapshot.Progress == null ? Array.Empty<int>() : (int[])snapshot.Progress.Clone()));
            }

            return copy;
        }
    }
}
