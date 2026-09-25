using System;
using System.Collections.Generic;
using System.IO;
using Shadowbound.Core.Serialization;
using UnityEngine;

namespace Shadowbound.Game.Save
{
    /// <summary>
    /// Stores save slots as files under the platform's persistent data path.
    ///
    /// Writes go to a temporary file first and are then moved into place, so a
    /// crash or a battery pull mid-write cannot leave a truncated save where a
    /// complete one used to be. On Android especially, a partially written file is
    /// indistinguishable from a corrupt one, and losing an entire playthrough to a
    /// badly timed interrupt is not acceptable.
    ///
    /// The core supplies the format and the migration; this class only moves bytes.
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private const string Extension = ".shadowbound.json";
        private const string TempExtension = ".tmp";

        private readonly string _directory;

        public FileSaveStorage(string subdirectory = "saves")
        {
            _directory = Path.Combine(Application.persistentDataPath, subdirectory);
        }

        public string Directory
        {
            get { return _directory; }
        }

        /// <summary>Last error encountered, for surfacing in the UI.</summary>
        public string LastError { get; private set; }

        public bool Exists(string slotId)
        {
            return !string.IsNullOrEmpty(slotId) && File.Exists(PathFor(slotId));
        }

        public string Read(string slotId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                return null;
            }

            string path = PathFor(slotId);

            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception exception)
            {
                LastError = "Could not read save '" + slotId + "': " + exception.Message;
                return null;
            }
        }

        public void Write(string slotId, string contents)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                throw new ArgumentException("A save slot needs an id.", nameof(slotId));
            }

            Directory.CreateDirectory(_directory);

            string path = PathFor(slotId);
            string temporary = path + TempExtension;

            try
            {
                File.WriteAllText(temporary, contents ?? string.Empty);

                // Replacing an existing file is not atomic everywhere, so the old
                // one is only removed once the new one is fully on disk. A crash
                // between the two leaves the temporary file, not a hole where the
                // save used to be.
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
            }
            catch (Exception exception)
            {
                LastError = "Could not write save '" + slotId + "': " + exception.Message;

                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch
                {
                    // Cleaning up a temporary file is best effort. Failing to do so
                    // must not mask the original write error.
                }

                throw;
            }
        }

        public void Delete(string slotId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                return;
            }

            string path = PathFor(slotId);

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                LastError = "Could not delete save '" + slotId + "': " + exception.Message;
            }
        }

        public IReadOnlyList<string> ListSlots()
        {
            var slots = new List<string>();

            if (!Directory.Exists(_directory))
            {
                return slots;
            }

            try
            {
                string[] files = Directory.GetFiles(_directory, "*" + Extension);

                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);

                    if (name.EndsWith(TempExtension, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    slots.Add(name.Substring(0, name.Length - Extension.Length));
                }
            }
            catch (Exception exception)
            {
                LastError = "Could not list saves: " + exception.Message;
            }

            slots.Sort(StringComparer.Ordinal);
            return slots;
        }

        private string PathFor(string slotId)
        {
            // Slot ids become file names, so anything that could escape the
            // directory is stripped rather than trusted.
            var safe = new System.Text.StringBuilder(slotId.Length);

            for (int i = 0; i < slotId.Length; i++)
            {
                char c = slotId[i];

                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    safe.Append(c);
                }
            }

            if (safe.Length == 0)
            {
                safe.Append("slot");
            }

            return Path.Combine(_directory, safe + Extension);
        }
    }
}
