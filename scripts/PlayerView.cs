using Godot;

namespace Shadowbound.Game
{
    /// <summary>
    /// The Warden's body.
    ///
    /// A view, not a simulation: position and facing are copied from the core
    /// combatant every step, and input is turned into a CombatIntent by
    /// <see cref="PlayerDriver"/>. The player and the enemies move through
    /// identical core code; only what drives them differs.
    /// </summary>
    public partial class PlayerView : CombatantView
    {
        public override void _Ready()
        {
            var body = GetNodeOrNull<MeshInstance3D>("Body");

            // A CapsuleMesh is 2 m tall with its origin at its centre, so its
            // centre sits 1 m above the feet point the core reports.
            AttachBody(body, 1.0f);
        }
    }
}
