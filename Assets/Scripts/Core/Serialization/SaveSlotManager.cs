using System;
using System.Collections.Generic;

namespace Shadowbound.Core.Serialization
{
    /// <summary>
    /// Somewhere save data can live.
    ///
    /// Abstracted so the core can load and save without knowing about files,
    /// player preferences or a cloud service. The core ships an in-memory
    /// implementation for tests, and the Unity layer supplies a real one.
    /// </summary>
    public interface ISaveStorage
    {
        bool Exists(string slotId);

        /// <summary>Returns the stored text, or null when the slot is absent.</summary>
        string Read(string slotId);

        void Write(string slotId, string contents);

        void Delete(string slotId);

        IReadOnlyList<string> ListSlots();
    }

    /// <summary>Storage that lives only as long as the process. Used by tests.</summary>
    public sealed class InMemorySaveStorage : ISaveStorage
    {
        private readonly Dictionary<string, string> _slots =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public int WriteCount { get; private set; }

        public bool Exists(string slotId)
        {
            return !string.IsNullOrEmpty(slotId) && _slots.ContainsKey(slotId);
        }

        public string Read(string slotId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                return null;
            }

            return _slots.TryGetValue(slotId, out string contents) ? contents : null;
        }

        public void Write(string slotId, string contents)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                throw new ArgumentException("A save slot needs an id.", nameof(slotId));
            }

            _slots[slotId] = contents ?? string.Empty;
            WriteCount++;
        }

        public void Delete(string slotId)
        {
            if (!string.IsNullOrEmpty(slotId))
            {
                _slots.Remove(slotId);
            }
        }

        public IReadOnlyList<string> ListSlots()
        {
            var slots = new List<string>(_slots.Count);
            foreach (KeyValuePair<string, string> entry in _slots)
            {
                slots.Add(entry.Key);
            }

            slots.Sort(StringComparer.Ordinal);
            return slots;
        }
    }

    /// <summary>Outcome of a save operation, with a message fit to show a player.</summary>
    public readonly struct SaveResult
    {
        public readonly bool Success;
        public readonly string Error;

        private SaveResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public static SaveResult Ok()
        {
            return new SaveResult(true, null);
        }

        public static SaveResult Fail(string error)
        {
            return new SaveResult(false, error);
        }

        public override string ToString()
        {
            return Success ? "OK" : "Failed: " + Error;
        }
    }

    /// <summary>
    /// Reads and writes save slots.
    ///
    /// Migration runs on load rather than on save, so an old file stays on disk in
    /// its original form until the player actually loads and re-saves it. If the
    /// upgrade goes wrong, the original is still there.
    /// </summary>
    public sealed class SaveSlotManager
    {
        private readonly ISaveStorage _storage;
        private readonly Func<long> _clock;

        /// <param name="storage">Where slot data lives.</param>
        /// <param name="clock">Supplies the current unix time. Passing null disables timestamping.</param>
        public SaveSlotManager(ISaveStorage storage, Func<long> clock = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _clock = clock;
        }

        public ISaveStorage Storage
        {
            get { return _storage; }
        }

        public bool Exists(string slotId)
        {
            return _storage.Exists(slotId);
        }

        public IReadOnlyList<string> ListSlots()
        {
            return _storage.ListSlots();
        }

        /// <summary>
        /// Writes a save, stamping its version and timestamp. The passed save is
        /// updated to match what was written, so the caller's copy does not drift
        /// out of step with the file.
        /// </summary>
        public SaveResult Save(SaveGame save)
        {
            if (save == null)
            {
                return SaveResult.Fail("There is no save data to write.");
            }

            if (!save.IsValid)
            {
                return SaveResult.Fail("Save data has no slot id.");
            }

            save.Version = SaveGame.CurrentVersion;

            if (_clock != null)
            {
                save.SavedAtUnixSeconds = _clock();
            }

            try
            {
                string json = SaveSerializer.Serialize(save);
                _storage.Write(save.SlotId, json);
                return SaveResult.Ok();
            }
            catch (Exception exception)
            {
                return SaveResult.Fail("Could not write the save: " + exception.Message);
            }
        }

        public SaveResult Load(string slotId, out SaveGame save)
        {
            save = null;

            if (string.IsNullOrEmpty(slotId))
            {
                return SaveResult.Fail("No slot id was given.");
            }

            if (!_storage.Exists(slotId))
            {
                return SaveResult.Fail("There is no save in slot '" + slotId + "'.");
            }

            string json = _storage.Read(slotId);
            if (string.IsNullOrEmpty(json))
            {
                return SaveResult.Fail("Save slot '" + slotId + "' is empty.");
            }

            if (!SaveSerializer.TryDeserialize(json, out SaveGame loaded, out string error))
            {
                return SaveResult.Fail(error);
            }

            try
            {
                SaveMigration.Migrate(loaded);
            }
            catch (SaveFormatException exception)
            {
                return SaveResult.Fail(exception.Message);
            }

            save = loaded;
            return SaveResult.Ok();
        }

        public SaveResult Delete(string slotId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                return SaveResult.Fail("No slot id was given.");
            }

            if (!_storage.Exists(slotId))
            {
                return SaveResult.Fail("There is no save in slot '" + slotId + "'.");
            }

            _storage.Delete(slotId);
            return SaveResult.Ok();
        }
    }
}
