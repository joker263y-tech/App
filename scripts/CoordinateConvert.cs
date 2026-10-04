using Godot;
using Shadowbound.Core.Numerics;

namespace Shadowbound.Game
{
    /// <summary>
    /// The boundary between the engine-free core and Godot.
    ///
    /// The core already simulates in a Y-up, right-handed space where one unit is
    /// one metre and facing 0 degrees points at +Z, growing toward +X. Godot is
    /// also Y-up and metres, so positions copy across component for component -
    /// there is no scale factor and no axis swap, which is one fewer place for a
    /// bug to hide.
    ///
    /// Rotation is the only conversion. A Godot node's forward is -Z, while the
    /// core's facing 0 points at +Z, so a core facing angle maps to a Godot Y
    /// rotation of facing + 180 degrees. Nothing outside this file converts
    /// coordinates, and the presentation layer never writes a position back into
    /// the core - the core is the single authority over where anything is.
    /// </summary>
    public static class CoordinateConvert
    {
        public static Vector3 ToGodot(Float3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        public static Float3 ToCore(Vector3 value)
        {
            return new Float3(value.X, value.Y, value.Z);
        }

        /// <summary>Core facing degrees to a Godot Y rotation, in radians.</summary>
        public static float FacingToYawRadians(float facingDegrees)
        {
            return Mathf.DegToRad(facingDegrees) + Mathf.Pi;
        }

        /// <summary>Unit ground direction for a core facing angle.</summary>
        public static Vector3 DirectionFromFacing(float facingDegrees)
        {
            float radians = Mathf.DegToRad(facingDegrees);
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        /// <summary>Unit ground direction for a camera yaw expressed in core degrees.</summary>
        public static Vector3 FlatDirection(float yawDegrees)
        {
            float radians = Mathf.DegToRad(yawDegrees);
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }
    }
}
