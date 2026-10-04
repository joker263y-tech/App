using Godot;

namespace Shadowbound.Game
{
    /// <summary>
    /// A creature's body: Hollow Walker, Cinder Hound, Veilwarden, Ashen Sentinel.
    ///
    /// Identical machinery for all of them - stats, abilities and behaviour come
    /// from the archetype through the core. The boss uses a larger box instead of
    /// a capsule, matching the placeholder shapes the project has always used for
    /// bosses; the distinction is cosmetic.
    /// </summary>
    public partial class EnemyView : CombatantView
    {
        private MeshInstance3D _capsule;
        private MeshInstance3D _bossBox;

        public override void _Ready()
        {
            _capsule = GetNodeOrNull<MeshInstance3D>("Body");
            _bossBox = GetNodeOrNull<MeshInstance3D>("BossBody");

            if (_bossBox != null)
            {
                _bossBox.Visible = false;
            }

            AttachBody(_capsule, 1.0f);
        }

        /// <summary>Switches to the boss body before binding.</summary>
        public void SetBossShape(bool boss)
        {
            if (_bossBox == null || _capsule == null || !boss)
            {
                return;
            }

            _capsule.Visible = false;
            _bossBox.Visible = true;

            // The box is 2.2 m tall, so its centre sits 1.1 m above the feet.
            AttachBody(_bossBox, 1.1f);
        }
    }
}
