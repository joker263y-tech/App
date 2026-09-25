using System;
using System.Collections.Generic;
using Shadowbound.Core.Quests;

namespace Shadowbound.Core.World
{
    public enum RegionKind
    {
        /// <summary>Safe hub. No enemies, vendors and a waypoint.</summary>
        Camp = 0,

        /// <summary>Open ground with roaming enemies.</summary>
        Wilds = 1,

        /// <summary>Structured ruins with puzzles and elites.</summary>
        Ruins = 2,

        /// <summary>Dense enemy territory leading to a chapter's climax.</summary>
        Sanctum = 3,

        /// <summary>A single-encounter arena.</summary>
        Threshold = 4
    }

    /// <summary>One traversable area of the world.</summary>
    public sealed class RegionDefinition
    {
        public string Id = "";
        public string DisplayName = "";
        public RegionKind Kind = RegionKind.Wilds;

        /// <summary>Adjacent regions. Maintained symmetrically by <see cref="WorldGraph"/>.</summary>
        public string[] Connections = Array.Empty<string>();

        /// <summary>
        /// Chapter that must be complete before this region can be entered.
        /// Empty means always accessible. Used for story gating, not geometry.
        /// </summary>
        public string RequiredChapterId = "";

        /// <summary>Suggested character level, used for warnings and for scaling reward tables.</summary>
        public int RecommendedLevel = 1;

        /// <summary>Loot table rolled for encounter rewards in this region.</summary>
        public string LootTableId = "";

        /// <summary>Ids of enemy spawn groups authored for this region.</summary>
        public string[] EncounterIds = Array.Empty<string>();

        public override string ToString()
        {
            return string.IsNullOrEmpty(DisplayName) ? Id : DisplayName;
        }
    }

    /// <summary>Why a region cannot currently be entered.</summary>
    public enum AccessFailure
    {
        None = 0,
        UnknownRegion = 1,
        ChapterIncomplete = 2,
        NotConnected = 3
    }

    /// <summary>
    /// The map of the world.
    ///
    /// Connections are stored symmetrically. Authoring only one side of a link is
    /// the classic way to create a region the player can enter but never leave,
    /// so registering a connection mirrors it automatically instead of relying on
    /// every definition being written consistently.
    ///
    /// Reachability is computed from the graph rather than stored, so gating a
    /// region behind a chapter cannot leave a stale "you can go here" flag behind.
    /// </summary>
    public sealed class WorldGraph
    {
        private readonly Dictionary<string, RegionDefinition> _regions;
        private readonly Dictionary<string, List<string>> _neighbours;
        private readonly List<RegionDefinition> _order;

        public WorldGraph()
        {
            _regions = new Dictionary<string, RegionDefinition>(StringComparer.Ordinal);
            _neighbours = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            _order = new List<RegionDefinition>(16);
        }

        public int Count
        {
            get { return _order.Count; }
        }

        public IReadOnlyList<RegionDefinition> All
        {
            get { return _order; }
        }

        /// <summary>Registers a region and mirrors its connections onto its neighbours.</summary>
        public void Register(RegionDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                throw new ArgumentException("Region definition requires a non-empty id.", nameof(definition));
            }

            if (!_regions.ContainsKey(definition.Id))
            {
                _regions[definition.Id] = definition;
                _neighbours[definition.Id] = new List<string>(4);
                _order.Add(definition);
            }
            else
            {
                _regions[definition.Id] = definition;
            }

            string[] connections = definition.Connections ?? Array.Empty<string>();
            for (int i = 0; i < connections.Length; i++)
            {
                AddConnection(definition.Id, connections[i]);
            }

            // A region authored before its neighbour exists would have its link
            // recorded in one direction only, because there was no neighbour list
            // to mirror onto yet. That is precisely the one-way door this class
            // exists to prevent, so any already-registered region that names this
            // one is re-linked now that both sides exist.
            for (int i = 0; i < _order.Count; i++)
            {
                RegionDefinition other = _order[i];
                if (other.Id == definition.Id)
                {
                    continue;
                }

                string[] otherConnections = other.Connections ?? Array.Empty<string>();
                for (int j = 0; j < otherConnections.Length; j++)
                {
                    if (otherConnections[j] == definition.Id)
                    {
                        AddConnection(definition.Id, other.Id);
                        break;
                    }
                }
            }
        }

        public void RegisterRange(IEnumerable<RegionDefinition> definitions)
        {
            if (definitions == null)
            {
                return;
            }

            // Register handles ordering already, so this only needs to iterate.
            foreach (RegionDefinition definition in definitions)
            {
                Register(definition);
            }
        }

        public RegionDefinition Get(string regionId)
        {
            if (string.IsNullOrEmpty(regionId))
            {
                return null;
            }

            return _regions.TryGetValue(regionId, out RegionDefinition region) ? region : null;
        }

