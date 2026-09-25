using Shadowbound.Core.Content;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.Stats;
using Shadowbound.Core.World;
using Xunit;

namespace Shadowbound.Core.Tests.Simulation
{
    /// <summary>
    /// Tests spending attribute points, and - just as importantly - that spent
    /// points survive being saved and loaded.
    ///
    /// These exist because of a real defect. Quests and levels handed out attribute
    /// points, the HUD counted them, and nothing in the game could spend them. And
    /// once spending exists, persistence matters too: base stats are rebuilt from
    /// the growth table on every load, so without the boosts being part of the
    /// save, every point spent would vanish on the next load.
    /// </summary>
    public class AttributePointTests
    {
        private static GameSession MakeSession()
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(1313),
                WorldBounds.Square(40f));

            GameContent.Populate(session);
            return session;
        }

        // ------------------------------- the defect ------------------------------

        [Fact]
        public void SpendingAPoint_RaisesTheStatAndCostsThePoint()
        {
            GameSession session = MakeSession();
            session.Progression.GrantAttributePoints(2);

            float attackBefore = session.Player.Stats.Get(StatId.AttackPower);

            // Exactly the call the menu makes.
            Assert.True(session.Progression.TrySpendAttributePoint(
                StatId.AttackPower,
                GameContent.AttributeAward(StatId.AttackPower)));

            Assert.Equal(attackBefore + GameContent.AttributeAward(StatId.AttackPower),
                session.Player.Stats.Get(StatId.AttackPower), 3);
            Assert.Equal(1, session.Progression.UnspentAttributePoints);
        }

        [Fact]
        public void SpendingWithoutPoints_IsRefused()
        {
            GameSession session = MakeSession();

            float attackBefore = session.Player.Stats.Get(StatId.AttackPower);

            Assert.False(session.Progression.TrySpendAttributePoint(StatId.AttackPower, 2f));
            Assert.Equal(attackBefore, session.Player.Stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void EveryShippedStat_HasAPositiveAward()
        {
            // The attributes page lists every stat. One without an award would be a
            // row that takes a point and gives nothing back.
            foreach (StatId stat in StatIds.All)
            {
                Assert.True(GameContent.AttributeAward(stat) > 0f, stat + " has no award");
            }
        }

        // ------------------------------ persistence ------------------------------

        [Fact]
        public void SpentPoints_SurviveSavingAndLoading()
        {
            GameSession session = MakeSession();
            session.Progression.GrantAttributePoints(3);

            session.Progression.TrySpendAttributePoint(StatId.MaxHealth, GameContent.AttributeAward(StatId.MaxHealth));
            session.Progression.TrySpendAttributePoint(StatId.MaxHealth, GameContent.AttributeAward(StatId.MaxHealth));
            session.Progression.TrySpendAttributePoint(StatId.CritChance, GameContent.AttributeAward(StatId.CritChance));

            float healthAfterSpending = session.Player.Stats.Get(StatId.MaxHealth);
            float critAfterSpending = session.Player.Stats.Get(StatId.CritChance);

            SaveGame save = session.CreateSave();
            string json = SaveSerializer.Serialize(save);

            var reloaded = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(1),
                WorldBounds.Square(40f));

            reloaded.ApplySave(SaveSerializer.Deserialize(json));

            Assert.Equal(healthAfterSpending, reloaded.Player.Stats.Get(StatId.MaxHealth), 3);
            Assert.Equal(critAfterSpending, reloaded.Player.Stats.Get(StatId.CritChance), 5);
            Assert.Equal(0, reloaded.Progression.UnspentAttributePoints);
        }

        [Fact]
        public void LoadingTheSameSaveTwice_DoesNotStackTheBoosts()
        {
            GameSession session = MakeSession();
            session.Progression.GrantAttributePoints(1);
            session.Progression.TrySpendAttributePoint(StatId.Armor, GameContent.AttributeAward(StatId.Armor));

            SaveGame save = session.CreateSave();
            float armorAfterSpending = session.Player.Stats.Get(StatId.Armor);

            var reloaded = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(1),
                WorldBounds.Square(40f));

            reloaded.ApplySave(save);
            reloaded.ApplySave(SaveSerializer.Deserialize(SaveSerializer.Serialize(save)));

            Assert.Equal(armorAfterSpending, reloaded.Player.Stats.Get(StatId.Armor), 3);
        }

        [Fact]
        public void AnOldSaveWithoutStatBoosts_LoadsWithNoBoosts()
        {
            // A version 1 save predates spending, so the field is simply absent.
            // Missing fields must fall back to a default, not fail the load.
            GameSession session = MakeSession();

            SaveGame save = session.CreateSave();
            save.StatBoosts = System.Array.Empty<float>();

            var reloaded = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(1),
                WorldBounds.Square(40f));

            reloaded.ApplySave(SaveSerializer.Deserialize(SaveSerializer.Serialize(save)));

            Assert.Equal(
                reloaded.Player.Stats.Get(StatId.MaxHealth),
                GameContent.CreatePlayer().Stats.Get(StatId.MaxHealth), 3);
        }

        [Fact]
        public void UnspentPoints_SurviveSavingAndLoading()
        {
            GameSession session = MakeSession();
            session.Progression.GrantAttributePoints(5);
            session.Progression.TrySpendAttributePoint(StatId.Armor, GameContent.AttributeAward(StatId.Armor));

            var reloaded = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(1),
                WorldBounds.Square(40f));

            reloaded.ApplySave(SaveSerializer.Deserialize(SaveSerializer.Serialize(session.CreateSave())));

            Assert.Equal(4, reloaded.Progression.UnspentAttributePoints);
        }
    }
}
