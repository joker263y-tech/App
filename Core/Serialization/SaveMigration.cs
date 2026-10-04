using System;
using System.Collections.Generic;

namespace Shadowbound.Core.Serialization
{
    /// <summary>
    /// Upgrades older save files to the current format.
    ///
    /// Each step upgrades from exactly one version to the next, and they are
    /// applied in sequence. Chaining single-version steps rather than writing a
    /// bespoke path from every old version to the current one means adding a new
    /// format version costs one step, not one step per historical version.
    ///
    /// There are currently no registered steps because version 1 is the first
    /// shipped format. The mechanism exists now, and is tested, so that the first
    /// real format change is a small addition rather than a redesign.
    /// </summary>
    public static class SaveMigration
    {
        private static readonly Dictionary<int, Action<SaveGame>> Steps =
            new Dictionary<int, Action<SaveGame>>();

        /// <summary>Format version this build writes and understands.</summary>
        public static int CurrentVersion
        {
            get { return SaveGame.CurrentVersion; }
        }

        /// <summary>Registers the upgrade from <paramref name="fromVersion"/> to the version after it.</summary>
        public static void Register(int fromVersion, Action<SaveGame> upgrade)
        {
            if (upgrade == null)
            {
                throw new ArgumentNullException(nameof(upgrade));
            }

            if (fromVersion < 1 || fromVersion > CurrentVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fromVersion),
                    "Migration steps must upgrade from an existing version.");
            }

            Steps[fromVersion] = upgrade;
        }

        public static bool HasStep(int fromVersion)
        {
            return Steps.ContainsKey(fromVersion);
        }

        /// <summary>Removes all registered steps. For test isolation.</summary>
        public static void ClearRegisteredSteps()
        {
            Steps.Clear();
        }

        public static bool IsSupported(int version)
        {
            return version >= 1 && version <= CurrentVersion;
        }

        /// <summary>
        /// Brings a loaded save up to the current version, mutating it in place
        /// and returning it.
        ///</summary>
        public static SaveGame Migrate(SaveGame save)
        {
            return Migrate(save, CurrentVersion, Steps);
        }

        /// <summary>
        /// The migration walk itself, with the target version and step table
        /// supplied explicitly.
        ///
        /// Separated from the public entry point so the chaining behaviour can be
        /// tested against a synthetic version history. Real format steps only
        /// appear once per shipped format change, so without this the loop that
        /// applies them would sit untested until the first one caused a problem.
        ///
        /// A save from a newer build is rejected outright rather than loaded
        /// best-effort. Reading a format this build does not understand could
        /// silently discard whatever it does not recognise, which turns "your
        /// save is from a newer version" into permanent data loss.
        /// </summary>
        internal static SaveGame Migrate(
            SaveGame save,
            int targetVersion,
            IReadOnlyDictionary<int, Action<SaveGame>> steps)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            if (save.Version > targetVersion)
            {
                throw new SaveFormatException(
                    "Save version " + save.Version + " is newer than this build supports ("
                    + targetVersion + ").");
            }

            if (save.Version < 1)
            {
                throw new SaveFormatException("Save version " + save.Version + " is not valid.");
            }

            // Steps are applied in strict sequence. A missing step is fatal rather
            // than skipped: continuing would load a half-upgraded save, which is
            // worse than refusing it.
            while (save.Version < targetVersion)
            {
                Action<SaveGame> step;
                if (steps == null || !steps.TryGetValue(save.Version, out step) || step == null)
                {
                    throw new SaveFormatException(
                        "No migration path from save version " + save.Version + ".");
                }

                step(save);
                save.Version++;
            }

            return save;
        }
    }
}
