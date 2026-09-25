using System.Collections.Generic;
using Shadowbound.Core.Items;
using Shadowbound.Core.Quests;
using Xunit;

namespace Shadowbound.Core.Tests.Quests
{
    public class QuestStateTests
    {
        private static QuestDefinition KillThreeQuest()
        {
            return new QuestDefinition
            {
                Id = "q-hollow",
                Title = "Hollow Ground",
                Objectives = new[]
                {
                    new ObjectiveDefinition("kills", ObjectiveKind.Kill, "hollow-walker", 3),
                    new ObjectiveDefinition("boss", ObjectiveKind.DefeatBoss, "sentinel", 1)
                }
            };
        }

        [Fact]
        public void ANewQuest_StartsLockedWithNoProgress()
        {
            var state = new QuestState(KillThreeQuest());

            Assert.Equal(QuestStatus.Locked, state.Status);
            Assert.Equal(0, state.ProgressOf("kills"));
            Assert.Equal(0f, state.Completion, 3);
        }

        [Fact]
        public void Report_IsIgnoredWhileTheQuestIsLocked()
        {
            var state = new QuestState(KillThreeQuest());

            Assert.Equal(0, state.Report(QuestEvent.Kill("hollow-walker")));
        }

        [Fact]
        public void Report_AdvancesMatchingObjectivesCumulatively()
        {
            var state = new QuestState(KillThreeQuest());
            state.Status = QuestStatus.Active;

            state.Report(QuestEvent.Kill("hollow-walker"));
            state.Report(QuestEvent.Kill("hollow-walker"));

            Assert.Equal(2, state.ProgressOf("kills"));
        }

        [Fact]
        public void Report_IgnoresUnrelatedTargets()
        {
            var state = new QuestState(KillThreeQuest());
            state.Status = QuestStatus.Active;

            state.Report(QuestEvent.Kill("something-else"));

            Assert.Equal(0, state.ProgressOf("kills"));
        }

        [Fact]
        public void Report_ClampsAtTheRequiredCount()
        {
            var state = new QuestState(KillThreeQuest());
            state.Status = QuestStatus.Active;

            state.Report(QuestEvent.Kill("hollow-walker", 50));

            Assert.Equal(3, state.ProgressOf("kills"));
        }

        [Fact]
        public void AnEmptyTarget_MatchesAnyEventOfThatKind()
        {
            var definition = new QuestDefinition
            {
                Id = "q-any",
                Objectives = new[] { new ObjectiveDefinition("kills", ObjectiveKind.Kill, "", 2) }
            };

            var state = new QuestState(definition);
            state.Status = QuestStatus.Active;

            state.Report(QuestEvent.Kill("anything"));
            state.Report(QuestEvent.Kill("anything-else"));

            Assert.Equal(2, state.ProgressOf("kills"));
        }

        [Fact]
        public void Completion_RequiresEveryRequiredObjective()
        {
            var state = new QuestState(KillThreeQuest());
            state.Status = QuestStatus.Active;

            state.Report(QuestEvent.Kill("hollow-walker", 3));
            Assert.False(state.IsComplete);

            state.Report(QuestEvent.DefeatBoss("sentinel"));
            Assert.True(state.IsComplete);
        }

        [Fact]
        public void OptionalObjectives_DoNotGateCompletion()
        {
            var definition = new QuestDefinition
            {
                Id = "q-optional",
                Objectives = new[]
                {
                    new ObjectiveDefinition("main", ObjectiveKind.Kill, "walker", 1),
                    new ObjectiveDefinition("secret", ObjectiveKind.Interact, "hidden-altar", 1, optional: true)
                }
            };

            var state = new QuestState(definition);
            state.Status = QuestStatus.Active;
            state.Report(QuestEvent.Kill("walker"));

            Assert.True(state.IsComplete);
            Assert.False(state.IsOptionalComplete("secret"));
            Assert.Equal(1f, state.Completion, 3);
        }

        [Fact]
        public void SetProgress_SetsAnAbsoluteCountRatherThanAdding()
        {
            // Collection objectives are satisfied by what the player holds, so
            // picking up and dropping the same item must not advance the quest.
            var definition = new QuestDefinition
            {
                Id = "q-collect",
                Objectives = new[] { new ObjectiveDefinition("hides", ObjectiveKind.Collect, "hide", 3) }
            };

            var state = new QuestState(definition);
            state.Status = QuestStatus.Active;

            state.SetProgress("hides", 2);
            Assert.Equal(2, state.ProgressOf("hides"));

            state.SetProgress("hides", 0);
            Assert.Equal(0, state.ProgressOf("hides"));
        }