        public bool Contains(string regionId)
        {
            return !string.IsNullOrEmpty(regionId) && _regions.ContainsKey(regionId);
        }

        public IReadOnlyList<string> Neighbours(string regionId)
        {
            if (string.IsNullOrEmpty(regionId) || !_neighbours.TryGetValue(regionId, out List<string> neighbours))
            {
                return Array.Empty<string>();
            }

            return neighbours;
        }

        public bool AreConnected(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            if (!_neighbours.TryGetValue(a, out List<string> neighbours))
            {
                return false;
            }

            for (int i = 0; i < neighbours.Count; i++)
            {
                if (neighbours[i] == b)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a region may be entered, given story progress. The region must
        /// also be reachable from somewhere the player has already been, which the
        /// caller checks with <see cref="FindPath"/>.
        /// </summary>
        public AccessFailure CanEnter(string regionId, ChapterTracker chapters)
        {
            RegionDefinition region = Get(regionId);
            if (region == null)
            {
                return AccessFailure.UnknownRegion;
            }

            if (string.IsNullOrEmpty(region.RequiredChapterId))
            {
                return AccessFailure.None;
            }

            if (chapters == null)
            {
                return AccessFailure.ChapterIncomplete;
            }

            return chapters.IsComplete(region.RequiredChapterId)
                ? AccessFailure.None
                : AccessFailure.ChapterIncomplete;
        }

        /// <summary>
        /// Breadth-first path from one region to another, inclusive of both ends.
        /// Returns an empty list when no route exists.
        ///
        /// Breadth-first search is used rather than depth-first so the returned
        /// route is the shortest one, which is what a map hint and a fast-travel
        /// distance both need.
        /// </summary>
        public List<string> FindPath(string from, string to)
        {
            var path = new List<string>();

            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                return path;
            }

            if (!_regions.ContainsKey(from) || !_regions.ContainsKey(to))
            {
                return path;
            }

            if (from == to)
            {
                path.Add(from);
                return path;
            }

            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal) { from };
            var queue = new Queue<string>();
            queue.Enqueue(from);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                List<string> neighbours = _neighbours[current];

                for (int i = 0; i < neighbours.Count; i++)
                {
                    string next = neighbours[i];
                    if (!visited.Add(next))
                    {
                        continue;
                    }

                    previous[next] = current;

                    if (next == to)
                    {
                        return BuildPath(previous, from, to, path);
                    }

                    queue.Enqueue(next);
                }
            }

            return path;
        }

        /// <summary>Number of hops between two regions, or -1 when unreachable.</summary>
        public int Distance(string from, string to)
        {
            List<string> path = FindPath(from, to);
            return path.Count == 0 ? -1 : path.Count - 1;
        }

        /// <summary>
        /// Every region reachable from a start point, respecting chapter gating.
        /// Used to decide what to offer on a map.
        /// </summary>
        public List<string> ReachableFrom(string start, ChapterTracker chapters)
        {
            var reachable = new List<string>();

            if (!_regions.ContainsKey(start))
            {
                return reachable;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal) { start };
            var queue = new Queue<string>();
            queue.Enqueue(start);
            reachable.Add(start);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                List<string> neighbours = _neighbours[current];

                for (int i = 0; i < neighbours.Count; i++)
                {
                    string next = neighbours[i];
                    if (visited.Contains(next))
                    {
                        continue;
                    }

                    if (CanEnter(next, chapters) != AccessFailure.None)
                    {
                        continue;
                    }

                    visited.Add(next);
                    reachable.Add(next);
                    queue.Enqueue(next);
                }
            }

            return reachable;
        }

        /// <summary>Regions with no chapter requirement at all. The starting map.</summary>
        public List<string> UnrestrictedRegions()
        {
            var open = new List<string>();

            for (int i = 0; i < _order.Count; i++)
            {
                if (string.IsNullOrEmpty(_order[i].RequiredChapterId))
                {
                    open.Add(_order[i].Id);
                }
            }

            return open;
        }

        private void AddConnection(string from, string to)
        {
            if (string.IsNullOrEmpty(to) || from == to)
            {
                return;
            }

            if (!_neighbours.TryGetValue(from, out List<string> fromList))
            {
                fromList = new List<string>(4);
                _neighbours[from] = fromList;
            }

            if (!fromList.Contains(to))
            {
                fromList.Add(to);
            }

            // Mirror the link so the graph can never contain a one-way door.
            if (_neighbours.TryGetValue(to, out List<string> toList))
            {
                if (!toList.Contains(from))
                {
                    toList.Add(from);
                }
            }
        }

        private static List<string> BuildPath(Dictionary<string, string> previous, string from, string to, List<string> path)
        {
            string current = to;
            path.Add(current);

            while (current != from && previous.TryGetValue(current, out string parent))
            {
                current = parent;
                path.Add(current);
            }

            path.Reverse();
            return path;
        }
    }
}
