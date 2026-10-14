using System;

namespace Stickies.Core.Overlay
{
    /// <summary>Minimal 3D vector for the Revit-free marker maths.</summary>
    public struct V3
    {
        public V3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double k) => new V3(a.X * k, a.Y * k, a.Z * k);

        public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public V3 Cross(V3 b) => new V3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(Dot(this));
        public V3 Normalize() => Length < 1e-12 ? this : this * (1 / Length);

        public static V3 FromArray(double[] a) => a == null || a.Length < 3 ? default(V3) : new V3(a[0], a[1], a[2]);

        public override string ToString() => "(" + X + ", " + Y + ", " + Z + ")";
    }
}
