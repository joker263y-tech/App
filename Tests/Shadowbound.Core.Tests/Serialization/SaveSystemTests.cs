using System.Collections.Generic;
using Shadowbound.Core.Items;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Serialization;
using Xunit;

namespace Shadowbound.Core.Tests.Serialization
{
    public class SaveSerializerTests
    {
        private static SaveGame MakeSave()
        {
            return new SaveGame
            {
                SlotId = "slot-1",
                ProfileName = "Ashen Wanderer",
                DifficultyId = "umbral",
                Version = SaveGame.CurrentVersion,
                SavedAtUnixSeconds = 1_800_000_000L,
                PlaytimeSeconds = 4210.5f,
                RegionId = "hollow-ruins",
                Position = new Float3(12.5f, 0.25f, -8f),
                FacingDegrees = 137.5f,
                TotalExperience = 4210,
                UnspentAttributePoints = 3,
                Inventory = new List<ItemStack>
                {
                    new ItemStack("ash", 12),
                    new ItemStack("bone-shard", 4)
                },
                Equipment = new List<ItemStack> { new ItemStack("umbral-blade", 1) },
                Quests = new List<QuestSnapshot>
                {
                    new QuestSnapshot("q-hollow", QuestStatus.Active, new[] { 2, 0 }),
                    new QuestSnapshot("q-arrival", QuestStatus.TurnedIn, new[] { 1 })
                },
                DiscoveredRegions = new List<string> { "camp", "wilds", "hollow-ruins" },
                RngState = 9876543210987654321UL,
                RngIncrement = 1442695040888963407UL
            };
        }

        [Fact]
        public void RoundTrip_PreservesEveryField()
        {
            SaveGame original = MakeSave();

            SaveGame restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(original));

            Assert.Equal(original.SlotId, restored.SlotId);
            Assert.Equal(original.ProfileName, restored.ProfileName);
            Assert.Equal(original.DifficultyId, restored.DifficultyId);
            Assert.Equal(original.Version, restored.Version);
            Assert.Equal(original.SavedAtUnixSeconds, restored.SavedAtUnixSeconds);
            Assert.Equal(original.PlaytimeSeconds, restored.PlaytimeSeconds, 3);
            Assert.Equal(original.RegionId, restored.RegionId);
            Assert.Equal(original.Position, restored.Position);
            Assert.Equal(original.FacingDegrees, restored.FacingDegrees, 3);
            Assert.Equal(original.TotalExperience, restored.TotalExperience);
            Assert.Equal(original.UnspentAttributePoints, restored.UnspentAttributePoints);
        }

        [Fact]
        public void RoundTrip_PreservesTheFullSixtyFourBitGeneratorState()
        {
            // A JSON number is a double and cannot represent every 64-bit integer
            // exactly, so the state is written as a string. Storing it numerically
            // would silently desynchronise deterministic replays.
            SaveGame original = MakeSave();

            SaveGame restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(original));

