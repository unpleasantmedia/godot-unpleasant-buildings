using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>What a building's roof is like.</summary>
public enum RoofType
{
    /// <summary>Flat, with a parapet round it.</summary>
    Flat,

    /// <summary>A slope up from every wall.</summary>
    Hipped,

    /// <summary>Two slopes meeting at a ridge along the roof side, with gable walls at their ends.</summary>
    Gabled,

    /// <summary>One slope, down towards the roof side.</summary>
    Shed,
}

/// <summary>
/// A pitched roof's shape, over an outline turned outward (see <see cref="BuildingMesher.Outward"/>):
/// slopes, each a plane rising from a line at the pitch, and the roof wherever the lowest of them is.
/// Heights are above the top of the walls.
/// </summary>
public sealed class RoofShape
{
    /// <summary>A slope: rising from the line through Origin, towards Inward, at the pitch.</summary>
    private readonly record struct Slope(Vector2 Origin, Vector2 Inward, Vector2 Along, bool OnlyInward);

    private readonly List<Slope> _slopes = [];
    private readonly float _rise;
    private readonly float _overhang;
    private readonly RoofType _type;
    private readonly Vector2[] _ring;

    /// <summary>The roof's edge from above: the outline, reached out by the overhang.</summary>
    public Vector2[] Edge { get; }

