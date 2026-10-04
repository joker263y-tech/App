using System;
using System.Collections.Generic;

namespace Shadowbound.Core.Quests
{
    /// <summary>
    /// One chapter of the story: a named group of quests, gated behind the
    /// chapters before it.
    /// </summary>
    public sealed class ChapterDefinition
    {
        public string Id = "";
        public string Title = "";
        public string Summary = "";

        /// <summary>Quests that make up this chapter. All must be turned in to complete it.</summary>
        public string[] QuestIds = Array.Empty<string>();

        /// <summary>Chapters that must be complete before this one unlocks.</summary>
        public string[] RequiredChapterIds = Array.Empty<string>();

        /// <summary>Region the chapter takes place in, used for the world map.</summary>
        public string RegionId = "";

        public override string ToString()
        {
            return string.IsNullOrEmpty(Title) ? Id : Title;
        }
    }

    /// <summary>
    /// Tracks which chapters are unlocked, in progress or complete.
    ///
    /// Chapter state is derived entirely from quest state rather than stored
    /// separately. Keeping one source of truth means a chapter cannot claim to be
    /// complete while one of its quests is still outstanding, which a duplicated
    /// flag would eventually allow.
    /// </summary>
    public sealed class ChapterTracker
    {
        private readonly Dictionary<string, ChapterDefinition> _definitions;
        private readonly List<ChapterDefinition> _order;
        private readonly QuestLog _quests;

        public ChapterTracker(QuestLog quests)
        {
            _quests = quests ?? throw new ArgumentNullException(nameof(quests));
            _definitions = new Dictionary<string, ChapterDefinition>(StringComparer.Ordinal);
            _order = new List<ChapterDefinition>(8);
        }

        /// <summary>Raised the first time a chapter becomes complete, in the order it happens.</summary>
        public event Action<ChapterDefinition> ChapterCompleted;

        public IReadOnlyList<ChapterDefinition> All
        {
            get { return _order; }
        }

        public int Count
        {
            get { return _order.Count; }
        }

        public void Register(ChapterDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                throw new ArgumentException("Chapter definition requires a non-empty id.", nameof(definition));
            }

            if (_definitions.ContainsKey(definition.Id))
            {
                _definitions[definition.Id] = definition;
                return;
            }

            _definitions[definition.Id] = definition;
            _order.Add(definition);
        }

        public void RegisterRange(IEnumerable<ChapterDefinition> definitions)
        {
            if (definitions == null)
            {
                return;
            }

            foreach (ChapterDefinition definition in definitions)
            {
                Register(definition);
            }
        }

        public ChapterDefinition Definition(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId))
            {
                return null;
            }

            return _definitions.TryGetValue(chapterId, out ChapterDefinition definition) ? definition : null;
        }

        /// <summary>True when every prerequisite chapter is complete.</summary>
        public bool IsUnlocked(string chapterId)
        {
            ChapterDefinition definition = Definition(chapterId);
            if (definition == null)
            {
                return false;
            }

            string[] required = definition.RequiredChapterIds;

            for (int i = 0; i < required.Length; i++)
            {
                if (!IsComplete(required[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when every quest in the chapter has been turned in.</summary>
        public bool IsComplete(string chapterId)
        {
            ChapterDefinition definition = Definition(chapterId);
            if (definition == null || definition.QuestIds.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < definition.QuestIds.Length; i++)
            {
                if (_quests.StatusOf(definition.QuestIds[i]) != QuestStatus.TurnedIn)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Fraction of the chapter's quests turned in, 0..1.</summary>
        public float Completion(string chapterId)
        {
            ChapterDefinition definition = Definition(chapterId);
            if (definition == null || definition.QuestIds.Length == 0)
            {
                return 0f;
            }

            int done = 0;
            for (int i = 0; i < definition.QuestIds.Length; i++)
            {
                if (_quests.StatusOf(definition.QuestIds[i]) == QuestStatus.TurnedIn)
                {
                    done++;
                }
            }

            return done / (float)definition.QuestIds.Length;
        }

        public int CompletedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _order.Count; i++)
                {
                    if (IsComplete(_order[i].Id))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>True when every registered chapter is complete. The end of the story.</summary>
        public bool IsStoryComplete
        {
            get
            {
                if (_order.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < _order.Count; i++)
                {
                    if (!IsComplete(_order[i].Id))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Re-checks every chapter and raises <see cref="ChapterCompleted"/> for
        /// any that just finished. Call after turning a quest in.
        /// </summary>
        public int Refresh()
        {
            int newlyCompleted = 0;

            for (int i = 0; i < _order.Count; i++)
            {
                ChapterDefinition definition = _order[i];

                if (_announced.Contains(definition.Id) || !IsComplete(definition.Id))
                {
                    continue;
                }

                _announced.Add(definition.Id);
                newlyCompleted++;

                ChapterCompleted?.Invoke(definition);
            }

            return newlyCompleted;
        }

        private readonly HashSet<string> _announced = new HashSet<string>(StringComparer.Ordinal);
    }
}