            Assert.Equal(original.RngState, restored.RngState);
            Assert.Equal(original.RngIncrement, restored.RngIncrement);
            Assert.Equal(9876543210987654321UL, restored.RngState);
        }

        [Fact]
        public void GeneratorState_IsWrittenAsAString()
        {
            string json = SaveSerializer.Serialize(MakeSave());

            Assert.Contains("\"rngState\": \"9876543210987654321\"", json);
        }

        [Fact]
        public void GeneratorState_StillReadsFromALegacyNumericValue()
        {
            // A hand-edited save using a plain number must still load.
            string json = SaveSerializer.Serialize(MakeSave())
                .Replace("\"rngState\": \"9876543210987654321\"", "\"rngState\": 12345");

            SaveGame restored = SaveSerializer.Deserialize(json);

            Assert.Equal(12345UL, restored.RngState);
        }

        [Fact]
        public void RoundTrip_PreservesInventoryAndEquipment()
        {
            SaveGame restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(MakeSave()));

            Assert.Equal(2, restored.Inventory.Count);
            Assert.Equal("ash", restored.Inventory[0].ItemId);
            Assert.Equal(12, restored.Inventory[0].Quantity);
            Assert.Single(restored.Equipment);
            Assert.Equal("umbral-blade", restored.Equipment[0].ItemId);
        }

        [Fact]
        public void RoundTrip_PreservesQuestProgressAndStatus()
        {
            SaveGame restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(MakeSave()));

            Assert.Equal(2, restored.Quests.Count);
            Assert.Equal("q-hollow", restored.Quests[0].QuestId);
            Assert.Equal(QuestStatus.Active, restored.Quests[0].Status);
            Assert.Equal(new[] { 2, 0 }, restored.Quests[0].Progress);
            Assert.Equal(QuestStatus.TurnedIn, restored.Quests[1].Status);
        }

        [Fact]
        public void RoundTrip_PreservesDiscoveredRegions()
        {
            SaveGame restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(MakeSave()));

            Assert.Equal(3, restored.DiscoveredRegions.Count);
            Assert.Contains("wilds", restored.DiscoveredRegions);
        }

        [Fact]
        public void Deserialize_RejectsAForeignDocument()
        {
            var exception = Assert.Throws<SaveFormatException>(
                () => SaveSerializer.Deserialize("{\"format\": \"something-else\", \"version\": 1, \"data\": {}}"));

            Assert.Contains("Not a Shadowbound save", exception.Message);
        }

        [Fact]
        public void Deserialize_RejectsANewerFormatVersion()
        {
            // Loading best-effort would silently discard whatever this build does
            // not understand, turning a version mismatch into data loss.
            string json = SaveSerializer.Serialize(MakeSave())
                .Replace("\"version\": 1", "\"version\": 99");

            var exception = Assert.Throws<SaveFormatException>(() => SaveSerializer.Deserialize(json));

            Assert.Contains("newer version", exception.Message);
        }

        [Fact]
        public void Deserialize_RejectsAVersionlessDocument()
        {
            Assert.Throws<SaveFormatException>(
                () => SaveSerializer.Deserialize("{\"format\": \"shadowbound.save\", \"data\": {}}"));
        }

        [Fact]
        public void Deserialize_RejectsAFileWithNoContents()
        {
            Assert.Throws<SaveFormatException>(
                () => SaveSerializer.Deserialize("{\"format\": \"shadowbound.save\", \"version\": 1}"));
        }

        [Fact]
        public void Deserialize_RejectsCorruptJson()
        {
            Assert.Throws<SaveFormatException>(() => SaveSerializer.Deserialize("{not json"));
        }

        [Fact]
        public void Deserialize_IgnoresUnknownFields()
        {
            // A save written by a newer but compatible build must still load.
            string json = SaveSerializer.Serialize(MakeSave())
                .Replace("\"regionId\"", "\"futureFeature\": {\"a\": 1},\n  \"regionId\"");

            SaveGame restored = SaveSerializer.Deserialize(json);

            Assert.Equal("hollow-ruins", restored.RegionId);
        }

        [Fact]
        public void Deserialize_DefaultsMissingFields()
        {
            // A field added in a later build must not make older saves unloadable.
            string json = "{\"format\": \"shadowbound.save\", \"version\": 1, \"data\": {\"slotId\": \"s\"}}";

            SaveGame restored = SaveSerializer.Deserialize(json);

            Assert.Equal("s", restored.SlotId);
            Assert.Equal("Wanderer", restored.ProfileName);
            Assert.Equal(0, restored.TotalExperience);
            Assert.Empty(restored.Inventory);
            Assert.Empty(restored.Quests);
            Assert.Equal(Float3.Zero, restored.Position);
        }

        [Fact]
        public void Deserialize_SkipsMalformedInventoryEntries()
        {
            string json = "{\"format\": \"shadowbound.save\", \"version\": 1, \"data\": {"
                + "\"inventory\": [{\"id\": \"ash\", \"qty\": 2}, {\"qty\": 5}, {\"id\": \"bone\", \"qty\": 0}]}}";

            SaveGame restored = SaveSerializer.Deserialize(json);

            Assert.Single(restored.Inventory);
            Assert.Equal("ash", restored.Inventory[0].ItemId);
        }

        [Fact]
        public void Deserialize_CoercesAnOutOfRangeQuestStatus()
        {
            string json = "{\"format\": \"shadowbound.save\", \"version\": 1, \"data\": {"
                + "\"quests\": [{\"id\": \"q\", \"status\": 250, \"progress\": []}]}}";

            SaveGame restored = SaveSerializer.Deserialize(json);

            Assert.Equal(QuestStatus.Locked, restored.Quests[0].Status);
        }

        [Fact]
        public void TryDeserialize_ReportsFailureWithoutThrowing()
        {
            bool ok = SaveSerializer.TryDeserialize("{broken", out SaveGame save, out string error);

            Assert.False(ok);
            Assert.Null(save);
            Assert.NotNull(error);
        }

        [Fact]
        public void Serialize_RejectsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => SaveSerializer.Serialize(null));
        }

        [Fact]
        public void Clone_ProducesAnIndependentCopy()
        {
            SaveGame original = MakeSave();

            SaveGame copy = original.Clone();
            copy.Inventory.Add(new ItemStack("extra", 1));
            copy.Quests[0].Progress[0] = 99;
            copy.SlotId = "changed";

            Assert.Equal(2, original.Inventory.Count);
            Assert.Equal(2, original.Quests[0].Progress[0]);
            Assert.Equal("slot-1", original.SlotId);
        }
    }

    public class SaveMigrationTests
    {
        public SaveMigrationTests()
        {
            SaveMigration.ClearRegisteredSteps();
        }

        [Fact]
        public void ACurrentVersionSave_PassesThroughUnchanged()
        {
            var save = new SaveGame { Version = SaveGame.CurrentVersion, SlotId = "s" };

            SaveMigration.Migrate(save);

            Assert.Equal(SaveGame.CurrentVersion, save.Version);
        }

        [Fact]
        public void ANewerVersionSave_IsRejected()
        {
            var save = new SaveGame { Version = SaveGame.CurrentVersion + 1 };

            Assert.Throws<SaveFormatException>(() => SaveMigration.Migrate(save));
        }

        [Fact]
        public void AVersionBelowOne_IsRejected()
        {
            Assert.Throws<SaveFormatException>(() => SaveMigration.Migrate(new SaveGame { Version = 0 }));
        }

        [Fact]
        public void Migrate_RejectsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => SaveMigration.Migrate(null));
        }

        [Fact]
        public void Steps_AreAppliedInSequence()
        {
            // Version 1 is the first shipped format, so no real step exists yet.
            // A synthetic history exercises the walk itself, so the machinery is
            // proven before the first real format change depends on it.
            var steps = new Dictionary<int, System.Action<SaveGame>>
            {
                { 1, save => save.ProfileName = "after-step-1" },
                { 2, save => save.RegionId = "after-step-2" }
            };

            var save = new SaveGame { Version = 1, ProfileName = "before" };

            SaveMigration.Migrate(save, 3, steps);

            Assert.Equal("after-step-1", save.ProfileName);
            Assert.Equal("after-step-2", save.RegionId);
            Assert.Equal(3, save.Version);
        }

        [Fact]
        public void AMissingStep_IsReportedRatherThanSkipped()
        {
            // Skipping would silently load a half-upgraded save, which is worse
            // than refusing it outright.
            var steps = new Dictionary<int, System.Action<SaveGame>>
            {
                { 1, save => save.ProfileName = "after-step-1" }
            };

            var save = new SaveGame { Version = 2 };

            Assert.Throws<SaveFormatException>(() => SaveMigration.Migrate(save, 3, steps));
            Assert.Equal(2, save.Version);
        }

        [Fact]
        public void StartingAboveTheTargetVersion_IsRejected()
        {
            var save = new SaveGame { Version = 5 };

            Assert.Throws<SaveFormatException>(
                () => SaveMigration.Migrate(save, 3, new Dictionary<int, System.Action<SaveGame>>()));
        }

        [Fact]
        public void Register_RejectsAVersionThisBuildDoesNotHave()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SaveMigration.Register(0, save => { }));
            Assert.Throws<System.ArgumentNullException>(() => SaveMigration.Register(1, null));
        }

        [Fact]
        public void HasStep_ReflectsWhatWasRegistered()
        {
            SaveMigration.ClearRegisteredSteps();

            Assert.False(SaveMigration.HasStep(1));
            SaveMigration.Register(1, save => save.ProfileName = "x");
            Assert.True(SaveMigration.HasStep(1));
        }

        [Fact]
        public void IsSupported_DescribesTheSupportedRange()
        {
            Assert.True(SaveMigration.IsSupported(1));
            Assert.False(SaveMigration.IsSupported(0));
            Assert.False(SaveMigration.IsSupported(SaveGame.CurrentVersion + 1));
        }
    }

    public class SaveSlotManagerTests
    {
        private static SaveGame MakeSave(string slotId = "slot-1")
        {
            return new SaveGame
            {
                SlotId = slotId,
                ProfileName = "Tester",
                RegionId = "wilds",
                TotalExperience = 500,
                Inventory = new List<ItemStack> { new ItemStack("ash", 3) }
            };
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var storage = new InMemorySaveStorage();
            var manager = new SaveSlotManager(storage);

            Assert.True(manager.Save(MakeSave()).Success);

            SaveResult result = manager.Load("slot-1", out SaveGame loaded);

            Assert.True(result.Success);
            Assert.Equal("Tester", loaded.ProfileName);
            Assert.Equal(500, loaded.TotalExperience);
            Assert.Equal(3, loaded.Inventory[0].Quantity);
        }

        [Fact]
        public void Save_StampsTheVersion()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage());
            var save = MakeSave();
            save.Version = 0;

            manager.Save(save);

            Assert.Equal(SaveGame.CurrentVersion, save.Version);
        }

        [Fact]
        public void Save_StampsTheTimestampWhenAClockIsSupplied()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage(), () => 42L);
            var save = MakeSave();

            manager.Save(save);

            Assert.Equal(42L, save.SavedAtUnixSeconds);
        }

        [Fact]
        public void Save_WithoutAClock_LeavesTheTimestampAlone()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage());
            var save = MakeSave();
            save.SavedAtUnixSeconds = 777L;

            manager.Save(save);

            Assert.Equal(777L, save.SavedAtUnixSeconds);
        }

        [Fact]
        public void Save_RefusesASaveWithNoSlot()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage());

            SaveResult result = manager.Save(new SaveGame());

            Assert.False(result.Success);
            Assert.Contains("slot", result.Error);
        }

        [Fact]
        public void Save_RefusesNull()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage());

            Assert.False(manager.Save(null).Success);
        }

        [Fact]
        public void Load_OfAMissingSlot_FailsWithAReadableMessage()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage());

            SaveResult result = manager.Load("nope", out SaveGame loaded);

            Assert.False(result.Success);
            Assert.Null(loaded);
            Assert.Contains("no save", result.Error);
        }

        [Fact]
        public void Load_OfACorruptSlot_FailsWithoutThrowing()
        {
            var storage = new InMemorySaveStorage();
            storage.Write("slot-1", "this is not a save file");

            var manager = new SaveSlotManager(storage);
            SaveResult result = manager.Load("slot-1", out SaveGame loaded);

            Assert.False(result.Success);
            Assert.Null(loaded);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void Load_OfASaveFromANewerBuild_FailsWithAReadableMessage()
        {
            var storage = new InMemorySaveStorage();
            storage.Write("slot-1", "{\"format\": \"shadowbound.save\", \"version\": 99, \"data\": {}}");

            var manager = new SaveSlotManager(storage);
            SaveResult result = manager.Load("slot-1", out SaveGame loaded);

            Assert.False(result.Success);
            Assert.Null(loaded);
            Assert.Contains("newer", result.Error);
        }

        [Fact]
        public void Load_RunsMigration()
        {
            var storage = new InMemorySaveStorage();
            SaveGame save = MakeSave();
            storage.Write("slot-1", SaveSerializer.Serialize(save));

            var manager = new SaveSlotManager(storage);
            SaveResult result = manager.Load("slot-1", out SaveGame loaded);

            Assert.True(result.Success);
            Assert.Equal(SaveGame.CurrentVersion, loaded.Version);
        }

        [Fact]
        public void ExistsAndListSlots_ReflectWhatWasWritten()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage());

            Assert.False(manager.Exists("slot-1"));
            manager.Save(MakeSave("slot-1"));
            manager.Save(MakeSave("slot-2"));

            Assert.True(manager.Exists("slot-1"));
            Assert.Equal(new[] { "slot-1", "slot-2" }, manager.ListSlots());
        }

        [Fact]
        public void Delete_RemovesTheSlot()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage());
            manager.Save(MakeSave("slot-1"));

            Assert.True(manager.Delete("slot-1").Success);
            Assert.False(manager.Exists("slot-1"));
            Assert.False(manager.Delete("slot-1").Success);
        }

        [Fact]
        public void SavingAgain_OverwritesTheSameSlot()
        {
            var manager = new SaveSlotManager(new InMemorySaveStorage());
            SaveGame first = MakeSave("slot-1");
            first.ProfileName = "First";
            manager.Save(first);

            SaveGame second = MakeSave("slot-1");
            second.ProfileName = "Second";
            manager.Save(second);

            manager.Load("slot-1", out SaveGame loaded);

            Assert.Equal("Second", loaded.ProfileName);
            Assert.Single(manager.ListSlots());
        }

        [Fact]
        public void SlotManager_RejectsNullStorage()
        {
            Assert.Throws<System.ArgumentNullException>(() => new SaveSlotManager(null));
        }

        [Fact]
        public void InMemoryStorage_RejectsAnEmptySlotId()
        {
            var storage = new InMemorySaveStorage();

            Assert.Throws<System.ArgumentException>(() => storage.Write("", "data"));
        }
    }
}