        [Fact]
        public void SetProgress_ClampsToTheRequiredCount()
        {
            var definition = new QuestDefinition
            {
                Id = "q-collect",
                Objectives = new[] { new ObjectiveDefinition("hides", ObjectiveKind.Collect, "hide", 3) }
            };

            var state = new QuestState(definition);
            state.Status = QuestStatus.Active;

            state.SetProgress("hides", 99);

            Assert.Equal(3, state.ProgressOf("hides"));
        }

        [Fact]
        public void Collect_IsNotAdvancedByReportingEvents()
        {
            var definition = new QuestDefinition
            {
                Id = "q-collect",
                Objectives = new[] { new ObjectiveDefinition("hides", ObjectiveKind.Collect, "hide", 3) }
            };

            var state = new QuestState(definition);
            state.Status = QuestStatus.Active;

            state.Report(QuestEvent.Kill("hide"));

            Assert.Equal(0, state.ProgressOf("hides"));
        }

        [Fact]
        public void RefreshCompletion_FiresOnlyOnTheCompletingTransition()
        {
            var state = new QuestState(KillThreeQuest());
            state.Status = QuestStatus.Active;
            state.Report(QuestEvent.Kill("hollow-walker", 3));
            state.Report(QuestEvent.DefeatBoss("sentinel"));

            Assert.True(state.RefreshCompletion());
            Assert.False(state.RefreshCompletion());
            Assert.Equal(QuestStatus.Completed, state.Status);
        }

        [Fact]
        public void ProgressRoundTripsThroughASaveSnapshot()
        {
            var state = new QuestState(KillThreeQuest());
            state.Status = QuestStatus.Active;
            state.Report(QuestEvent.Kill("hollow-walker", 2));

            int[] saved = state.ToProgressArray();

            var restored = new QuestState(KillThreeQuest());
            restored.Status = QuestStatus.Active;
            restored.LoadProgress(saved);

            Assert.Equal(2, restored.ProgressOf("kills"));
        }

        [Fact]
        public void LoadProgress_ToleratesASnapshotWithADifferentObjectiveCount()
        {
            // Content changes must not make an old save unloadable.
            var state = new QuestState(KillThreeQuest());
            state.Status = QuestStatus.Active;

            state.LoadProgress(new[] { 2, 1, 5, 5, 5 });

            Assert.Equal(2, state.ProgressOf("kills"));
            Assert.Equal(1, state.ProgressOf("boss"));
        }
    }

    public class QuestLogTests
    {
        private static QuestDefinition Quest(
            string id,
            string targetId,
            int kills = 1,
            string[] prerequisites = null,
            QuestReward? reward = null)
        {
            return new QuestDefinition
            {
                Id = id,
                Title = id,
                Objectives = new[] { new ObjectiveDefinition("main", ObjectiveKind.Kill, targetId, kills) },
                PrerequisiteQuestIds = prerequisites ?? new string[0],
                Rewards = reward ?? QuestReward.None
            };
        }

        [Fact]
        public void AQuestWithNoPrerequisites_IsImmediatelyStartable()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker"));

            Assert.True(log.IsStartable("q1"));
            Assert.True(log.TryStart("q1"));
            Assert.Equal(QuestStatus.Active, log.StatusOf("q1"));
        }

        [Fact]
        public void AQuestBehindAPrerequisite_CannotStartEarly()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker"));
            log.Register(Quest("q2", "sentinel", prerequisites: new[] { "q1" }));

            Assert.False(log.IsStartable("q2"));
            Assert.False(log.TryStart("q2"));

            log.TryStart("q1");
            Assert.False(log.IsStartable("q2"));

            // Only turning the prerequisite in unlocks the successor, so simply
            // finishing the objectives is not enough to skip ahead.
            log.Report(QuestEvent.Kill("walker"));
            Assert.False(log.IsStartable("q2"));

