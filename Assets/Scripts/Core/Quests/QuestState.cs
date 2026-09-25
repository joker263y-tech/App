using System;
using Shadowbound.Core.Numerics;

namespace Shadowbound.Core.Quests
{
    /// <summary>
    /// Live progress for one quest.
    ///
    /// Two ways to advance an objective, because the two kinds of objective need
    /// genuinely different semantics:
    ///
    ///   Cumulative (<see cref="Report"/>) suits things that happen: kills,
    ///   interactions, seconds survived, arriving somewhere. The event stream is
    ///   the truth.
    ///
    ///   Absolute (<see cref="SetProgress"/>) suits Collect objectives, where the
    ///   truth is how many the player currently holds. Reporting collections
    ///   cumulatively would let a player pick up and drop the same item to finish
    ///   a quest, so holding-count is synced from the inventory instead.
    /// </summary>
    public sealed class QuestState
    {
        private readonly int[] _progress;

        public QuestState(QuestDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _progress = new int[definition.Objectives.Length];
            Status = QuestStatus.Locked;
        }

        /// <summary>Content for this quest. Replaceable so a content reload keeps the player's progress.</summary>
        public QuestDefinition Definition { get; internal set; }

        public QuestStatus Status { get; internal set; }

        /// <summary>True when every required objective has reached its target count.</summary>
        public bool IsComplete
        {
            get
            {
                ObjectiveDefinition[] objectives = Definition.Objectives;

                for (int i = 0; i < objectives.Length; i++)
                {
                    ObjectiveDefinition objective = objectives[i];
                    if (objective == null || objective.IsOptional)
                    {
                        continue;
                    }

                    if (_progress[i] < objective.RequiredCount)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Fraction of required objectives satisfied, 0..1. Optional objectives are excluded.</summary>
        public float Completion
        {
            get
            {
                int required = Definition.RequiredObjectiveCount;
                if (required == 0)
                {
                    return 1f;
                }

                float total = 0f;
                ObjectiveDefinition[] objectives = Definition.Objectives;

                for (int i = 0; i < objectives.Length; i++)
                {
                    ObjectiveDefinition objective = objectives[i];
                    if (objective == null || objective.IsOptional)
                    {
                        continue;
                    }

                    total += FMath.Clamp01(_progress[i] / (float)objective.RequiredCount);
                }

                return FMath.Clamp01(total / required);
            }
        }

        /// <summary>True when an optional objective has been satisfied.</summary>
        public bool IsOptionalComplete(string objectiveId)
        {
            int index = IndexOf(objectiveId);
            if (index < 0 || !Definition.Objectives[index].IsOptional)
            {
                return false;
            }

            return _progress[index] >= Definition.Objectives[index].RequiredCount;
        }

        public int ProgressOf(string objectiveId)
        {
            int index = IndexOf(objectiveId);
            return index < 0 ? 0 : _progress[index];
        }

        /// <summary>
        /// Advances every matching objective by the event's amount, clamped to
        /// the required count. Returns the number of objectives that advanced.
        /// Only active quests progress; a completed quest stops counting.
        /// </summary>
        public int Report(in QuestEvent questEvent)
        {
            if (Status != QuestStatus.Active || questEvent.Amount <= 0)
            {
                return 0;
            }

            int advanced = 0;
            ObjectiveDefinition[] objectives = Definition.Objectives;

            for (int i = 0; i < objectives.Length; i++)
            {
                ObjectiveDefinition objective = objectives[i];
                if (objective == null || objective.Kind != questEvent.Kind)
                {
                    continue;
                }

                if (!TargetMatches(objective.TargetId, questEvent.TargetId))
                {
                    continue;
                }

                if (_progress[i] >= objective.RequiredCount)
                {
                    continue;
                }

                _progress[i] = FMath.ClampInt(_progress[i] + questEvent.Amount, 0, objective.RequiredCount);
                advanced++;
            }

            return advanced;
        }

        /// <summary>
        /// Sets an objective's progress outright. Used to sync Collect objectives
        /// against the inventory. Returns true when the objective exists.
        /// </summary>
        public bool SetProgress(string objectiveId, int value)
        {
            int index = IndexOf(objectiveId);
            if (index < 0)
            {
                return false;
            }

            if (Status != QuestStatus.Active)
            {
                return false;
            }

            ObjectiveDefinition objective = Definition.Objectives[index];
            _progress[index] = FMath.ClampInt(value, 0, objective.RequiredCount);
            return true;
        }

        /// <summary>
        /// Re-evaluates whether a quest should be marked completed. Called after
        /// any progress change. Returns true only on the completing transition,
        /// so completion side effects cannot fire twice.
        /// </summary>
        internal bool RefreshCompletion()
        {
            if (Status == QuestStatus.Active && IsComplete)
            {
                Status = QuestStatus.Completed;
                return true;
            }

            return false;
        }

        internal void ResetProgress()
        {
            for (int i = 0; i < _progress.Length; i++)
            {
                _progress[i] = 0;
            }
        }

        /// <summary>Progress values for saving, in declaration order.</summary>
        public int[] ToProgressArray()
        {
            var copy = new int[_progress.Length];
            Array.Copy(_progress, copy, _progress.Length);
            return copy;
        }

        /// <summary>
        /// Restores saved progress. Tolerates a snapshot from an older content
        /// version that had a different number of objectives, applying only the
        /// overlap rather than failing to load the whole save.
        /// </summary>
        public void LoadProgress(int[] saved)
        {
            if (saved == null)
            {
                return;
            }

            int count = saved.Length < _progress.Length ? saved.Length : _progress.Length;

            for (int i = 0; i < count; i++)
            {
                ObjectiveDefinition objective = Definition.Objectives[i];
                int required = objective == null ? 0 : objective.RequiredCount;
                _progress[i] = FMath.ClampInt(saved[i], 0, required);
            }
        }

        private int IndexOf(string objectiveId)
        {
            if (string.IsNullOrEmpty(objectiveId))
            {
                return -1;
            }

            ObjectiveDefinition[] objectives = Definition.Objectives;
            for (int i = 0; i < objectives.Length; i++)
            {
                if (objectives[i] != null && objectives[i].Id == objectiveId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>An empty objective target matches any event target.</summary>
        private static bool TargetMatches(string objectiveTarget, string eventTarget)
        {
            if (string.IsNullOrEmpty(objectiveTarget))
            {
                return true;
            }

            return objectiveTarget == eventTarget;
        }
    }
}
