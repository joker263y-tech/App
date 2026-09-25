using System.Collections.Generic;
using Shadowbound.Core.Quests;
using Shadowbound.Core.World;
using Xunit;

namespace Shadowbound.Core.Tests.World
{
    public class WorldGraphTests
    {
        /// <summary>
        /// A small world:
        ///   camp - wilds - ruins - sanctum
        ///                  \
        ///                   hollow
        /// </summary>
        private static WorldGraph MakeGraph()
        {
            var graph = new WorldGraph();

            graph.Register(new RegionDefinition
            {
                Id = "camp",
                DisplayName = "Last Ember Camp",
                Kind = RegionKind.Camp,
                Connections = new[] { "wilds" }
            });

            graph.Register(new RegionDefinition
            {
                Id = "wilds",
                Connections = new[] { "camp", "ruins" }
            });

            graph.Register(new RegionDefinition
            {
                Id = "ruins",
                Connections = new[] { "wilds", "sanctum", "hollow" }
            });

            graph.Register(new RegionDefinition
            {
                Id = "sanctum",
                Connections = new[] { "ruins" }
            });

            graph.Register(new RegionDefinition
            {
                Id = "hollow",
                Connections = new[] { "ruins" }
            });

            return graph;
        }

        [Fact]
        public void RegisteringARegion_MakesItsNeighboursMutual()
        {
            WorldGraph graph = MakeGraph();

            Assert.Equal(5, graph.Count);
            Assert.True(graph.AreConnected("camp", "wilds"));
            Assert.True(graph.AreConnected("wilds", "camp"));
        }

        [Fact]
        public void ConnectionsAreMirrored_SoOneWayDoorsCannotExist()
        {
            // Authoring only one side of a link is the classic way to create a
            // region the player can enter but never leave.
            var graph = new WorldGraph();
            graph.Register(new RegionDefinition { Id = "a", Connections = new[] { "b" } });
            graph.Register(new RegionDefinition { Id = "b", Connections = new string[0] });

            Assert.True(graph.AreConnected("a", "b"));
            Assert.True(graph.AreConnected("b", "a"), "The reverse link must be created automatically.");
        }

        [Fact]
        public void RegisteringInReverseOrder_StillMirrors()
        {
            var graph = new WorldGraph();
            graph.Register(new RegionDefinition { Id = "b", Connections = new string[0] });
            graph.Register(new RegionDefinition { Id = "a", Connections = new[] { "b" } });

            Assert.True(graph.AreConnected("b", "a"));
        }

        [Fact]
        public void RegisterRange_MirrorsLinksBetweenRegionsAddedInOneCall()
        {
            var regions = new List<RegionDefinition>
            {
                new RegionDefinition { Id = "a", Connections = new[] { "b" } },
                new RegionDefinition { Id = "b", Connections = new string[0] },
                new RegionDefinition { Id = "c", Connections = new[] { "b" } }
            };

            var graph = new WorldGraph();
            graph.RegisterRange(regions);

            Assert.True(graph.AreConnected("b", "a"));
            Assert.True(graph.AreConnected("b", "c"));
        }

        [Fact]
        public void SelfLinks_AreIgnored()
        {
            var graph = new WorldGraph();
            graph.Register(new RegionDefinition { Id = "a", Connections = new[] { "a" } });

            Assert.False(graph.AreConnected("a", "a"));
        }

        [Fact]
        public void FindPath_ReturnsTheShortestRouteInclusiveOfBothEnds()
        {
            WorldGraph graph = MakeGraph();

            List<string> path = graph.FindPath("camp", "sanctum");

            Assert.Equal(new[] { "camp", "wilds", "ruins", "sanctum" }, path);
        }

        [Fact]
        public void FindPath_ToTheSameRegion_IsASingleEntry()
        {
            Assert.Equal(new[] { "camp" }, MakeGraph().FindPath("camp", "camp"));
        }

        [Fact]
        public void FindPath_ToAnUnknownRegion_IsEmpty()
        {
            WorldGraph graph = MakeGraph();

            Assert.Empty(graph.FindPath("camp", "nowhere"));
            Assert.Empty(graph.FindPath("nowhere", "camp"));
            Assert.Empty(graph.FindPath(null, "camp"));
        }

        [Fact]
        public void FindPath_UsesTheShorterOfTwoBranches()
        {
            // ruins connects to both sanctum and hollow, so both are one hop away.
            WorldGraph graph = MakeGraph();

            Assert.Equal(1, graph.Distance("ruins", "sanctum"));
            Assert.Equal(1, graph.Distance("ruins", "hollow"));
            Assert.Equal(2, graph.Distance("wilds", "hollow"));
        }

