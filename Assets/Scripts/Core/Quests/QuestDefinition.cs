using System;
using Shadowbound.Core.Items;

namespace Shadowbound.Core.Quests
{
    /// <summary>What kind of action advances an objective.</summary>
    public enum ObjectiveKind
    {
        /// <summary>Defeat creatures. Usually cumulative.</summary>
        Kill = 0,

        /// <summary>Hold a number of items. Tracked as an absolute count.</summary>
        Collect = 1,

        /// <summary>Arrive at a place.</summary>
        Reach = 2,

        /// <summary>Use something in the world.</summary>
        Interact = 3,

        /// <summary>Survive for a duration, in seconds.</summary>
        Survive = 4,

        /// <summary>Defeat a named encounter.</summary>
        DefeatBoss = 5
    }

    /// <summary>One step of a quest.</summary>
    public sealed class ObjectiveDefinition
    {
        public string Id = "";
        public string Description = "";
        public ObjectiveKind Kind = ObjectiveKind.Kill;

        /// <summary>
        /// What the objective is about: a creature archetype, item, region or
        /// interactable. An empty value matches any target of this kind.
        /// </summary>
        public string TargetId = "";

        public int RequiredCount = 1;

        /// <summary>
        /// Optional objectives appear in the log but do not gate completion.
        /// Used for secrets and bonus rewards.
        /// </summary>
        public bool IsOptional;

        public ObjectiveDefinition()
        {
        }

        public ObjectiveDefinition(string id, ObjectiveKind kind, string targetId, int requiredCount, bool optional = false, string description = "")
        {
            Id = id;
            Kind = kind;
            TargetId = targetId;
            RequiredCount = requiredCount < 1 ? 1 : requiredCount;
            IsOptional = optional;
            Description = description;
        }
    }

    /// <summary>What the player receives for finishing a quest.</summary>
    public struct QuestReward
    {
        public int Experience;
        public int AttributePoints;
        public ItemStack[] Items;

        public bool IsEmpty
        {
            get
            {
                return Experience <= 0
                    && AttributePoints <= 0
                    && (Items == null || Items.Length == 0);
            }
        }

        public static QuestReward None
        {
            get { return new QuestReward { Items = Array.Empty<ItemStack>() }; }
        }
    }

    /// <summary>
    /// Authored quest content. Immutable at runtime; per-playthrough progress
    /// lives in <see cref="QuestState"/>.
    /// </summary>
    public sealed class QuestDefinition
    {
        public string Id = "";
        public string Title = "";
        public string Summary = "";

        /// <summary>Chapter this quest belongs to, used by chapter completion.</summary>
        public string ChapterId = "";

        public ObjectiveDefinition[] Objectives = Array.Empty<ObjectiveDefinition>();

        public QuestReward Rewards = QuestReward.None;

        /// <summary>Quests that must be turned in before this one can be started.</summary>
        public string[] PrerequisiteQuestIds = Array.Empty<string>();

        /// <summary>Repeatable quests can be turned in more than once.</summary>
        public bool IsRepeatable;

        public int RequiredObjectiveCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < Objectives.Length; i++)
                {
                    if (Objectives[i] != null && !Objectives[i].IsOptional)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Title) ? Id : Title;
        }
    }

    /// <summary>Where a quest is in its lifecycle.</summary>
    public enum QuestStatus
    {
        /// <summary>Prerequisites unmet. Not shown as available.</summary>
        Locked = 0,

        /// <summary>Started and in progress.</summary>
        Active = 1,

        /// <summary>All required objectives met, awaiting turn-in.</summary>
        Completed = 2,

        /// <summary>Rewards claimed.</summary>
        TurnedIn = 3
    }

    /// <summary>
    /// Something that happened in the world, which quests may care about.
    ///
    /// Reporting events rather than letting quests poll the world keeps quest
    /// evaluation out of the frame loop and makes it trivially testable: a test
    /// can describe a whole playthrough as a list of events.
    /// </summary>
    public readonly struct QuestEvent
    {
        public readonly ObjectiveKind Kind;

        /// <summary>Empty matches any target of this kind.</summary>
        public readonly string TargetId;

        public readonly int Amount;

        public QuestEvent(ObjectiveKind kind, string targetId, int amount)
        {
            Kind = kind;
            TargetId = targetId ?? string.Empty;
            Amount = amount < 0 ? 0 : amount;
        }

        public static QuestEvent Kill(string targetId, int amount = 1)
        {
            return new QuestEvent(ObjectiveKind.Kill, targetId, amount);
        }

        public static QuestEvent DefeatBoss(string targetId)
        {
            return new QuestEvent(ObjectiveKind.DefeatBoss, targetId, 1);
        }

        public static QuestEvent Reach(string regionId)
        {
            return new QuestEvent(ObjectiveKind.Reach, regionId, 1);
        }

        public static QuestEvent Interact(string targetId, int amount = 1)
        {
            return new QuestEvent(ObjectiveKind.Interact, targetId, amount);
        }

        public static QuestEvent Survive(float seconds)
        {
            return new QuestEvent(ObjectiveKind.Survive, string.Empty, (int)seconds);
        }

        public override string ToString()
        {
            return Kind + ":" + TargetId + " x" + Amount;
        }
    }
}