            log.TryTurnIn("q1", out _);
            Assert.True(log.IsStartable("q2"));
        }

        [Fact]
        public void Report_RoutesEventsToEveryActiveQuest()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker"));
            log.Register(Quest("q2", "walker", prerequisites: new[] { "q1" }));
            log.TryStart("q1");

            log.Report(QuestEvent.Kill("walker"));

            Assert.Equal(QuestStatus.Completed, log.StatusOf("q1"));
        }

        [Fact]
        public void Report_ReturnsTheNumberOfQuestsCompleted()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker"));
            log.TryStart("q1");

            int completed = log.Report(QuestEvent.Kill("walker"));

            Assert.Equal(1, completed);
        }

        [Fact]
        public void Completed_FiresOnce()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker"));
            int fired = 0;
            log.Completed += _ => fired++;

            log.TryStart("q1");
            log.Report(QuestEvent.Kill("walker"));
            log.Report(QuestEvent.Kill("walker"));

            Assert.Equal(1, fired);
        }

        [Fact]
        public void AQuestWithNoObjectives_CompletesOnStart()
        {
            var log = new QuestLog();
            log.Register(new QuestDefinition { Id = "q-empty", Title = "Arrival" });

            log.TryStart("q-empty");

            Assert.Equal(QuestStatus.Completed, log.StatusOf("q-empty"));
        }

        [Fact]
        public void TryTurnIn_RefusesAnIncompleteQuest()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker"));
            log.TryStart("q1");

            Assert.False(log.TryTurnIn("q1", out QuestReward reward));
            Assert.True(reward.IsEmpty);
        }

        [Fact]
        public void TryTurnIn_GrantsTheRewardAndMarksItTurnedIn()
        {
            var reward = new QuestReward
            {
                Experience = 250,
                AttributePoints = 1,
                Items = new[] { new ItemStack("ash", 3) }
            };

            var log = new QuestLog();
            log.Register(Quest("q1", "walker", reward: reward));
            log.TryStart("q1");
            log.Report(QuestEvent.Kill("walker"));

            Assert.True(log.TryTurnIn("q1", out QuestReward granted));

            Assert.Equal(250, granted.Experience);
            Assert.Equal(1, granted.AttributePoints);
            Assert.Equal("ash", granted.Items[0].ItemId);
            Assert.Equal(QuestStatus.TurnedIn, log.StatusOf("q1"));
        }

        [Fact]
        public void TryTurnIn_CannotBeClaimedTwice()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker"));
            log.TryStart("q1");
            log.Report(QuestEvent.Kill("walker"));
            log.TryTurnIn("q1", out _);

            Assert.False(log.TryTurnIn("q1", out _));
            Assert.Equal(1, log.CountWithStatus(QuestStatus.TurnedIn));
        }

        [Fact]
        public void RepeatableQuest_ReturnsToLockedAndCanBeRunAgain()
        {
            var log = new QuestLog();
            log.Register(new QuestDefinition
            {
                Id = "q-bounty",
                IsRepeatable = true,
                Objectives = new[] { new ObjectiveDefinition("main", ObjectiveKind.Kill, "walker", 1) }
            });

            log.TryStart("q-bounty");
            log.Report(QuestEvent.Kill("walker"));
            log.TryTurnIn("q-bounty", out _);

            Assert.True(log.IsStartable("q-bounty"));
            log.TryStart("q-bounty");
            Assert.Equal(0, log.State("q-bounty").ProgressOf("main"));
        }

        [Fact]
        public void SetProgress_SyncsACollectionObjectiveAndCompletes()
        {
            var log = new QuestLog();
            log.Register(new QuestDefinition
            {
                Id = "q-collect",
                Objectives = new[] { new ObjectiveDefinition("hides", ObjectiveKind.Collect, "hide", 2) }
            });

            log.TryStart("q-collect");
            log.SetProgress("q-collect", "hides", 2);

            Assert.Equal(QuestStatus.Completed, log.StatusOf("q-collect"));
        }

        [Fact]
        public void ReRegisteringAQuest_KeepsProgress()
        {
            // A content hot-reload must not wipe the journal.
            var log = new QuestLog();
            log.Register(Quest("q1", "walker", kills: 3));
            log.TryStart("q1");
            log.Report(QuestEvent.Kill("walker", 2));

            log.Register(Quest("q1", "walker", kills: 3));

            Assert.Equal(2, log.State("q1").ProgressOf("main"));
            Assert.Equal(QuestStatus.Active, log.StatusOf("q1"));
        }

        [Fact]
        public void CollectActive_GathersOnlyActiveQuests()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker"));
            log.Register(Quest("q2", "sentinel", prerequisites: new[] { "q1" }));
            log.TryStart("q1");

            var active = new List<QuestState>();
            log.CollectActive(active);

            Assert.Single(active);
            Assert.Equal("q1", active[0].Definition.Id);
        }

        [Fact]
        public void UnknownQuest_QueriesAreSafe()
        {
            var log = new QuestLog();

            Assert.Null(log.State("nope"));
            Assert.False(log.IsStartable("nope"));
            Assert.False(log.TryStart("nope"));
            Assert.False(log.TryTurnIn("nope", out _));
            Assert.Equal(0, log.Report(QuestEvent.Kill("anything")));
        }

        [Fact]
        public void ForceComplete_MarksAQuestDoneWithoutPlayingIt()
        {
            var log = new QuestLog();
            log.Register(Quest("q1", "walker", kills: 5));

            log.ForceComplete("q1");

            Assert.Equal(QuestStatus.Completed, log.StatusOf("q1"));
        }
    }

    public class ChapterTrackerTests
    {
        private static QuestLog MakeLogWithQuest(string questId, string chapterId)
        {
            var log = new QuestLog();
            log.Register(new QuestDefinition
            {
                Id = questId,
                ChapterId = chapterId,
                Objectives = new[] { new ObjectiveDefinition("main", ObjectiveKind.Kill, "walker", 1) }
            });

            return log;
        }

        private static void Complete(QuestLog log, string questId)
        {
            log.TryStart(questId);
            log.Report(QuestEvent.Kill("walker"));
            log.TryTurnIn(questId, out _);
        }

        [Fact]
        public void AChapter_IsCompleteOnlyWhenEveryQuestIsTurnedIn()
        {
            QuestLog log = MakeLogWithQuest("q1", "ch1");
            log.Register(new QuestDefinition
            {
                Id = "q2",
                ChapterId = "ch1",
                Objectives = new[] { new ObjectiveDefinition("main", ObjectiveKind.Kill, "walker", 1) }
            });

            var tracker = new ChapterTracker(log);
            tracker.Register(new ChapterDefinition { Id = "ch1", QuestIds = new[] { "q1", "q2" } });

            Complete(log, "q1");
            Assert.False(tracker.IsComplete("ch1"));
            Assert.Equal(0.5f, tracker.Completion("ch1"), 3);

            Complete(log, "q2");
            Assert.True(tracker.IsComplete("ch1"));
            Assert.Equal(1f, tracker.Completion("ch1"), 3);
        }

        [Fact]
        public void AChapter_IsLockedUntilItsPrerequisitesAreComplete()
        {
            QuestLog log = MakeLogWithQuest("q1", "ch1");
            log.Register(new QuestDefinition
            {
                Id = "q2",
                ChapterId = "ch2",
                Objectives = new[] { new ObjectiveDefinition("main", ObjectiveKind.Kill, "walker", 1) }
            });

            var tracker = new ChapterTracker(log);
            tracker.Register(new ChapterDefinition { Id = "ch1", QuestIds = new[] { "q1" } });
            tracker.Register(new ChapterDefinition { Id = "ch2", QuestIds = new[] { "q2" }, RequiredChapterIds = new[] { "ch1" } });

            Assert.False(tracker.IsUnlocked("ch2"));

            Complete(log, "q1");

            Assert.True(tracker.IsUnlocked("ch2"));
        }

        [Fact]
        public void RefreshingChapters_AnnouncesCompletionOnlyOnce()
        {
            QuestLog log = MakeLogWithQuest("q1", "ch1");
            var tracker = new ChapterTracker(log);
            tracker.Register(new ChapterDefinition { Id = "ch1", QuestIds = new[] { "q1" } });

            var announced = new List<string>();
            tracker.ChapterCompleted += chapter => announced.Add(chapter.Id);

            Complete(log, "q1");

            Assert.Equal(1, tracker.Refresh());
            Assert.Equal(0, tracker.Refresh());
            Assert.Single(announced);
        }

        [Fact]
        public void StoryIsComplete_OnlyWhenEveryChapterIs()
        {
            QuestLog log = MakeLogWithQuest("q1", "ch1");
            log.Register(new QuestDefinition
            {
                Id = "q2",
                ChapterId = "ch2",
                Objectives = new[] { new ObjectiveDefinition("main", ObjectiveKind.Kill, "walker", 1) }
            });

            var tracker = new ChapterTracker(log);
            tracker.Register(new ChapterDefinition { Id = "ch1", QuestIds = new[] { "q1" } });
            tracker.Register(new ChapterDefinition { Id = "ch2", QuestIds = new[] { "q2" } });

            Complete(log, "q1");
            Assert.False(tracker.IsStoryComplete);

            Complete(log, "q2");
            Assert.True(tracker.IsStoryComplete);
        }

        [Fact]
        public void UnknownChapter_QueriesAreSafe()
        {
            var tracker = new ChapterTracker(new QuestLog());

            Assert.False(tracker.IsComplete("nope"));
            Assert.False(tracker.IsUnlocked("nope"));
            Assert.Equal(0f, tracker.Completion("nope"), 3);
        }
    }
}