        [Fact]
        public void Distance_ToAnUnreachableRegion_IsMinusOne()
        {
            var graph = new WorldGraph();
            graph.Register(new RegionDefinition { Id = "a" });
            graph.Register(new RegionDefinition { Id = "island" });

            Assert.Equal(-1, graph.Distance("a", "island"));
        }

        [Fact]
        public void CanEnter_WithoutAChapterRequirement_IsAllowed()
        {
            WorldGraph graph = MakeGraph();

            Assert.Equal(AccessFailure.None, graph.CanEnter("camp", null));
        }

        [Fact]
        public void CanEnter_WithAnIncompleteChapter_IsRefused()
        {
            var graph = new WorldGraph();
            graph.Register(new RegionDefinition { Id = "sanctum", RequiredChapterId = "ch2" });

            var log = new QuestLog();
            var chapters = new ChapterTracker(log);

            Assert.Equal(AccessFailure.ChapterIncomplete, graph.CanEnter("sanctum", chapters));
        }

        [Fact]
        public void CanEnter_WithACompleteChapter_IsAllowed()
        {
            var log = new QuestLog();
            log.Register(new QuestDefinition
            {
                Id = "q1",
                Objectives = new[] { new ObjectiveDefinition("main", ObjectiveKind.Kill, "walker", 1) }
            });

            var chapters = new ChapterTracker(log);
            chapters.Register(new ChapterDefinition { Id = "ch2", QuestIds = new[] { "q1" } });

            log.TryStart("q1");
            log.Report(QuestEvent.Kill("walker"));
            log.TryTurnIn("q1", out _);

            var graph = new WorldGraph();
            graph.Register(new RegionDefinition { Id = "sanctum", RequiredChapterId = "ch2" });

            Assert.Equal(AccessFailure.None, graph.CanEnter("sanctum", chapters));
        }

        [Fact]
        public void CanEnter_AnUnknownRegion_ReportsIt()
        {
            Assert.Equal(AccessFailure.UnknownRegion, MakeGraph().CanEnter("nowhere", null));
        }

        [Fact]
        public void ReachableFrom_StopsAtAGatedRegion()
        {
            var graph = new WorldGraph();
            graph.Register(new RegionDefinition { Id = "camp", Connections = new[] { "wilds" } });
            graph.Register(new RegionDefinition { Id = "wilds", Connections = new[] { "camp", "sanctum" } });
            graph.Register(new RegionDefinition { Id = "sanctum", Connections = new[] { "wilds" }, RequiredChapterId = "ch1" });
            graph.Register(new RegionDefinition { Id = "beyond", Connections = new[] { "sanctum" } });

            var log = new QuestLog();
            var chapters = new ChapterTracker(log);

            List<string> reachable = graph.ReachableFrom("camp", chapters);

            // Beyond is not reachable only because to get there you must pass
            // through a region that is itself gated.
            Assert.Contains("camp", reachable);
            Assert.Contains("wilds", reachable);
            Assert.DoesNotContain("sanctum", reachable);
            Assert.DoesNotContain("beyond", reachable);
        }

        [Fact]
        public void ReachableFrom_AnUnknownRegion_IsEmpty()
        {
            Assert.Empty(MakeGraph().ReachableFrom("nowhere", null));
        }

        [Fact]
        public void UnrestrictedRegions_ListsTheStartingMap()
        {
            var graph = new WorldGraph();
            graph.Register(new RegionDefinition { Id = "camp" });
            graph.Register(new RegionDefinition { Id = "wilds" });
            graph.Register(new RegionDefinition { Id = "sanctum", RequiredChapterId = "ch3" });

            List<string> open = graph.UnrestrictedRegions();

            Assert.Equal(2, open.Count);
            Assert.Contains("camp", open);
            Assert.DoesNotContain("sanctum", open);
        }

        [Fact]
        public void UnknownRegion_QueriesAreSafe()
        {
            WorldGraph graph = MakeGraph();

            Assert.Null(graph.Get("nowhere"));
            Assert.Empty(graph.Neighbours("nowhere"));
            Assert.False(graph.AreConnected("nowhere", "camp"));
            Assert.False(graph.Contains("nowhere"));
        }

        [Fact]
        public void Register_RejectsDefinitionsWithoutAnId()
        {
            var graph = new WorldGraph();

            Assert.Throws<System.ArgumentException>(() => graph.Register(new RegionDefinition { Id = "" }));
        }
    }
}
