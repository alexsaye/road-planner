using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadPlanning
{
    /// <summary>
    /// Describes a plan of nodes, which form roads, which form districts.
    /// </summary>
    public interface IPlan
    {
        IEnumerable<INode> Nodes { get; }

        IEnumerable<IRoad> Roads { get; }

        /// <summary>
        /// Get all the nodes connecting to a node.
        /// </summary>
        IEnumerable<INode> ConnectingNodes(INode node);

        /// <summary>
        /// Get all the roads connecting to a node.
        /// </summary>
        IEnumerable<IRoad> ConnectingRoads(INode node);

        /// <summary>
        /// Get the road connecting two nodes.
        /// </summary>
        IRoad ConnectingRoad(INode a, INode b);

        /// <summary>
        /// Get the closest road to a position.
        /// </summary>
        IRoad ClosestRoad(Vector3 position);

        /// <summary>
        /// Get the closest road to a position within a district.
        /// </summary>
        IRoad ClosestRoad(Vector3 position, IDistrict district);

        /// <summary>
        /// Get the district adjacent to a side of a road.
        /// </summary>
        IDistrict AdjacentDistrict(Side side, IRoad road);
    }

    /// <summary>
    /// A plan of nodes, which form roads, which form districts.
    /// </summary>
    public class Plan : IPlan
    {
        public IEnumerable<INode> Nodes => roadsByNode.Keys;

        public IEnumerable<IRoad> Roads { get; private set; }

        private readonly IDictionary<INode, IDictionary<INode, IRoad>> roadsByNode;

        private readonly IDictionary<IRoad, IDictionary<Side, IDistrict>> districtsByRoadSide;

        public Plan(IDictionary<INode, IDictionary<INode, IRoad>> graph)
        {
            // Cache the graph.
            roadsByNode = new Dictionary<INode, IDictionary<INode, IRoad>>(graph);

            // Cache all unique roads.
            Roads = graph.Values.SelectMany(connections => connections.Values).ToHashSet();

            // Prepare the cache for districts by road side.
            districtsByRoadSide = new Dictionary<IRoad, IDictionary<Side, IDistrict>>();
            foreach (var road in Roads)
            {
                districtsByRoadSide[road] = new Dictionary<Side, IDistrict>();
            }

            // Build all cycles in the graph.
            var allCycles = BuildAllCycles(graph.First().Key, null, new List<INode>(), new HashSet<IRoad>(), new HashSet<ISet<IRoad>>());

            // Find the shortest cycles to cover the interior districts.
            var interiorCycles = FindInteriorDistrictCycles(allCycles);

            // Build the cycle that forms the infinite exterior district. TODO: This cycle has already been found, maybe it would be better to find it from all the cycles than to build it from the interior cycles.
            var exteriorCycle = BuildExteriorDistrictCycle(interiorCycles);

            Debug.Log($"From {allCycles.Count} cycles, found {interiorCycles.Count} interior cycles and an exterior cycle of {exteriorCycle.Count} roads.");
            foreach (var cycle in allCycles)
            {
                Debug.Log($"Cycle: {string.Join(", ", cycle.Select(road => road.Name))}");
            }

            foreach (var cycle in interiorCycles)
            {
                Debug.Log($"Interior cycle: {string.Join(", ", cycle.Select(road => road.Name))}");
            }

            Debug.Log($"Exterior cycle: {string.Join(", ", exteriorCycle.Select(road => road.Name))}");

            // TODO: Construct the districts.
        }

        public Plan(IEnumerable<IRoadBuilder> graph) : this(IRoadBuilder.Build(graph)) { }


        /// <summary>
        /// Perform a depth-first search to find all unique road cycles, building up a found cycles cache.
        /// </summary>
        private ICollection<ISet<IRoad>> BuildAllCycles(INode current, INode previous, IList<INode> trace, ISet<IRoad> travelled, ICollection<ISet<IRoad>> found)
        {
            trace.Add(current);

            foreach (var connection in roadsByNode[current].Keys)
            {
                // Don't go whence we came.
                if (connection == previous)
                {
                    continue;
                }

                // Get the road for this connection.
                var road = roadsByNode[connection][current];

                // Don't go along a road we've already travelled.
                if (travelled.Contains(road))
                {
                    continue;
                }

                travelled.Add(road);

                // If we've already visited this node, we've found a cycle.
                if (trace.Contains(connection))
                {
                    // Start with the road from the connection to the current node.
                    var cycle = new HashSet<IRoad>() {
                        road,
                        roadsByNode[current][previous]
                    };

                    // Trace the roads back to the connection to form the cycle.
                    var index = trace.Count - 2;
                    do
                    {
                        cycle.Add(roadsByNode[trace[index]][trace[index - 1]]);
                        --index;
                    } while (!trace[index].Equals(connection));

                    // Cache the cycle if we haven't already found this cycle before.
                    if (!found.Any(existing => existing.SetEquals(cycle)))
                    {
                        found.Add(cycle);
                    }
                }
                else
                {
                    // Otherwise, travel this road and continue the search
                    BuildAllCycles(connection, current, trace, travelled, found);
                }

                travelled.Remove(road);
            }
            trace.RemoveAt(trace.Count - 1);

            return found;
        }

        /// <summary>
        /// Reduce cycles to cover all roads with only the shortest cycles.
        /// </summary>
        private ICollection<ISet<IRoad>> FindInteriorDistrictCycles(ICollection<ISet<IRoad>> cycles)
        {
            var reduced = new HashSet<ISet<IRoad>>();
            var ordered = cycles.OrderBy(cycle => cycle.Count);
            var covered = new HashSet<IRoad>();
            while (covered.Count < Roads.Count())
            {
                // Find the shortest cycle that covers a road we haven't covered yet, mark its roads as covered and cache it. I'm pretty sure the efficiency of this can be improved.
                var shortest = ordered.First(cycle => cycle.Any(road => !covered.Contains(road)));
                covered.UnionWith(shortest);
                reduced.Add(shortest);
            }
            return reduced;
        }

        /// <summary>
        /// Build the cycle which forms the exterior from a set of unique interior cycles.
        /// </summary>
        private ISet<IRoad> BuildExteriorDistrictCycle(ICollection<ISet<IRoad>> interiorCycles)
        {
            var exterior = new HashSet<IRoad>();
            foreach (var cycle in interiorCycles)
            {
                foreach (var road in cycle)
                {
                    // Construct the exterior from roads that are only covered by one cycle - interior roads are shared by two cycles.
                    if (!interiorCycles.Any(other => other != cycle && other.Contains(road)))
                    {
                        exterior.Add(road);
                    }
                }
            }
            return exterior;
        }

        public IEnumerable<INode> ConnectingNodes(INode node)
        {
            return roadsByNode[node].Keys;
        }

        public IEnumerable<IRoad> ConnectingRoads(INode node)
        {
            return roadsByNode[node].Values;
        }

        public IRoad ConnectingRoad(INode a, INode b)
        {
            return roadsByNode[a][b];
        }

        public IRoad ClosestRoad(Vector3 position)
        {
            return districtsByRoadSide.Keys
                .OrderBy(road => Vector3.SqrMagnitude(road.ClosestPoint(position) - position))
                .First();
        }

        public IRoad ClosestRoad(Vector3 position, IDistrict district)
        {
            return district.Sides.Keys
                .OrderBy(road => Vector3.SqrMagnitude(road.ClosestPoint(position) - position))
                .First();
        }

        public IDistrict AdjacentDistrict(Side side, IRoad road)
        {
            return districtsByRoadSide[road][side];
        }
    }

    /// <summary>
    /// Describes a road going from one node to another node.
    /// </summary>
    public interface IRoad
    {
        string Name { get; }

        INode Start { get; }

        INode End { get; }

        /// <summary>
        /// Get which side of the road the position is on compared with the direction of the road.
        /// </summary>
        Side SideOfPoint(Vector3 position);

        /// <summary>
        /// Get the closest point on the road to the given position.
        /// </summary>
        Vector3 ClosestPoint(Vector3 position);
    }

    /// <summary>
    /// A straight road, the simplest road.
    /// </summary>
    public class StraightRoad : IRoad
    {
        public string Name { get; private set; }

        public INode Start { get; private set; }

        public INode End { get; private set; }

        public StraightRoad(INode start, INode end, string name)
        {
            Start = start;
            End = end;
            Name = name;
        }

        public Side SideOfPoint(Vector3 position)
        {
            var axis = (End.Position - Start.Position).normalized;
            var sign = LineUtils.SignOfPointOnAxis(position - Start.Position, axis, Vector3.up);
            return sign > 0f ? Side.Right : Side.Left;
        }

        public Vector3 ClosestPoint(Vector3 position)
        {
            return LineUtils.ClosestPoint(position, Start.Position, End.Position);
        }
    }

    /// <summary>
    /// Describes a region defined by a set of roads and the sides of those roads that the district is on.
    /// </summary>
    public interface IDistrict
    {
        public string Name { get; }

        public IReadOnlyDictionary<IRoad, Side> Sides { get; }
    }

    class District : IDistrict
    {
        public string Name { get; private set; }

        public IReadOnlyDictionary<IRoad, Side> Sides { get; private set; }

        public District(IEnumerable<(IRoad, Side)> roads, string name)
        {
            Sides = roads.ToDictionary(pair => pair.Item1, pair => pair.Item2);
            Name = name;
        }
    }

    /// <summary>
    /// Describes a connection position for roads.
    /// </summary>
    public interface INode
    {
        string Name { get; }

        Vector3 Position { get; }
    }

    /// <summary>
    /// Describes a node that can build roads to other nodes.
    /// </summary>
    public interface IRoadBuilder : INode
    {
        /// <summary>
        /// Directional forward connections to other nodes.
        /// </summary>
        public IEnumerable<IRoadBuilder> Connections { get; }

        /// <summary>
        /// Build a road between this node and another node.
        /// </summary>
        public IRoad Build(INode node);

        /// <summary>
        /// Build all roads as an undirected graph of nodes superimposed with a directed graph of roads.
        /// </summary>
        public static IDictionary<INode, IDictionary<INode, IRoad>> Build(IEnumerable<IRoadBuilder> nodes)
        {
            var roads = new Dictionary<INode, IDictionary<INode, IRoad>>();
            foreach (var node in nodes)
            {
                roads[node] = new Dictionary<INode, IRoad>();
            }
            foreach (var node in nodes)
            {
                foreach (var connection in node.Connections)
                {
                    var road = node.Build(connection);
                    roads[node][connection] = road;
                    roads[connection][node] = road;
                }
            }
            return roads;
        }
    }

    /// <summary>
    /// The side of a road.
    /// </summary>
    public enum Side
    {
        Left,
        Right
    }
}