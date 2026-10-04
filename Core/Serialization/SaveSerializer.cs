using System;
using System.Collections.Generic;
using System.Globalization;
using Shadowbound.Core.Items;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Quests;

namespace Shadowbound.Core.Serialization
{
    /// <summary>Thrown when a save file cannot be interpreted.</summary>
    public sealed class SaveFormatException : Exception
    {
        public SaveFormatException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Converts a <see cref="SaveGame"/> to and from JSON.
    ///
    /// The file is wrapped in an envelope carrying a format id and a version, so a
    /// foreign or future file is rejected before its contents are interpreted.
    /// Interpreting first and asking questions later is how a save silently loads
    /// halfway and corrupts a character.
    ///
    /// Reading is tolerant in two specific ways, both deliberate:
    ///   * Missing fields fall back to a default, so a field added later does not
    ///     make older saves unloadable.
    ///   * Unknown fields are ignored, so a save written by a newer build that is
    ///     otherwise compatible still loads.
    ///
    /// The 64-bit generator state is written as a decimal STRING rather than a
    /// JSON number. A JSON number is a double here, which cannot represent every
    /// 64-bit integer exactly, so storing it numerically would silently corrupt
    /// the generator and desynchronise deterministic replays.
    /// </summary>
    public static class SaveSerializer
    {
        public const string FormatKey = "format";
        public const string VersionKey = "version";
        public const string DataKey = "data";

        public static string Serialize(SaveGame save)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            var root = JsonValue.NewObject();
            root.Set(FormatKey, SaveGame.FormatId);
            root.Set(VersionKey, save.Version);

            var data = JsonValue.NewObject();
            data.Set("slotId", save.SlotId);
            data.Set("profileName", save.ProfileName);
            data.Set("difficulty", save.DifficultyId);
            data.Set("savedAt", save.SavedAtUnixSeconds);
            data.Set("playtime", save.PlaytimeSeconds);
            data.Set("regionId", save.RegionId);
            data.Set("position", VectorToJson(save.Position));
            data.Set("facing", save.FacingDegrees);
            data.Set("totalExperience", save.TotalExperience);
            data.Set("unspentAttributePoints", save.UnspentAttributePoints);
            data.Set("statBoosts", FloatsToJson(save.StatBoosts));
            data.Set("inventory", StacksToJson(save.Inventory));
            data.Set("equipment", StacksToJson(save.Equipment));
            data.Set("quests", QuestsToJson(save.Quests));
            data.Set("discoveredRegions", StringsToJson(save.DiscoveredRegions));
            data.Set("rngState", save.RngState.ToString(CultureInfo.InvariantCulture));
            data.Set("rngIncrement", save.RngIncrement.ToString(CultureInfo.InvariantCulture));

            root.Set(DataKey, data);
            return root.ToJson(true);
        }

        public static SaveGame Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new SaveFormatException("Save data is empty.");
            }

            if (!JsonValue.TryParse(json, out JsonValue root, out string parseError))
            {
                throw new SaveFormatException("Save data is not valid JSON: " + parseError);
            }

            if (root.Kind != JsonKind.Object)
            {
                throw new SaveFormatException("Save data is not a JSON object.");
            }

            string format = root.Get(FormatKey)?.AsString("");
            if (format != SaveGame.FormatId)
            {
                throw new SaveFormatException(
                    "Not a Shadowbound save file (found format '" + format + "').");
            }

            JsonValue versionNode = root.Get(VersionKey);
            if (versionNode == null || versionNode.Kind != JsonKind.Number)
            {
                throw new SaveFormatException("Save data has no version.");
            }

            int version = versionNode.AsInt(0);
            if (version < 1)
            {
                throw new SaveFormatException("Save data has an invalid version (" + version + ").");
            }

            if (version > SaveGame.CurrentVersion)
            {
                throw new SaveFormatException(
                    "Save was written by a newer version of the game (format " + version
                    + ", this build understands " + SaveGame.CurrentVersion + ").");
            }

            JsonValue data = root.Get(DataKey);
            if (data == null || data.Kind != JsonKind.Object)
            {
                throw new SaveFormatException("Save data is missing its contents.");
            }

            var save = new SaveGame { Version = version };

            save.SlotId = data.Get("slotId")?.AsString("") ?? "";
            save.ProfileName = data.Get("profileName")?.AsString("Wanderer") ?? "Wanderer";
            save.DifficultyId = data.Get("difficulty")?.AsString("wanderer") ?? "wanderer";
            save.SavedAtUnixSeconds = data.Get("savedAt")?.AsLong(0L) ?? 0L;
            save.PlaytimeSeconds = data.Get("playtime")?.AsFloat(0f) ?? 0f;
            save.RegionId = data.Get("regionId")?.AsString("") ?? "";
            save.Position = VectorFromJson(data.Get("position"));
            save.FacingDegrees = data.Get("facing")?.AsFloat(0f) ?? 0f;
            save.TotalExperience = data.Get("totalExperience")?.AsInt(0) ?? 0;
            save.UnspentAttributePoints = data.Get("unspentAttributePoints")?.AsInt(0) ?? 0;
            save.StatBoosts = FloatsFromJson(data.Get("statBoosts"));

            save.Inventory = StacksFromJson(data.Get("inventory"));
            save.Equipment = StacksFromJson(data.Get("equipment"));
            save.Quests = QuestsFromJson(data.Get("quests"));
            save.DiscoveredRegions = data.Get("discoveredRegions")?.AsStringList() ?? new List<string>();

            save.RngState = ParseUnsigned(data.Get("rngState"), 0UL);
            save.RngIncrement = ParseUnsigned(data.Get("rngIncrement"), 1UL);

