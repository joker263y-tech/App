using Godot;

namespace Shadowbound.Game
{
    /// <summary>
    /// The arena: floor, walls, line-of-sight pillars and the gate marker.
    ///
    /// Geometry is built from Godot's primitive meshes at runtime rather than
    /// authored as binary assets, so the committed source of truth stays
    /// reviewable and cannot drift out of step with the layout the core is tuned
    /// against. Real art replaces the meshes; no game rule changes.
    ///
    /// Walls and pillars are StaticBody3D on the "world" physics layer. They are
    /// gameplay, not decoration: they block the line-of-sight raycast the enemies'
    /// vision cone depends on. Combatants themselves carry no collision body - the
    /// core owns every position, so a physics body would be a second, conflicting
    /// authority.
    /// </summary>
    public partial class Arena : Node3D
    {
        /// <summary>Half-extent in metres, matching WorldBounds.Square(38) in the core.</summary>
        public const float HalfExtent = 38f;

        /// <summary>Physics layer 1 ("world"), the only layer the sight ray tests against.</summary>
        public const uint WorldLayer = 1;

        private static readonly Color FloorColor = new Color(0.13f, 0.13f, 0.15f);
        private static readonly Color BlockColor = new Color(0.18f, 0.18f, 0.21f);
        private static readonly Color GateColor = new Color(0.62f, 0.5f, 0.28f);

        public override void _Ready()
        {
            BuildFloor();
            BuildBlocks();
        }

        private void BuildFloor()
        {
            var mesh = new MeshInstance3D
            {
                Name = "Floor",
                Mesh = new PlaneMesh { Size = new Vector2(HalfExtent * 2f, HalfExtent * 2f) },
                MaterialOverride = MakeMaterial(FloorColor)
            };

            // The plane is centred on its origin; lift it so its surface is Y = 0,
            // which is exactly where the core's ground plane sits.
            mesh.Position = new Vector3(0f, 0f, 0f);
            AddChild(mesh);
        }

        private void BuildBlocks()
        {
            // Walls: 4 m tall, 1 m thick, spanning the full 76 m. Both the core's
            // WorldBounds clamp and this geometry stop the player walking out.
            AddBlock("WallNorth", new Vector3(0f, 2f, HalfExtent), new Vector3(HalfExtent * 2f, 4f, 1f));
            AddBlock("WallSouth", new Vector3(0f, 2f, -HalfExtent), new Vector3(HalfExtent * 2f, 4f, 1f));
            AddBlock("WallEast", new Vector3(HalfExtent, 2f, 0f), new Vector3(1f, 4f, HalfExtent * 2f));
            AddBlock("WallWest", new Vector3(-HalfExtent, 2f, 0f), new Vector3(1f, 4f, HalfExtent * 2f));

            // Scattered pillars give the enemies something to break line of sight
            // against, which is what makes their vision cone matter.
            AddBlock("Pillar1", new Vector3(6f, 1.5f, 4f), new Vector3(1.6f, 3f, 1.6f));
            AddBlock("Pillar2", new Vector3(-7f, 1.5f, 2f), new Vector3(1.6f, 3f, 1.6f));
            AddBlock("Pillar3", new Vector3(3f, 1.5f, -12f), new Vector3(2.2f, 3f, 2.2f));
            AddBlock("Pillar4", new Vector3(-4f, 1.5f, -14f), new Vector3(2.2f, 3f, 2.2f));

            // The gate marker at the far end. Travelling through it (or via the
            // menu) is the same region rule the session enforces; the marker is a
            // visible way out rather than only a menu entry. It does not collide.
            var gate = new MeshInstance3D
            {
                Name = "Gate",
                Mesh = new BoxMesh { Size = new Vector3(7f, 3.2f, 0.5f) },
                MaterialOverride = MakeMaterial(GateColor),
                Position = new Vector3(0f, 1.6f, HalfExtent - 6f)
            };
            AddChild(gate);
        }

        private void AddBlock(string name, Vector3 centre, Vector3 size)
        {
            var body = new StaticBody3D
            {
                Name = name,
                Position = centre,
                CollisionLayer = WorldLayer,
                CollisionMask = 0
            };
            AddChild(body);

            body.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = MakeMaterial(BlockColor)
            });

            body.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = size }
            });
        }

        private static StandardMaterial3D MakeMaterial(Color tint)
        {
            return new StandardMaterial3D
            {
                AlbedoColor = tint,
                Roughness = 0.92f,
                Metallic = 0f
            };
        }
    }
}
