using Shadowbound.Core.Combat;
using Shadowbound.Core.Content;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.World;
using Xunit;

namespace Shadowbound.Core.Tests.Simulation
{
    /// <summary>
    /// Tests moving between regions.
    ///
    /// Two things are worth pinning down here.
    ///
    /// First, the rule that governs travel. <c>WorldGraph.CanEnter</c> answers only
    /// "is the chapter gate open" and knows nothing about where the player is standing,
    /// so a caller that relied on it alone could walk from the camp straight into the
    /// final sanctum and the region graph would mean nothing. <c>CanTravelTo</c> is the
    /// rule that remembers adjacency and discovery, and it is tested here.
    ///
    /// Second, that the story is actually completable. Two quests ask the player to
    /// reach a specific region, and a region they cannot get to is a quest they cannot
    /// finish - which is how the whole story ended up unreachable before. These tests
    /// walk the required route in order and assert every step is permitted.
    /// </summary>
    public class RegionTravelTests
    {
        private static GameSession MakeSession()
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(4242),
                WorldBounds.Square(40f));

            GameContent.Populate(session);

            return session;
        }

        /// <summary>Drives the session to the point where the named region is the objective.</summary>
        private static void CompleteUpTo(GameSession session, string questId)
        {
            session.Quests.TryStart(GameContent.QuestArrival);
            session.AdvanceQuests();

            if (questId == GameContent.QuestArrival)
            {
                return;
            }

            session.Quests.ForceComplete(GameContent.QuestArrival);
            session.AdvanceQuests();

            if (questId == GameContent.QuestFirstBlood)
            {
                return;
            }

            session.Quests.ForceComplete(GameContent.QuestFirstBlood);
            session.AdvanceQuests();
        }

        /// <summary>Completes every quest, which is what opens the chapter gates.</summary>
        private static void FinishTheStory(GameSession session)
        {
            string[] chain =
            {
                GameContent.QuestArrival,
                GameContent.QuestFirstBlood,
                GameContent.QuestDescent,
                GameContent.QuestSplinters,
                GameContent.QuestSentinel
            };

            session.Quests.TryStart(GameContent.QuestArrival);

            for (int i = 0; i < chain.Length; i++)
            {
                session.Quests.ForceComplete(chain[i]);
                session.AdvanceQuests();
            }
        }

        // ------------------------------- the travel rule ---------------------------

        [Fact]
        public void APlayerStartsInTheCamp()
        {
            Assert.Equal(GameContent.RegionCamp, MakeSession().RegionId);
        }

        [Fact]
        public void MovingToANeighbour_IsAllowed()
        {
            GameSession session = MakeSession();

            Assert.True(session.CanTravelTo(GameContent.RegionWilds, out AccessFailure failure));
            Assert.Equal(AccessFailure.None, failure);

            Assert.True(session.TryTravelTo(GameContent.RegionWilds, out _));
            Assert.Equal(GameContent.RegionWilds, session.RegionId);
        }

        [Fact]
        public void MovingToAnUnconnectedRegion_IsRefused()
        {
            GameSession session = MakeSession();

            // The sanctum is nowhere near the camp and has not been visited. If this
            // were allowed, the region graph would be decorative. Which of the two
            // rules refuses it is not the point here - the sanctum is both gated and
            // unreachable; AnOpenChapterGate_DoesNotBypassTheWorldGraph pins down
            // connectivity specifically.
            Assert.False(session.CanTravelTo(GameContent.RegionSanctum, out AccessFailure failure));
            Assert.NotEqual(AccessFailure.None, failure);

            Assert.False(session.TryTravelTo(GameContent.RegionSanctum, out _));
            Assert.Equal(GameContent.RegionCamp, session.RegionId);
        }

        [Fact]
        public void MovingToTheRegionYouAreAlreadyIn_IsRefused()
        {
            Assert.False(MakeSession().CanTravelTo(GameContent.RegionCamp, out _));
        }

        [Fact]
        public void Travel_RecordsTheRegionAsDiscovered()
        {
            GameSession session = MakeSession();

            Assert.False(session.DiscoveredRegions.Contains(GameContent.RegionWilds));

            session.TryTravelTo(GameContent.RegionWilds, out _);

            Assert.True(session.DiscoveredRegions.Contains(GameContent.RegionWilds));
        }

        [Fact]
        public void ReturningToAVisitedRegion_IsAllowedEvenWhenNotAdjacent()
        {
            GameSession session = MakeSession();

            CompleteUpTo(session, GameContent.QuestDescent);

            Assert.True(session.TryTravelTo(GameContent.RegionWilds, out _));
            Assert.True(session.TryTravelTo(GameContent.RegionRuins, out _));
            Assert.Equal(GameContent.RegionRuins, session.RegionId);

            // Two hops back, but it has been visited, so fast travel is permitted
            // rather than forcing the player to walk the whole way.
            Assert.True(session.CanTravelTo(GameContent.RegionCamp, out AccessFailure failure), "Camp: " + failure);
        }

        [Fact]
        public void AnOpenChapterGate_DoesNotBypassTheWorldGraph()
        {
            // The two rules must compose. Opening the story gate makes the sanctum
            // permitted in principle; it does not teleport the player there.
            GameSession session = MakeSession();

            Assert.NotEqual(
                AccessFailure.None,
                session.World.CanEnter(GameContent.RegionSanctum, session.Chapters));

            FinishTheStory(session);

            Assert.Equal(
                AccessFailure.None,
                session.World.CanEnter(GameContent.RegionSanctum, session.Chapters));

            // Chapter open, but still not adjacent and not yet visited.
            Assert.False(session.CanTravelTo(GameContent.RegionSanctum, out AccessFailure failure));
            Assert.Equal(AccessFailure.NotConnected, failure);
        }

        // --------------------------- the story must be walkable --------------------

        [Fact]
        public void TheStorysRequiredRegions_AreReachableInOrder()
        {
            // The route the authored quests demand: reach the Grey Wilds for the
            // opening quest, then the Hollowed Ruins for the third. Both must be
            // permitted at the moment the quest asks for them, or the story stalls with
            // the player standing in front of a door that will not open.
            GameSession session = MakeSession();

            CompleteUpTo(session, GameContent.QuestArrival);

            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestArrival));

            Assert.True(
                session.TryTravelTo(GameContent.RegionWilds, out AccessFailure firstFailure),
                "The opening quest asks the player to reach the Grey Wilds, so it must be reachable. " + firstFailure);

            // Arriving completes it, which pays out and offers the next quest.
            Assert.Equal(QuestStatus.TurnedIn, session.Quests.StatusOf(GameContent.QuestArrival));
            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestFirstBlood));

            CompleteUpTo(session, GameContent.QuestDescent);

            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestDescent));

            Assert.True(
                session.TryTravelTo(GameContent.RegionRuins, out AccessFailure secondFailure),
                "The third quest asks the player to enter the Hollowed Ruins, so it must be reachable. "
                + secondFailure);

            Assert.Equal(QuestStatus.TurnedIn, session.Quests.StatusOf(GameContent.QuestDescent));
            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf(GameContent.QuestSplinters));
        }

        [Fact]
        public void EveryRegion_HasANeighbour()
        {
            // A region with no connections can never be travelled to, so it is content
            // nobody will see. The content validator checks this too; asserting it here
            // as well keeps the reason visible beside the travel code it constrains.
            GameSession session = MakeSession();

            Assert.True(session.World.All.Count > 1, "There should be more than one region.");

            for (int i = 0; i < session.World.All.Count; i++)
            {
                string id = session.World.All[i].Id;
                int neighbours = session.World.Neighbours(id).Count;

                Assert.True(neighbours > 0, "Region '" + id + "' has no neighbours, so nothing can lead there.");
            }
        }

        [Fact]
        public void Travel_PreservesTheCharacter()
        {
            // Travelling is not a reset. The player keeps their level, bag and health,
            // which is what makes a hub worth returning to.
            GameSession session = MakeSession();

            session.Quests.TryStart(GameContent.QuestArrival);
            session.AdvanceQuests();
            session.Progression.AddExperience(500);
            session.GrantItem(GameContent.ItemAsh, 3);

            int level = session.Progression.Level;
            int experience = session.Progression.TotalExperience;
            float health = session.Player.Vitals.Health;

            Assert.True(session.TryTravelTo(GameContent.RegionWilds, out _));

            Assert.Equal(level, session.Progression.Level);
            Assert.True(session.Progression.TotalExperience >= experience);
            Assert.Equal(health, session.Player.Vitals.Health, 3);
            Assert.Equal(3, session.Inventory.Count(GameContent.ItemAsh));
        }
    }
}
