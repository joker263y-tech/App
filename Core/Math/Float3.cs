using System;

namespace Shadowbound.Core.Numerics
{
    /// <summary>
    /// Engine-independent vector type used by all core simulation code.
    /// The core deliberately does not know about UnityEngine.Vector3 so that
    /// combat, AI and world logic can run and be tested without an engine.
    /// </summary>
    [Serializable]
    public readonly struct Float3 : IEquatable<Float3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Float3 Zero = new Float3(0f, 0f, 0f);
        public static readonly Float3 One = new Float3(1f, 1f, 1f);
        public static readonly Float3 Up = new Float3(0f, 1f, 0f);
        public static readonly Float3 Down = new Float3(0f, -1f, 0f);
        public static readonly Float3 Right = new Float3(1f, 0f, 0f);
        public static readonly Float3 Forward = new Float3(0f, 0f, 1f);

        /// <summary>Squared length. Prefer this over <see cref="Magnitude"/> for comparisons.</summary>
        public float SqrMagnitude
        {
            get { return (X * X) + (Y * Y) + (Z * Z); }
        }

        public float Magnitude
        {
            get { return (float)System.Math.Sqrt((X * X) + (Y * Y) + (Z * Z)); }
        }

        /// <summary>
        /// Returns a unit-length copy, or <see cref="Zero"/> when the vector is
        /// too short to normalise. Never returns NaN.
        /// </summary>
        public Float3 Normalized
        {
            get
            {
                float sqr = SqrMagnitude;
                if (sqr <= FMath.EpsilonSqr)
                {
                    return Zero;
                }

                float inv = 1f / (float)System.Math.Sqrt(sqr);
                return new Float3(X * inv, Y * inv, Z * inv);
            }
        }

        /// <summary>Projection onto the horizontal plane, normalised. Used for movement and facing.</summary>
        public Float3 FlattenedXZ
        {
            get
            {
                var flat = new Float3(X, 0f, Z);
                return flat.Normalized;
            }
        }

        public static Float3 operator +(Float3 a, Float3 b)
        {
            return new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Float3 operator -(Float3 a, Float3 b)
        {
            return new Float3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Float3 operator -(Float3 a)
        {
            return new Float3(-a.X, -a.Y, -a.Z);
        }

        public static Float3 operator *(Float3 a, float scalar)
        {
            return new Float3(a.X * scalar, a.Y * scalar, a.Z * scalar);
        }

        public static Float3 operator *(float scalar, Float3 a)
        {
            return new Float3(a.X * scalar, a.Y * scalar, a.Z * scalar);
        }

        public static Float3 operator /(Float3 a, float scalar)
        {
            if (scalar == 0f)
            {
                return Zero;
            }

            return new Float3(a.X / scalar, a.Y / scalar, a.Z / scalar);
        }

        public static Float3 operator -(Float3 a, float scalar)
        {
            return new Float3(a.X - scalar, a.Y - scalar, a.Z - scalar);
        }

        public static bool operator ==(Float3 a, Float3 b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Float3 a, Float3 b)
        {
            return !a.Equals(b);
        }

        public static float Dot(Float3 a, Float3 b)
        {
            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }

        public static Float3 Cross(Float3 a, Float3 b)
        {
            return new Float3(
                (a.Y * b.Z) - (a.Z * b.Y),
                (a.Z * b.X) - (a.X * b.Z),
                (a.X * b.Y) - (a.Y * b.X));
        }

        public static float Distance(Float3 a, Float3 b)
        {
            return (a - b).Magnitude;
        }

        /// <summary>Horizontal distance, ignoring height. The right metric for most aggro ranges.</summary>
        public static float DistanceXZ(Float3 a, Float3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        }

        public static float SqrDistanceXZ(Float3 a, Float3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (dx * dx) + (dz * dz);
        }

        public static Float3 Lerp(Float3 a, Float3 b, float t)
        {
            float c = FMath.Clamp01(t);
            return new Float3(
                a.X + ((b.X - a.X) * c),
                a.Y + ((b.Y - a.Y) * c),
                a.Z + ((b.Z - a.Z) * c));
        }

        /// <summary>
        /// Steps from <paramref name="current"/> toward <paramref name="target"/> by at
        /// most <paramref name="maxDelta"/> units. Used by steering and camera follow.
        /// </summary>
        public static Float3 MoveTowards(Float3 current, Float3 target, float maxDelta)
        {
            Float3 delta = target - current;
            float distance = delta.Magnitude;
            if (distance <= maxDelta || distance <= FMath.Epsilon)
            {
                return target;
            }

            return current + (delta / distance * maxDelta);
        }

        /// <summary>
        /// Rotation around the Y axis by <paramref name="degrees"/>. Used to convert a
        /// facing direction into a movement direction for strafing enemies.
        /// </summary>
        public Float3 RotateY(float degrees)
        {
            float radians = degrees * FMath.Deg2Rad;
            float cos = (float)System.Math.Cos(radians);
            float sin = (float)System.Math.Sin(radians);
            return new Float3((X * cos) + (Z * sin), Y, (-X * sin) + (Z * cos));
        }

        public bool Equals(Float3 other)
        {
            return FMath.Approximately(X, other.X)
                && FMath.Approximately(Y, other.Y)
                && FMath.Approximately(Z, other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is Float3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ", " + Z.ToString("0.###") + ")";
        }
    }
}
