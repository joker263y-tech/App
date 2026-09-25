using Shadowbound.Core.Combat;
using Shadowbound.Core.Content;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.Simulation;
using Xunit;

namespace Shadowbound.Core.Tests.Simulation
{
    /// <summary>
    /// Tests the quest lifecycle end to end: finishing a quest, being paid for it,
    /// and the next quest in the chain becoming available.
    ///
    /// These exist because of a real defect. A quest only becomes startable once its
    /// prerequisite has been TURNED IN, and nothing in the project ever turned one
    /// in. Every quest after the first was therefore unreachable, its reward was
    /// never granted, and the story could not progress past the opening scene - while
    /// every individual quest test still passed, because each one only ever exercised
    /// a single quest in isolation.
    ///
    /// The lesson is why these tests drive several quests in sequence rather than
    /// checking one.
    /// </summary>
    public class QuestAdvancementTests
    {
        private static GameSession MakeSession(bool autoAdvance = true)
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(20250925),
                WorldBounds.Square(40f));

            GameContent.Populate(session);
            session.AutoAdvanceQuests = autoAdvance;

            return session;
        }

        private static QuestReward RewardOf(GameSession session, string questId)
        {
            return session.Quests.State(questId).Definition.Rewards;
        }

        /// <summary>Finishes the opening quest the same way the runtime does.</summary>
        private static void CompleteOpeningQuest(GameSession session)
        {
            // EnterRegion does an access check first, then exactly this. Reporting the
            // event directly keeps the test about quest state rather than world gating,
            // which RegionTests covers separately.
            session.Quests.Report(QuestEvent.Reach(GameContent.RegionWilds));
        }

        // ------------------------------ the defect itself --------------------------

        [Fact]
        public void WithoutAutoAdvance_TheStoryStallsAfterTheOpeningQuest()
        {
            // This is the behaviour the fix removes, asserted so it cannot come back.
            // It documents precisely why AutoAdvanceQuests exists.
            GameSession session = MakeSession(autoAdvance: false);

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);

            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf(GameContent.QuestArrival));
            Assert.Equal(0, session.AdvanceQuests());

            // Work done, never paid, and the next quest stays locked forever.
            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf(GameContent.QuestArrival));
            Assert.Equal(QuestStatus.Locked, session.Quests.StatusOf(GameContent.QuestFirstBlood));
            Assert.Equal(0, session.Progression.TotalExperience);
        }

        // ------------------------------ the fix -----------------------------------

        [Fact]
        public void CompletingAQuest_GrantsItsReward()
        {
            GameSession session = MakeSession();

            QuestReward reward = RewardOf(session, GameContent.QuestArrival);

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);
            session.AdvanceQuests();

            Assert.Equal(QuestStatus.TurnedIn, session.Quests.StatusOf(GameContent.QuestArrival));
            Assert.Equal(reward.Experience, session.Progression.TotalExperience);
            Assert.Equal(reward.AttributePoints, session.Progression.UnspentAttributePoints);

            Assert.NotNull(reward.Items);

            for (int i = 0; i < reward.Items.Length; i++)
            {
                Assert.Equal(
                    reward.Items[i].Quantity,
                    session.Inventory.Count(reward.Items[i].ItemId));
            }
        }

        [Fact]
        public void CompletingAQuest_OffersTheNextOne()
        {
            GameSession session = MakeSession();

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);
            session.AdvanceQuests();

            // The chain is real: this is the second quest, and it exists because the
            // first was turned in rather than merely completed.
            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestFirstBlood));
        }

        [Fact]
        public void AFreshSession_StartsTheOpeningQuest()
        {
            GameSession session = MakeSession();

            // Nothing is completed, but the opening quest has no prerequisites, so it
            // is offered immediately. Without this a player would arrive with no task.
            Assert.Equal(1, session.AdvanceQuests());
            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestArrival));
        }

        [Fact]
        public void Rewards_AreNotGrantedTwice()
        {
            GameSession session = MakeSession();

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);
            session.AdvanceQuests();

            int experience = session.Progression.TotalExperience;
            int points = session.Progression.UnspentAttributePoints;

            // Nothing is outstanding now, so advancing again must be a no-op.
            Assert.Equal(0, session.AdvanceQuests());

            Assert.Equal(experience, session.Progression.TotalExperience);
            Assert.Equal(points, session.Progression.UnspentAttributePoints);
        }

        // --------------------------- the whole chain, in order ---------------------

        [Fact]
        public void KillingTheRequiredCreatures_ClaimsTheQuestAndUnlocksTheNext()
        {
            GameSession session = MakeSession();

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);
            session.AdvanceQuests();

            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestFirstBlood));

            EnemyArchetype walker = GameContent.FindArchetype(GameContent.ArchetypeHollowWalker);
            Assert.NotNull(walker);

            // The quest asks for four. Killing exactly that many must finish it
            // without a fifth, which is what the objective count is for.
            for (int i = 0; i < 4; i++)
            {
                Combatant victim = walker.Create("walker-" + i, new Float3(i, 0f, 6f), 1);
                session.ReportDefeat(victim);
            }

            Assert.Equal(QuestStatus.TurnedIn, session.Quests.StatusOf(GameContent.QuestFirstBlood));
            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestDescent));

            // The reward included the Warden's Blade. Before the fix it was never
            // handed over, so the player finished the second quest with no weapon.
            Assert.Equal(1, session.Inventory.Count(GameContent.ItemWardensBlade));
        }

        [Fact]
        public void KillingFewerThanRequired_DoesNotCompleteTheQuest()
        {
            GameSession session = MakeSession();

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);
            session.AdvanceQuests();

            EnemyArchetype walker = GameContent.FindArchetype(GameContent.ArchetypeHollowWalker);
            Assert.NotNull(walker);

            for (int i = 0; i < 3; i++)
            {
                session.ReportDefeat(walker.Create("walker-" + i, new Float3(i, 0f, 6f), 1));
            }

            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestFirstBlood));
            Assert.Equal(QuestStatus.Locked, session.Quests.StatusOf(GameContent.QuestDescent));
            Assert.Equal(0, session.Inventory.Count(GameContent.ItemWardensBlade));
        }

        [Fact]
        public void CollectingTheRequiredItems_ClaimsTheQuest()
        {
            GameSession session = MakeSession();

            // Walk the chain to the collect quest: opening, then four kills.
            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);
            session.AdvanceQuests();

            EnemyArchetype walker = GameContent.FindArchetype(GameContent.ArchetypeHollowWalker);
            Assert.NotNull(walker);

            for (int i = 0; i < 4; i++)
            {
                session.ReportDefeat(walker.Create("walker-" + i, new Float3(i, 0f, 6f), 1));
            }

            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestDescent));

            // 'Descent' is a reach objective, so the ruins must be entered before the
            // collect quest is offered at all.
            session.Quests.Report(QuestEvent.Reach(GameContent.RegionRuins));
            session.AdvanceQuests();

            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestSplinters));

            QuestReward reward = RewardOf(session, GameContent.QuestSplinters);
            int required = 3;

            // Collect objectives count what the player HOLDS, so the items are picked
            // up rather than reported as an event.
            Assert.Equal(0, session.Inventory.Count(GameContent.ItemVeilSplinter));

            session.GrantItem(GameContent.ItemVeilSplinter, required);

            Assert.Equal(QuestStatus.TurnedIn, session.Quests.StatusOf(GameContent.QuestSplinters));
            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestSentinel));

            for (int i = 0; i < reward.Items.Length; i++)
            {
                // The reward is the Ember Relic, on top of the splinters still held.
                Assert.True(session.Inventory.Count(reward.Items[i].ItemId) >= reward.Items[i].Quantity);
            }
        }

        [Fact]
        public void DefeatingTheBoss_CompletesTheFinalQuest()
        {
            GameSession session = MakeSession();

            // Drive the chain with the debug hook, since exercising four fights and
            // three regions here would duplicate the tests above.
            session.Quests.ForceComplete(GameContent.QuestArrival);
            session.AdvanceQuests();
            session.Quests.ForceComplete(GameContent.QuestFirstBlood);
            session.AdvanceQuests();
            session.Quests.ForceComplete(GameContent.QuestDescent);
            session.AdvanceQuests();
            session.Quests.ForceComplete(GameContent.QuestSplinters);
            session.AdvanceQuests();

            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestSentinel));

            EnemyArchetype sentinel = GameContent.FindArchetype(GameContent.ArchetypeAshenSentinel);
            Assert.NotNull(sentinel);
            Assert.True(sentinel.IsBoss);

            session.ReportDefeat(sentinel.Create("sentinel-boss", new Float3(0f, 0f, 20f), 10));

            Assert.Equal(QuestStatus.TurnedIn, session.Quests.StatusOf(GameContent.QuestSentinel));

            // The final chapter is complete once its quest is turned in, which is
            // the end of the story as authored.
            Assert.True(session.Chapters.IsComplete(GameContent.ChapterTheUmbralSanctum));
        }

        // --------------------------------- persistence -----------------------------

        [Fact]
        public void LoadingASave_DoesNotGrantQuestRewardsAgain()
        {
            GameSession session = MakeSession();

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);
            session.AdvanceQuests();

            int experience = session.Progression.TotalExperience;
            int points = session.Progression.UnspentAttributePoints;
            QuestReward reward = RewardOf(session, GameContent.QuestArrival);
            int draughts = session.Inventory.Count(reward.Items[0].ItemId);

            SaveGame save = session.CreateSave();

            GameSession restored = MakeSession();
            restored.ApplySave(save);

            // A save written after the reward was claimed must not pay out twice.
            Assert.Equal(experience, restored.Progression.TotalExperience);
            Assert.Equal(points, restored.Progression.UnspentAttributePoints);
            Assert.Equal(draughts, restored.Inventory.Count(reward.Items[0].ItemId));
            Assert.Equal(QuestStatus.TurnedIn, restored.Quests.StatusOf(GameContent.QuestArrival));
        }

        [Fact]
        public void LoadingASaveOfAFinishedQuest_StillOffersTheNextOne()
        {
            GameSession session = MakeSession();

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);
            session.AdvanceQuests();

            SaveGame save = session.CreateSave();

            GameSession restored = MakeSession();
            restored.ApplySave(save);

            // The second quest was active when the player quit, so resuming must put
            // them back on it rather than at the start of the game.
            Assert.Equal(QuestStatus.Active, restored.Quests.StatusOf(GameContent.QuestFirstBlood));
        }

        [Fact]
        public void ASaveWithAFinishedButUnclaimedQuest_PaysOutOnLoad()
        {
            GameSession session = MakeSession();

            Assert.True(session.Quests.TryStart(GameContent.QuestArrival));
            CompleteOpeningQuest(session);

            // Saved at exactly the wrong moment: the work is done, the reward is not
            // yet claimed. This would otherwise strand the reward permanently.
            SaveGame save = session.CreateSave();

            Assert.Equal(QuestStatus.Completed, save.Quests[0].Status);

            GameSession restored = MakeSession();
            restored.ApplySave(save);

            Assert.Equal(QuestStatus.TurnedIn, restored.Quests.StatusOf(GameContent.QuestArrival));
            Assert.True(restored.Progression.TotalExperience > 0);
        }
    }
}
