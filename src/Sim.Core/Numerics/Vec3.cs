namespace Sim.Core.Numerics;

/// <summary>
/// A 3-vector of <see cref="Fixed64"/>. Used for positions and directions in the sim.
/// Unit is context-dependent and documented at the call site (orbital math works in gigametres;
/// the ephemeris table stores millimetres as raw int64, not this type).
/// </summary>
public readonly struct Vec3 : IEquatable<Vec3>
{
    public Fixed64 X { get; }
    public Fixed64 Y { get; }
    public Fixed64 Z { get; }

    public Vec3(Fixed64 x, Fixed64 y, Fixed64 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X.Raw, Y.Raw, Z.Raw);
    public override string ToString() => $"({X}, {Y}, {Z})";
}
