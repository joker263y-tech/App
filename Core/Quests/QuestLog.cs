using System;
using System.Collections.Generic;

namespace Shadowbound.Core.Quests
{
    /// <summary>
    /// The player's quest journal.
    ///
    /// Owns every quest state, decides which quests may be started, and routes
    /// world events to the quests that care about them. Events are reported once
    /// and fan out from here, so no other system needs to know which quests are
    /// watching - advancing the story does not mean threading quest ids through
    /// combat, the inventory and the world.
    ///
    /// Completion is detected here rather than by callers, so the completion
    /// event cannot be missed by a code path that forgot to check.
    /// </summary>
    public sealed class QuestLog
    {
        private readonly Dictionary<string, QuestState> _states;
        private readonly List<QuestState> _order;

        public QuestLog()
        {
            _states = new Dictionary<string, QuestState>(StringComparer.Ordinal);
            _order = new List<QuestState>(16);
        }

        /// <summary>Raised when a quest becomes active.</summary>
        public event Action<QuestState> Started;

        /// <summary>Raised when a quest's required objectives are all met.</summary>
        public event Action<QuestState> Completed;

        /// <summary>Raised when rewards are claimed.</summary>
        public event Action<QuestState> TurnedIn;

        public int Count
        {
            get { return _order.Count; }
        }

        /// <summary>Quests in registration order, which is the authored story order.</summary>
        public IReadOnlyList<QuestState> All
        {
            get { return _order; }
        }

        /// <summary>Fills <paramref name="into"/> with active quests. Avoids allocating an iterator.</summary>
        public void CollectActive(List<QuestState> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();

            for (int i = 0; i < _order.Count; i++)
            {
                if (_order[i].Status == QuestStatus.Active)
                {
                    into.Add(_order[i]);
                }
            }
        }

        public void Register(QuestDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                throw new ArgumentException("Quest definition requires a non-empty id.", nameof(definition));
            }

            if (_states.ContainsKey(definition.Id))
            {
                // Re-registering replaces content but keeps the player's progress,
                // so a content hot-reload does not wipe the journal.
                QuestState existing = _states[definition.Id];
                existing.Definition = definition;
                return;
            }

            var state = new QuestState(definition);
            _states[definition.Id] = state;
            _order.Add(state);
        }

        public void RegisterRange(IEnumerable<QuestDefinition> definitions)
        {
            if (definitions == null)
            {
                return;
            }

            foreach (QuestDefinition definition in definitions)
            {
                Register(definition);
            }
        }

        public QuestState State(string questId)
        {
            if (string.IsNullOrEmpty(questId))
            {
                return null;
            }

            return _states.TryGetValue(questId, out QuestState state) ? state : null;
        }

        public QuestStatus StatusOf(string questId)
        {
            QuestState state = State(questId);
            return state == null ? QuestStatus.Locked : state.Status;
        }

        /// <summary>True when every prerequisite quest has been turned in.</summary>
        public bool ArePrerequisitesMet(string questId)
        {
            QuestState state = State(questId);
            if (state == null)
            {
                return false;
            }

            string[] prerequisites = state.Definition.PrerequisiteQuestIds;

            for (int i = 0; i < prerequisites.Length; i++)
            {
                if (StatusOf(prerequisites[i]) != QuestStatus.TurnedIn)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A quest is startable when it is locked, unmet, and its prerequisites are done.</summary>
        public bool IsStartable(string questId)
        {
            QuestState state = State(questId);
            if (state == null || state.Status != QuestStatus.Locked)
            {
                return false;
            }

            return ArePrerequisitesMet(questId);
        }

        public bool TryStart(string questId)
        {
            if (!IsStartable(questId))
            {
                return false;
            }

            QuestState state = _states[questId];
            state.ResetProgress();
            state.Status = QuestStatus.Active;

            Started?.Invoke(state);

            // A quest with no objectives is complete on arrival, so completion is
            // evaluated immediately rather than waiting for an event that will
            // never come.
            TryComplete(state);
            return true;
        }

        /// <summary>
        /// Routes an event to every active quest. Returns how many quests became
        /// completed as a result, so the caller can queue their completion
        /// presentation.
        /// </summary>
        public int Report(in QuestEvent questEvent)
        {
            int completed = 0;

            for (int i = 0; i < _order.Count; i++)
            {
                QuestState state = _order[i];
                if (state.Status != QuestStatus.Active)
                {
                    continue;
                }

                if (state.Report(questEvent) > 0 && TryComplete(state))
                {
                    completed++;
                }
            }

            return completed;
        }

        /// <summary>Reports many events in order. Convenient for tests and for batched encounter results.</summary>
        public int ReportAll(IReadOnlyList<QuestEvent> events)
        {
            if (events == null)
            {
                return 0;
            }

            int completed = 0;
            for (int i = 0; i < events.Count; i++)
            {
                completed += Report(events[i]);
            }

            return completed;
        }

        /// <summary>Syncs an absolute-count objective, such as a collection target.</summary>
        public bool SetProgress(string questId, string objectiveId, int value)
        {
            QuestState state = State(questId);
            if (state == null || !state.SetProgress(objectiveId, value))
            {
                return false;
            }

            TryComplete(state);
            return true;
        }

        /// <summary>
        /// Claims a completed quest's reward and marks it turned in, which is what
        /// unlocks its dependants. Returns false when the quest is not completed,
        /// so rewards cannot be claimed early or twice.
        /// </summary>
        public bool TryTurnIn(string questId, out QuestReward reward)
        {
            reward = QuestReward.None;

            QuestState state = State(questId);
            if (state == null || state.Status != QuestStatus.Completed)
            {
                return false;
            }

            reward = state.Definition.Rewards;

            if (state.Definition.IsRepeatable)
            {
                state.ResetProgress();
                state.Status = QuestStatus.Locked;
            }
            else
            {
                state.Status = QuestStatus.TurnedIn;
            }

            TurnedIn?.Invoke(state);
            return true;
        }

        public int CountWithStatus(QuestStatus status)
        {
            int count = 0;

            for (int i = 0; i < _order.Count; i++)
            {
                if (_order[i].Status == status)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Marks a quest complete without playing through it. Used by debug tools and cheats.</summary>
        public void ForceComplete(string questId)
        {
            QuestState state = State(questId);
            if (state == null)
            {
                return;
            }

            if (state.Status == QuestStatus.Locked)
            {
                state.Status = QuestStatus.Active;
            }

            ObjectiveDefinition[] objectives = state.Definition.Objectives;
            for (int i = 0; i < objectives.Length; i++)
            {
                if (objectives[i] != null)
                {
                    state.SetProgress(objectives[i].Id, objectives[i].RequiredCount);
                }
            }

            TryComplete(state);
        }

        private bool TryComplete(QuestState state)
        {
            if (!state.RefreshCompletion())
            {
                return false;
            }

            Completed?.Invoke(state);
            return true;
        }
    }
}