            return save;
        }

        /// <summary>Non-throwing load. Returns false with a message the UI can show.</summary>
        public static bool TryDeserialize(string json, out SaveGame save, out string error)
        {
            save = null;
            error = null;

            try
            {
                save = Deserialize(json);
                return true;
            }
            catch (SaveFormatException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static ulong ParseUnsigned(JsonValue node, ulong fallback)
        {
            if (node == null)
            {
                return fallback;
            }

            // Written as a string to preserve all 64 bits; a number is accepted
            // too so a hand-edited save still loads.
            if (node.Kind == JsonKind.String)
            {
                return ulong.TryParse(node.AsString(""), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed)
                    ? parsed
                    : fallback;
            }

            if (node.Kind == JsonKind.Number)
            {
                double value = node.AsNumber(double.NaN);
                if (double.IsNaN(value) || value < 0d)
                {
                    return fallback;
                }

                return value > ulong.MaxValue ? fallback : (ulong)value;
            }

            return fallback;
        }

        private static JsonValue VectorToJson(Float3 vector)
        {
            var node = JsonValue.NewObject();
            node.Set("x", vector.X);
            node.Set("y", vector.Y);
            node.Set("z", vector.Z);
            return node;
        }

        private static Float3 VectorFromJson(JsonValue node)
        {
            if (node == null || node.Kind != JsonKind.Object)
            {
                return Float3.Zero;
            }

            return new Float3(
                node.Get("x")?.AsFloat(0f) ?? 0f,
                node.Get("y")?.AsFloat(0f) ?? 0f,
                node.Get("z")?.AsFloat(0f) ?? 0f);
        }

        private static JsonValue StacksToJson(List<ItemStack> stacks)
        {
            var array = JsonValue.NewArray();

            if (stacks == null)
            {
                return array;
            }

            for (int i = 0; i < stacks.Count; i++)
            {
                ItemStack stack = stacks[i];
                if (stack.IsEmpty)
                {
                    continue;
                }

                var node = JsonValue.NewObject();
                node.Set("id", stack.ItemId);
                node.Set("qty", stack.Quantity);
                array.Add(node);
            }

            return array;
        }

        private static List<ItemStack> StacksFromJson(JsonValue node)
        {
            var stacks = new List<ItemStack>();

            if (node == null || node.Kind != JsonKind.Array)
            {
                return stacks;
            }

            for (int i = 0; i < node.Count; i++)
            {
                JsonValue entry = node.At(i);
                if (entry == null || entry.Kind != JsonKind.Object)
                {
                    continue;
                }

                string id = entry.Get("id")?.AsString("") ?? "";
                int quantity = entry.Get("qty")?.AsInt(0) ?? 0;

                if (string.IsNullOrEmpty(id) || quantity <= 0)
                {
                    continue;
                }

                stacks.Add(new ItemStack(id, quantity));
            }

            return stacks;
        }

        private static JsonValue QuestsToJson(List<QuestSnapshot> quests)
        {
            var array = JsonValue.NewArray();

            if (quests == null)
            {
                return array;
            }

            for (int i = 0; i < quests.Count; i++)
            {
                QuestSnapshot snapshot = quests[i];
                if (string.IsNullOrEmpty(snapshot.QuestId))
                {
                    continue;
                }

                var node = JsonValue.NewObject();
                node.Set("id", snapshot.QuestId);
                node.Set("status", (int)snapshot.Status);

                var progress = JsonValue.NewArray();
                int[] saved = snapshot.Progress ?? Array.Empty<int>();
                for (int j = 0; j < saved.Length; j++)
                {
                    progress.Add(saved[j]);
                }

                node.Set("progress", progress);
                array.Add(node);
            }

            return array;
        }

        private static List<QuestSnapshot> QuestsFromJson(JsonValue node)
        {
            var quests = new List<QuestSnapshot>();

            if (node == null || node.Kind != JsonKind.Array)
            {
                return quests;
            }

            for (int i = 0; i < node.Count; i++)
            {
                JsonValue entry = node.At(i);
                if (entry == null || entry.Kind != JsonKind.Object)
                {
                    continue;
                }

                string id = entry.Get("id")?.AsString("") ?? "";
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                int rawStatus = entry.Get("status")?.AsInt(0) ?? 0;
                QuestStatus status = rawStatus >= 0 && rawStatus <= (int)QuestStatus.TurnedIn
                    ? (QuestStatus)rawStatus
                    : QuestStatus.Locked;

                quests.Add(new QuestSnapshot(
                    id,
                    status,
                    entry.Get("progress")?.AsIntArray() ?? Array.Empty<int>()));
            }

            return quests;
        }

        private static JsonValue FloatsToJson(float[] values)
        {
            var array = JsonValue.NewArray();

            if (values == null)
            {
                return array;
            }

            for (int i = 0; i < values.Length; i++)
            {
                array.Add(values[i]);
            }

            return array;
        }

        private static float[] FloatsFromJson(JsonValue node)
        {
            if (node == null || node.Kind != JsonKind.Array)
            {
                return Array.Empty<float>();
            }

            var values = new float[node.Count];

            for (int i = 0; i < node.Count; i++)
            {
                values[i] = node.At(i)?.AsFloat(0f) ?? 0f;
            }

            return values;
        }

        private static JsonValue StringsToJson(List<string> values)
        {
            var array = JsonValue.NewArray();

            if (values == null)
            {
                return array;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrEmpty(values[i]))
                {
                    array.Add(values[i]);
                }
            }

            return array;
        }
    }
}
