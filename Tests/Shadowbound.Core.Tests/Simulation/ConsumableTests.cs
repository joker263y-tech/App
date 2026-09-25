using Shadowbound.Core.Combat;
using Shadowbound.Core.Content;
using Shadowbound.Core.Items;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.World;
using Xunit;

namespace Shadowbound.Core.Tests.Simulation
{
    /// <summary>
    /// Tests the consumable path end to end: using an item applies its effects and
    /// takes it out of the bag.
    ///
    /// These exist because of a real defect. The quest rewards handed out ember
    /// draughts - five of them across the opening chain - and nothing anywhere in
    /// the game could drink one. The core knew what a consumable was, the content
    /// carried effects, and no code path ever connected the two. A reward item
    /// that does nothing is the same as no reward at all.
    /// </summary>
    public class ConsumableTests
    {
        private static GameSession MakeSession()
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(5150),
                WorldBounds.Square(40f));

            GameContent.Populate(session);
            return session;
        }

        // ------------------------------- the defect ------------------------------

        [Fact]
        public void UsingADraught_HealsAndSpendsTheItem()
        {
            GameSession session = MakeSession();
            session.GrantItem(GameContent.ItemEmberDraught, 2);

            session.Player.Vitals.ApplyDamage(200f, null);
            float healthBefore = session.Player.Vitals.Health;

            Assert.True(session.TryUseConsumable(GameContent.ItemEmberDraught, out ConsumableFailure failure));
            Assert.Equal(ConsumableFailure.None, failure);

            // The draught's authored effect is 120 health.
            Assert.Equal(healthBefore + 120f, session.Player.Vitals.Health, 3);
            Assert.Equal(1, session.Inventory.Count(GameContent.ItemEmberDraught));
        }

        [Fact]
        public void UsingADraughtAtFullHealth_StillSpendsTheItem()
        {
            // Deliberate behaviour, asserted so it cannot drift: effects clamp
            // rather than refuse. Whether a use is worth it is the player's call.
            GameSession session = MakeSession();
            session.GrantItem(GameContent.ItemEmberDraught, 1);

            float healthBefore = session.Player.Vitals.Health;

            Assert.True(session.TryUseConsumable(GameContent.ItemEmberDraught, out _));
            Assert.Equal(healthBefore, session.Player.Vitals.Health, 3);
            Assert.Equal(0, session.Inventory.Count(GameContent.ItemEmberDraught));
        }

        [Fact]
        public void UsingADraught_RestoresStaminaToo()
        {
            GameSession session = MakeSession();
            session.GrantItem(GameContent.ItemEmberDraught, 1);

            Assert.True(session.Player.Vitals.TrySpendStamina(60f));
            float staminaBefore = session.Player.Vitals.Stamina;

            Assert.True(session.TryUseConsumable(GameContent.ItemEmberDraught, out _));

            // The draught's second authored effect is 40 stamina.
            Assert.Equal(staminaBefore + 40f, session.Player.Vitals.Stamina, 3);
        }

        [Fact]
        public void TheLastDraught_LeavesTheBagEmpty()
        {
            GameSession session = MakeSession();
            session.GrantItem(GameContent.ItemEmberDraught, 1);

            Assert.True(session.TryUseConsumable(GameContent.ItemEmberDraught, out _));
            Assert.False(session.TryUseConsumable(GameContent.ItemEmberDraught, out ConsumableFailure failure));
            Assert.Equal(ConsumableFailure.NotHeld, failure);
        }

        // ------------------------------- refusals --------------------------------

        [Fact]
        public void UsingAnUnknownItem_IsRefused()
        {
            GameSession session = MakeSession();

            Assert.False(session.TryUseConsumable("not-an-item", out ConsumableFailure failure));
            Assert.Equal(ConsumableFailure.UnknownItem, failure);
        }

        [Fact]
        public void UsingAMaterial_IsRefused()
        {
            GameSession session = MakeSession();
            session.GrantItem(GameContent.ItemAsh, 5);

            Assert.False(session.TryUseConsumable(GameContent.ItemAsh, out ConsumableFailure failure));
            Assert.Equal(ConsumableFailure.NotConsumable, failure);
            Assert.Equal(5, session.Inventory.Count(GameContent.ItemAsh));
        }

        [Fact]
        public void UsingEquipment_IsRefused()
        {
            GameSession session = MakeSession();
            session.GrantItem(GameContent.ItemWardensBlade, 1);

            Assert.False(session.TryUseConsumable(GameContent.ItemWardensBlade, out ConsumableFailure failure));
            Assert.Equal(ConsumableFailure.NotConsumable, failure);
            Assert.True(session.Inventory.Has(GameContent.ItemWardensBlade));
        }

        // --------------------------- the other effects ---------------------------

        [Fact]
        public void UsingAStatusItem_AppliesTheStatus()
        {
            GameSession session = MakeSession();

            session.Items.Register(new ItemDefinition
            {
                Id = "tonic-of-wards",
                DisplayName = "Tonic of Wards",
                Kind = ItemKind.Consumable,
                MaxStack = 5,
                Effects = new[] { new ItemEffect(StatusKind.Warded, 0.4f, 8f) }
            });

            session.GrantItem("tonic-of-wards", 1);

            Assert.True(session.TryUseConsumable("tonic-of-wards", out _));
            Assert.True(session.Player.Statuses.Has(StatusKind.Warded));
        }

        [Fact]
        public void UsingAnExperienceItem_GrantsExperience()
        {
            GameSession session = MakeSession();

            session.Items.Register(new ItemDefinition
            {
                Id = "memory-of-ash",
                DisplayName = "Memory of Ash",
                Kind = ItemKind.Consumable,
                MaxStack = 5,
                Effects = new[] { new ItemEffect(EffectKind.GrantExperience, 500f) }
            });

            session.GrantItem("memory-of-ash", 1);

            int experienceBefore = session.Progression.TotalExperience;

            Assert.True(session.TryUseConsumable("memory-of-ash", out _));
            Assert.Equal(experienceBefore + 500, session.Progression.TotalExperience);
        }

        // ------------------------------ the full chain ---------------------------

        [Fact]
        public void TheQuestRewardedDraughts_AreActuallyUsable()
        {
            // The whole chain the defect broke: finish the opening quest, be paid
            // in draughts, and find that the reward can actually be used.
            GameSession session = MakeSession();

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            session.Quests.Report(QuestEvent.Reach(GameContent.RegionWilds));

            // Reporting an event only completes the quest; the runtime's
            // EnterRegion/Update loop is what turns it in and pays it out. Do that
            // step explicitly here so the test stays about the reward chain rather
            // than about world gating.
            Assert.True(session.AdvanceQuests() > 0);

            Assert.True(session.Inventory.Count(GameContent.ItemEmberDraught) > 0);

            session.Player.Vitals.ApplyDamage(300f, null);
            float healthBefore = session.Player.Vitals.Health;

            Assert.True(session.TryUseConsumable(GameContent.ItemEmberDraught, out _));
            Assert.True(session.Player.Vitals.Health > healthBefore);
        }
    }
}
