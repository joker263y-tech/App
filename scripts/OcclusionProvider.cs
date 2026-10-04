using Godot;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Simulation;

namespace Shadowbound.Game
{
    /// <summary>
    /// Answers the core's line-of-sight queries with a real Godot physics raycast.
    ///
    /// The core deliberately has no geometry, so the engine layer supplies it.
    /// Walls and pillars are on the "world" layer and combatants carry no
    /// collision body, so a ray between two combatants only ever hits arena
    /// geometry - which is what makes the enemies' vision cone meaningful.
    /// </summary>
    public sealed class OcclusionProvider : IOcclusionProvider
    {
        /// <summary>Height above the ground at which sight is measured, so the ray
        /// does not skim the floor.</summary>
        public float EyeHeight = 1.5f;

        /// <summary>Physics layers the sight ray tests: the world, plus camera blockers.</summary>
        public uint Mask = Arena.WorldLayer | (1u << 2);

        private readonly Node3D _worldSource;

        public OcclusionProvider(Node3D worldSource)
        {
            _worldSource = worldSource;
        }

        public bool HasLineOfSight(Float3 from, Float3 to)
        {
            World3D world = _worldSource.GetWorld3D();
            if (world == null)
            {
                return true;
            }

            PhysicsDirectSpaceState3D space = world.DirectSpaceState;
            if (space == null)
            {
                return true;
            }

            var query = new PhysicsRayQueryParameters3D
            {
                From = CoordinateConvert.ToGodot(from) + new Vector3(0f, EyeHeight, 0f),
                To = CoordinateConvert.ToGodot(to) + new Vector3(0f, EyeHeight, 0f),
                CollisionMask = Mask,
                CollideWithAreas = false,
                CollideWithBodies = true
            };

            return space.IntersectRay(query).Count == 0;
        }
    }
}