    /// <param name="ring">The outline, turned outward.</param>
    /// <param name="side">For gabled and shed roofs, which side of the ring it goes by.</param>
    public RoofShape(Vector2[] ring, RoofType type, int side, float pitchDegrees, float overhang)
    {
        _type = type;
        _ring = ring;
        _rise = Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(pitchDegrees, 1f, 80f)));
        _overhang = Mathf.Max(0f, overhang);
        Edge = _overhang > 0.001f ? Reached(ring, _overhang) : ring;

        var n = ring.Length;
        Slope Of(int i)
        {
            var a = ring[i];
            var along = (ring[(i + 1) % n] - a).Normalized();
            return new Slope(a, new Vector2(-along.Y, along.X), along, false);
        }

        switch (type)
        {
            case RoofType.Hipped:
                for (var i = 0; i < n; i++)
                    _slopes.Add(Of(i) with { OnlyInward = true });
                break;
            case RoofType.Gabled:
            {
                var first = Of(side);
                // Across to the farthest corner from the side: the far slope rises from there, back towards it.
                var width = ring.Max(c => (c - first.Origin).Dot(first.Inward));
                _slopes.Add(first);
                _slopes.Add(new Slope(first.Origin + first.Inward * width, -first.Inward, -first.Along, false));
                break;
            }

            default:
                _slopes.Add(Of(side));
                break;
        }
    }

    /// <summary>How far in from a slope's line a place is (its height, over the pitch).</summary>
    private static float In(Slope s, Vector2 p) => (p - s.Origin).Dot(s.Inward);

    /// <summary>Whether a slope reaches a place: hips don't reach back past their own walls (and the overhang).</summary>
    private bool Reaches(Slope s, Vector2 p) => !s.OnlyInward || In(s, p) >= -_overhang - 0.001f;

    private float HeightOf(Slope s, Vector2 p) => In(s, p) * _rise;

    /// <summary>The roof's height over a place: its lowest slope's there.</summary>
    public float HeightAt(Vector2 p)
    {
        var best = float.PositiveInfinity;
        foreach (var s in _slopes)
            if (Reaches(s, p))
                best = Mathf.Min(best, HeightOf(s, p));
        return float.IsPositiveInfinity(best) ? 0f : best;
    }

    /// <summary>
    /// Along a line from a to b, where the roof's height changes how it goes (one slope taking over
    /// from another): as fractions of the way, from 0 to 1, in order. Between two of them its height
    /// goes straight, so walls and edges built between them meet it exactly.
    /// </summary>
    public List<float> Breaks(Vector2 a, Vector2 b)
    {
        var ts = new List<float> { 0f, 1f };
        void Cross(float f0, float f1)
        {
            if ((f0 < 0f) != (f1 < 0f) && Mathf.Abs(f0 - f1) > 1e-6f)
            {
                var t = f0 / (f0 - f1);
                if (t > 1e-4f && t < 1f - 1e-4f)
                    ts.Add(t);
            }
        }

        for (var i = 0; i < _slopes.Count; i++)
        {
            var si = _slopes[i];
            if (si.OnlyInward)
                Cross(In(si, a) + _overhang, In(si, b) + _overhang);
            for (var j = i + 1; j < _slopes.Count; j++)
                Cross(In(si, a) - In(_slopes[j], a), In(si, b) - In(_slopes[j], b));
        }

        ts.Sort();
        return ts;
    }

    /// <summary>The roof's faces from above: for each slope, where it is the lowest, each with its slope's number.</summary>
    public List<(int Slope, Vector2[] Shape)> Faces()
    {
        var faces = new List<(int, Vector2[])>();
        if (_type == RoofType.Hipped)
        {
            // Hips by the outline's straight skeleton (the lowest slope is only right without inner
            // corners), each face reaching out to the eaves on its own slope.
            var skeleton = StraightSkeleton.Faces(_ring);
            for (var i = 0; i < skeleton.Count; i++)
            {
                var face = skeleton[i];
                if (_overhang > 0.001f)
                {
                    var j = (i + 1) % _ring.Length;
                    face = [Edge[i], Edge[j], .. face.Skip(1), face[0]];
                }

                faces.Add((i, face));
            }

            return faces;
        }

        var size = Edge.Aggregate(0f, (m, c) => Mathf.Max(m, c.Length())) * 4f + 100f;
        for (var i = 0; i < _slopes.Count; i++)
        {
            var si = _slopes[i];
            var parts = new List<Vector2[]> { Edge };
            if (si.OnlyInward)
                parts = Keep(parts, HalfPlane(-si.Inward, -si.Inward.Dot(si.Origin) + _overhang, size));
            for (var j = 0; j < _slopes.Count && parts.Count > 0; j++)
            {
                if (j == i)
                    continue;
                var sj = _slopes[j];
                // Where slope j is lower than slope i (and reaches): not slope i's.
                var normal = sj.Inward - si.Inward;
                var lower = HalfPlane(normal, sj.Inward.Dot(sj.Origin) - si.Inward.Dot(si.Origin), size);
                if (sj.OnlyInward)
                    lower = Intersect(lower, HalfPlane(-sj.Inward, -sj.Inward.Dot(sj.Origin) + _overhang, size));
                parts = Cut(parts, lower);
            }

            faces.AddRange(parts.Where(p => p.Length >= 3 && !Geometry2D.IsPolygonClockwise(p)).Select(p => (i, p)));
        }

        return faces;
    }

    /// <summary>A face's point on its slope, in 3D (its height above the walls' top, which is added after).</summary>
    public Vector3 On(int slope, Vector2 p) => new(p.X, HeightOf(_slopes[slope], p), p.Y);

    /// <summary>A slope's upward direction, square to it.</summary>
    public Vector3 Normal(int slope)
    {
        var s = _slopes[slope];
        return new Vector3(-s.Inward.X * _rise, 1f, -s.Inward.Y * _rise).Normalized();
    }

    /// <summary>Where a texture goes on a slope, in metres: along its line, and up it.</summary>
    public Vector2 Uv(int slope, Vector2 p)
    {
        var s = _slopes[slope];
        return new Vector2(p.Dot(s.Along), -In(s, p) * Mathf.Sqrt(1f + _rise * _rise));
    }

    /// <summary>An outline reached out by a distance, corners staying sharp (one corner for each of the outline's).</summary>
    public static Vector2[] Reached(Vector2[] ring, float by)
    {
        var n = ring.Length;
        var reached = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var before = BuildingMesher.Out(ring[(i - 1 + n) % n], ring[i]);
            var after = BuildingMesher.Out(ring[i], ring[(i + 1) % n]);
            var n1 = new Vector2(before.X, before.Z);
            var n2 = new Vector2(after.X, after.Z);
            var sum = n1 + n2;
            var cos = n1.Dot(n2);
            // Going back on itself: out square to the side before.
            reached[i] = cos < -0.999f ? ring[i] + n1 * by : ring[i] + sum * (by / (1f + cos));
        }

        return reached;
    }

    /// <summary>Everything where Normal · p is at most Limit, as a big square-ish polygon.</summary>
    private static Vector2[] HalfPlane(Vector2 normal, float limit, float size)
    {
        var length = normal.Length();
        if (length < 1e-6f)
            return limit >= 0f ? [new(-size, -size), new(size, -size), new(size, size), new(-size, size)] : [];
        var unit = normal / length;
        var on = unit * (limit / length);
        var across = new Vector2(-unit.Y, unit.X) * size;
        var back = -unit * size;
        return [on - across, on + across, on + across + back, on - across + back];
    }

    private static Vector2[] Intersect(Vector2[] a, Vector2[] b)
    {
        if (a.Length == 0 || b.Length == 0)
            return [];
        var both = Geometry2D.IntersectPolygons(a, b);
        return both.Count > 0 ? both[0] : [];
    }

    private static List<Vector2[]> Keep(List<Vector2[]> parts, Vector2[] within) =>
        within.Length == 0 ? [] : parts.SelectMany(p => Geometry2D.IntersectPolygons(p, within)).ToList();

    private static List<Vector2[]> Cut(List<Vector2[]> parts, Vector2[] away) =>
        away.Length == 0 ? parts : parts.SelectMany(p => Geometry2D.ClipPolygons(p, away)).ToList();
}
