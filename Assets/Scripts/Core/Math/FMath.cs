using System;

namespace Shadowbound.Core.Numerics
{
    /// <summary>
    /// Engine-independent math helpers.
    /// Deliberately separate from System.Math so that the core has one
    /// consistent, auditable source of clamping and comparison behaviour.
    /// </summary>
    public static class FMath
    {
        public const float Epsilon = 1e-5f;
        public const float EpsilonSqr = 1e-10f;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public const float TwoPi = 6.2831855f;
        public const float Pi = 3.1415927f;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static int ClampInt(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        /// <summary>Clamps a value to the 0..1 range and back to 0 if it is not a real number.</summary>
        public static float SafeClamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Clamp01(value);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * Clamp01(t));
        }

        /// <summary>Unclamped lerp. Used for exponential-style curves where overshoot is meaningful.</summary>
        public static float LerpUnclamped(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        public static float InverseLerp(float a, float b, float value)
        {
            if (Math.Abs(b - a) <= Epsilon)
            {
                return 0f;
            }

            return Clamp01((value - a) / (b - a));
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (Math.Abs(delta) <= maxDelta)
            {
                return target;
            }

            return current + (Math.Sign(delta) * maxDelta);
        }

        /// <summary>Steps toward zero, never overshooting. Used to drain knockback impulses.</summary>
        public static float MoveTowardsZero(float current, float maxDelta)
        {
            return MoveTowards(current, 0f, maxDelta);
        }

        public static bool Approximately(float a, float b)
        {
            return Math.Abs(b - a) <= Epsilon;
        }

        public static bool Approximately(float a, float b, float tolerance)
        {
            return Math.Abs(b - a) <= tolerance;
        }

        /// <summary>
        /// Smallest signed difference between two angles in degrees, in -180..180.
        /// Enemy facing and camera yaw both need this to turn the short way round.
        /// </summary>
        public static float DeltaAngle(float fromDegrees, float toDegrees)
        {
            float delta = Repeat(toDegrees - fromDegrees, 360f);
            if (delta > 180f)
            {
                delta -= 360f;
            }

            return delta;
        }

        public static float Repeat(float value, float length)
        {
            if (length <= Epsilon)
            {
                return 0f;
            }

            float result = value - ((float)Math.Floor(value / length) * length);
            return Clamp(result, 0f, length);
        }

        public static float Sqrt(float value)
        {
            return value <= 0f ? 0f : (float)Math.Sqrt(value);
        }

        /// <summary>Distance over which a value falls from 1 to 0, with a floor.</summary>
        public static float Falloff(float distance, float radius, float minimum)
        {
            if (radius <= Epsilon)
            {
                return minimum;
            }

            float t = Clamp01(1f - (distance / radius));
            return minimum + ((1f - minimum) * t);
        }

        /// <summary>
        /// Returns 1 when <paramref name="degrees"/> is at or beyond
        /// <paramref name="halfAngle"/>, falling linearly to 0 at the far edge.
        /// Used for perceiving a target inside a vision cone.
        /// </summary>
        public static bool WithinCone(Float3 origin, Float3 forward, Float3 target, float halfAngleDegrees, float range)
        {
            if (SqrDistance(origin, target) > range * range)
            {
                return false;
            }

            Float3 toTarget = (target - origin).Normalized;
            Float3 look = forward.Normalized;
            if (toTarget == Float3.Zero || look == Float3.Zero)
            {
                return false;
            }

            float cosAngle = Dot3(look, toTarget);
            float cosThreshold = (float)Math.Cos(halfAngleDegrees * Deg2Rad);
            return cosAngle >= cosThreshold;
        }

        private static float Dot3(Float3 a, Float3 b)
        {
            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }

        private static float SqrDistance(Float3 a, Float3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return (dx * dx) + (dy * dy) + (dz * dz);
        }
    }
}
