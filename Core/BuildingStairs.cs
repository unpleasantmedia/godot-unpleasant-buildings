using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>One step (or a landing) of a staircase, in its own space: across from X0 to X1, along from Z0 to Z1, its top so high above its foot.</summary>
public readonly record struct StairCell(float X0, float X1, float Z0, float Z1, float Top);

/// <summary>
/// A staircase laid out: its steps and landing (in order, up), the edge the last one meets the floor
/// above at, and the outline of the hole it needs in that floor (all in its own space).
/// </summary>
public sealed record StairLayout(List<StairCell> Cells, (Vector2 A, Vector2 B) Arrival, Vector2[] Hole, float Rise, int Steps, int First);

/// <summary>The materials of a building's stairs.</summary>
public readonly record struct StairMaterials(Material? Steps, Material? Railing);

public static class StairPlan
{

    /// <summary>How high a step may be at most, in metres.</summary>
    public const float MaxRise = 0.19f;

    /// <summary>The gap between a U's two flights.</summary>
    public const float Gap = 0.1f;

    /// <summary>The smallest a staircase may be along the way it climbs (its first flight, and an L's or U's landing), in metres.</summary>
    public const float MinLength = 3f;

    /// <summary>The smallest a staircase may be across, in metres.</summary>
    public const float MinWidth = 1f;

    /// <summary>An L's or U's steps before its landing.</summary>
    private static int Turn(Staircase s, int n) => s.TurnAfter > 0 ? Mathf.Clamp(s.TurnAfter, 1, n - 2) : n / 2;

    /// <summary>A staircase's steps, climbing so high (a storey: floor to floor).</summary>
    public static StairLayout Lay(Staircase s, float height)
    {
        var n = Mathf.Max(2, Mathf.CeilToInt(height / MaxRise));
        var rise = height / n;
        var w = s.Width;
        // Never shorter than MinLength along the way it climbs: its steps deepened to reach it.
        var t = s.Shape == StairShape.Straight
            ? Mathf.Max(s.Tread, MinLength / n)
            : Mathf.Max(s.Tread, (MinLength - w) / Turn(s, n));
        var half = w / 2f;
        var cells = new List<StairCell>();
        if (s.Shape == StairShape.Straight)
        {
            for (var k = 0; k < n; k++)
                cells.Add(new(-half, half, k * t, (k + 1) * t, (k + 1) * rise));
            var end = n * t;
            return new(cells, (new(-half, end), new(half, end)), [new(-half, 0f), new(half, 0f), new(half, end), new(-half, end)], rise, n, n);
        }

        // Turning right (going up) is +X here, so left is -X.
        var side = s.TurnLeft ? -1f : 1f;
        var first = Turn(s, n);
        var second = n - first - 1;
        for (var k = 0; k < first; k++)
            cells.Add(new(-half, half, k * t, (k + 1) * t, (k + 1) * rise));
        var landing = first * t;
        if (s.Shape == StairShape.L)
        {
            cells.Add(new(-half, half, landing, landing + w, (first + 1) * rise));
            for (var j = 0; j < second; j++)
            {
                var (a, b) = (side * (half + j * t), side * (half + (j + 1) * t));
                cells.Add(new(Mathf.Min(a, b), Mathf.Max(a, b), landing, landing + w, (first + 2 + j) * rise));
            }

            var reach = side * (half + second * t);
            Vector2[] hole = side > 0f
                ? [new(-half, 0f), new(half, 0f), new(half, landing), new(reach, landing), new(reach, landing + w), new(-half, landing + w)]
                : [new(-half, 0f), new(half, 0f), new(half, landing + w), new(reach, landing + w), new(reach, landing), new(-half, landing)];
            return new(cells, (new(reach, landing), new(reach, landing + w)), hole, rise, n, first);
        }

        // U: the second flight beside the first, coming back.
        var middle = side * (w + Gap);
        var (l0, l1) = (Mathf.Min(-half, middle - half), Mathf.Max(half, middle + half));
        cells.Add(new(l0, l1, landing, landing + w, (first + 1) * rise));
        for (var j = 0; j < second; j++)
            cells.Add(new(middle - half, middle + half, landing - (j + 1) * t, landing - j * t, (first + 2 + j) * rise));
        var arrive = landing - second * t;
        var bottom = arrive;
        return new(cells, (new(middle - half, arrive), new(middle + half, arrive)),
            [new(l0, bottom), new(l1, bottom), new(l1, landing + w), new(l0, landing + w)], rise, n, first);
    }

    /// <summary>Its outline on the ground (where its steps are), in its own space.</summary>
    public static Vector2[] Footprint(StairLayout layout)
    {
        var polygons = new List<Vector2[]>();
        foreach (var c in layout.Cells)
        {
            Vector2[] box = [new(c.X0, c.Z0), new(c.X1, c.Z0), new(c.X1, c.Z1), new(c.X0, c.Z1)];
            polygons = polygons.Count == 0 ? [box] : Merge(polygons, box);
        }

        return polygons.OrderByDescending(p => Mathf.Abs(Area(p))).FirstOrDefault() ?? [];
    }

    private static List<Vector2[]> Merge(List<Vector2[]> polygons, Vector2[] box)
    {
        var grown = box.Select(p => p).ToArray();
        var result = new List<Vector2[]>();
        var merged = grown;
        foreach (var p in polygons)
        {
            var union = Geometry2D.MergePolygons(p, merged);
            if (union.Count == 1)
                merged = union[0];
            else
                result.Add(p);
        }

        result.Add(merged);
        return result;
    }

    private static float Area(Vector2[] ring)
    {
        var a = 0f;
        for (var i = 0; i < ring.Length; i++)
            a += ring[i].Cross(ring[(i + 1) % ring.Length]);
        return a / 2f;
    }
}

public static partial class BuildingMesher
{
    /// <summary>How thick an open stair's treads are.</summary>
    private const float TreadThickness = 0.05f;

    /// <summary>How thick an open stair's stringers are (across), and how deep under the treads they reach.</summary>
    private const float StringerThickness = 0.05f;

    private const float StringerDepth = 0.25f;

    private const float StairRailHeight = 0.9f;
    private const float StairPost = 0.06f;
    private const float StairRail = 0.05f;
    private const float Baluster = 0.025f;
    private const float BalusterSpacing = 0.12f;

    /// <summary>
    /// Fills a floor ring with holes cut out of it (flat, at a height, facing up or down), as
    /// pieces with none inside (a piece holding a hole split through it, again until none does).
    /// </summary>
    private static void CapHoled(Faces faces, Vector2[] ring, List<Vector2[]> holes, float height, bool up)
    {
        foreach (var piece in Subtract(ring, holes))
            faces.Cap(piece, height, up);
    }

    /// <summary>A ring with holes cut out, as pieces with none inside.</summary>
    public static List<Vector2[]> Subtract(Vector2[] ring, List<Vector2[]> holes)
    {
        var pieces = new List<Vector2[]> { ring };
        foreach (var hole in holes)
        {
            var next = new List<Vector2[]>();
            foreach (var piece in pieces)
                next.AddRange(Cut(piece, hole, 0));
            pieces = next;
        }

        return pieces.Where(p => p.Length >= 3).ToList();
    }

    private static List<Vector2[]> Cut(Vector2[] piece, Vector2[] hole, int depth)
    {
        var result = Geometry2D.ClipPolygons(piece, hole).ToList();
        // One of them inside another: a hole, which a cap can't have; split it down its middle.
        var holed = result.Any(r => result.Any(o => o != r && Geometry2D.IsPointInPolygon(r[0], o)));
        if (!holed || depth > 4)
            return holed ? result.Where(r => !result.Any(o => o != r && Geometry2D.IsPointInPolygon(r[0], o))).ToList() : result;
        var middle = hole.Aggregate(Vector2.Zero, (a, p) => a + p) / hole.Length;
        const float far = 100000f;
        Vector2[] left = [new(-far, -far), new(middle.X, -far), new(middle.X, far), new(-far, far)];
        Vector2[] right = [new(middle.X, -far), new(far, -far), new(far, far), new(middle.X, far)];
        var pieces = new List<Vector2[]>();
        foreach (var half in new[] { left, right })
            foreach (var part in Geometry2D.IntersectPolygons(piece, half))
                pieces.AddRange(Cut(part, hole, depth + 1));
        return pieces;
    }

    /// <summary>A staircase as it is to be built: laid out, on a floor, with whether a floor is over it (for its hole's railing), and what is against a wall on its floor and the one above.</summary>
    public readonly record struct PlacedStairs(Staircase Stairs, StairLayout Layout, float Floor, float? Above, System.Func<Vector2, Vector2, bool> WallBelow, System.Func<Vector2, Vector2, bool> WallAbove);

    /// <summary>Staircases, onto a mesh.</summary>
    public static void AllStairs(ArrayMesh mesh, List<PlacedStairs> stairs, StairMaterials materials)
    {
        var steps = new Faces();
        var railing = new Faces();
        foreach (var p in stairs)
        {
            Stairs(steps, railing, p.Stairs, p.Layout, p.Floor, p.WallBelow);
            if (p.Above is { } above && p.Stairs.HoleRailing)
                HoleRailing(railing, p.Stairs, p.Layout, above, p.WallAbove);
        }

        steps.AddTo(mesh, materials.Steps);
        railing.AddTo(mesh, materials.Railing);
    }

    /// <summary>A staircase: its steps (solid down to the floor, or open treads on stringers) and its railings, from a floor.</summary>
    private static void Stairs(Faces steps, Faces railing, Staircase s, StairLayout layout, float floor, System.Func<Vector2, Vector2, bool> againstWall)
    {
        Vector3 P(float x, float z, float y)
        {
            var g = s.At(x, z);
            return new Vector3(g.X, floor + y, g.Y);
        }

        var right3 = new Vector3(s.Right.X, 0f, s.Right.Y);
        var forward3 = new Vector3(s.Forward.X, 0f, s.Forward.Y);
        foreach (var c in layout.Cells)
        {
            var bottom = s.Solid ? 0f : c.Top - TreadThickness;
            // A landing, open, is thicker.
            if (!s.Solid && c.Z1 - c.Z0 > layout.Cells[0].Z1 - layout.Cells[0].Z0 + 0.01f && c.X1 - c.X0 > layout.Cells[0].Z1 - layout.Cells[0].Z0 + 0.01f)
                bottom = c.Top - 0.15f;
            CellBox(steps, P, right3, forward3, c, bottom);
        }

        var runs = Runs(layout);
        if (!s.Solid)
            foreach (var r in runs.Where(r => Mathf.Abs(r.H1 - r.H0) > 0.01f))
                Stringer(steps, s, r, floor);

        if (s.Railing)
            foreach (var r in runs.Where(r => !againstWall(s.At(r.A.X, r.A.Y), s.At(r.B.X, r.B.Y))))
                RailingRun(railing, s.At(r.A.X, r.A.Y), s.At(r.B.X, r.B.Y), floor + r.H0, floor + r.H1, s.Right * r.Inward.X + s.Forward * r.Inward.Y, inside: true);
    }

    /// <summary>The hole's railing on the floor above: round its edge but where the stairs come up, and where it is against a wall.</summary>
    private static void HoleRailing(Faces railing, Staircase s, StairLayout layout, float floor, System.Func<Vector2, Vector2, bool> againstWall)
    {
        var hole = layout.Hole;
        var (aa, ab) = layout.Arrival;
        for (var i = 0; i < hole.Length; i++)
        {
            var a = hole[i];
            var b = hole[(i + 1) % hole.Length];
            var pieces = new List<(Vector2, Vector2)>();
            // On the line the stairs arrive at: that part left open.
            var d = (b - a).Normalized();
            if (Mathf.Abs(d.Cross(ab - aa)) < 0.001f && Mathf.Abs(d.Cross(aa - a)) < 0.01f)
            {
                var length = a.DistanceTo(b);
                var (t0, t1) = ((aa - a).Dot(d), (ab - a).Dot(d));
                if (t0 > t1)
                    (t0, t1) = (t1, t0);
                if (t0 > 0.1f)
                    pieces.Add((a, a + d * Mathf.Min(t0, length)));
                if (length - t1 > 0.1f)
                    pieces.Add((a + d * Mathf.Max(t1, 0f), b));
            }
            else
                pieces.Add((a, b));

            var inside = hole.Aggregate(Vector2.Zero, (m, p) => m + p) / hole.Length;
            foreach (var (p, q) in pieces)
            {
                var (gp, gq) = (s.At(p.X, p.Y), s.At(q.X, q.Y));
                if (againstWall(gp, gq))
                    continue;
                var mid = (p + q) / 2f;
                var toward = s.At(inside.X, inside.Y) - s.At(mid.X, mid.Y);
                // Set back from the edge onto the floor (away from the hole), not over it.
                RailingRun(railing, gp, gq, floor, floor, -toward, inside: false);
            }
        }
    }

    /// <summary>A side of a flight or a landing with nothing beside it: from A to B (in its own space), the steps so high at each end, and which way is in (onto the steps, on the ground).</summary>
    private readonly record struct StairRun(Vector2 A, Vector2 B, float H0, float H1, Vector2 Inward);

    /// <summary>The steps' open sides, joined into runs along each straight stretch that climbs evenly.</summary>
    private static List<StairRun> Runs(StairLayout layout)
    {
        var cells = layout.Cells;
        var edges = new List<(Vector2 A, Vector2 B, float Top, Vector2 Out, int Cell)>();
        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            (Vector2, Vector2, Vector2)[] sides =
            [
                (new(c.X0, c.Z0), new(c.X0, c.Z1), new(-1f, 0f)),
                (new(c.X1, c.Z0), new(c.X1, c.Z1), new(1f, 0f)),
                (new(c.X0, c.Z0), new(c.X1, c.Z0), new(0f, -1f)),
                (new(c.X0, c.Z1), new(c.X1, c.Z1), new(0f, 1f)),
            ];
            foreach (var (a, b, o) in sides)
            {
                // Open only where no step (higher, or the same) is beside it, along all of it.
                var open = new List<(Vector2, Vector2)> { (a, b) };
                foreach (var (other, k) in cells.Select((x, k) => (x, k)).Where(x => x.k != i))
                {
                    var next = new List<(Vector2, Vector2)>();
                    foreach (var (p, q) in open)
                        next.AddRange(Uncovered(p, q, o, other));
                    open = next;
                }

                foreach (var (p, q) in open.Where(e => e.Item1.DistanceTo(e.Item2) > 0.02f))
                {
                    // Not the first step's front, nor the edge it arrives at.
                    if (i == 0 && o == new Vector2(0f, -1f))
                        continue;
                    if (i == cells.Count - 1 && OnSegment(p, layout.Arrival) && OnSegment(q, layout.Arrival))
                        continue;
                    // A step's front or back (across the way it climbs, with steps on the other side) is no side.
                    edges.Add((p, q, c.Top, o, i));
                }
            }
        }

        // Of the edges across a flight, only those with nothing in front or behind: those were dropped
        // above (covered by the next step); join the rest along their lines.
        var runs = new List<StairRun>();
        var used = new bool[edges.Count];
        for (var i = 0; i < edges.Count; i++)
        {
            if (used[i])
                continue;
            used[i] = true;
            var e = edges[i];
            var dir = (e.B - e.A).Normalized();
            var start = e.A;
            var end = e.B;
            var (h0, h1) = (e.Top, e.Top);
            var step = float.NaN;
            var grown = true;
            while (grown)
            {
                grown = false;
                for (var j = 0; j < edges.Count; j++)
                {
                    if (used[j] || edges[j].Out != e.Out)
                        continue;
                    var f = edges[j];
                    if (Mathf.Abs((f.A - start).Cross(dir)) > 0.001f)
                        continue;
                    if (Mathf.Abs(f.A.DistanceTo(f.B) - e.A.DistanceTo(e.B)) > 0.001f)
                        continue;
                    if (f.A.DistanceTo(end) < 0.001f && (float.IsNaN(step) || Mathf.Abs(f.Top - h1 - step) < 0.001f))
                    {
                        step = f.Top - h1;
                        h1 = f.Top;
                        end = f.B;
                    }
                    else if (f.B.DistanceTo(start) < 0.001f && (float.IsNaN(step) || Mathf.Abs(h0 - f.Top - step) < 0.001f))
                    {
                        step = h0 - f.Top;
                        h0 = f.Top;
                        start = f.A;
                    }
                    else
                        continue;
                    used[j] = true;
                    grown = true;
                }
            }

            if (float.IsNaN(step))
                step = 0f;
            // Along the nosings' middles: half a step below the first's top, half above the last's.
            runs.Add(new(start, end, h0 - step / 2f, h1 + step / 2f, -e.Out));
        }

        return runs;
    }

    private static bool OnSegment(Vector2 p, (Vector2 A, Vector2 B) s) =>
        Geometry2D.GetClosestPointToSegment(p, s.A, s.B).DistanceTo(p) < 0.001f;

    /// <summary>The parts of an edge (facing out) not against another cell beside it, the other side.</summary>
    private static IEnumerable<(Vector2, Vector2)> Uncovered(Vector2 a, Vector2 b, Vector2 o, StairCell other)
    {
        // The other cell's span along this edge, if it sits right against it on its out side.
        var along = o.X != 0f ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
        var line = o.X != 0f ? a.X : a.Y;
        var touches = o.X switch
        {
            < 0f => Mathf.Abs(other.X1 - line) < 0.001f,
            > 0f => Mathf.Abs(other.X0 - line) < 0.001f,
            _ => o.Y < 0f ? Mathf.Abs(other.Z1 - line) < 0.001f : Mathf.Abs(other.Z0 - line) < 0.001f,
        };
        var (s0, s1) = o.X != 0f ? (other.Z0, other.Z1) : (other.X0, other.X1);
        var (e0, e1) = (a.Dot(along), b.Dot(along));
        if (!touches || s1 <= e0 + 0.001f || s0 >= e1 - 0.001f)
        {
            yield return (a, b);
            yield break;
        }

        if (s0 > e0 + 0.001f)
            yield return (a, a + along * (s0 - e0));
        if (s1 < e1 - 0.001f)
            yield return (a + along * (s1 - e0), b);
    }

    private static void CellBox(Faces f, System.Func<float, float, float, Vector3> P, Vector3 right, Vector3 forward, StairCell c, float bottom)
    {
        var (x0, x1, z0, z1, lo, hi) = (c.X0, c.X1, c.Z0, c.Z1, bottom, c.Top);
        var w = x1 - x0;
        var d = z1 - z0;
        f.Quad(P(x0, z0, hi), P(x1, z0, hi), P(x1, z1, hi), P(x0, z1, hi), Vector3.Up, new(x0, z0), new(x1, z0), new(x1, z1), new(x0, z1));
        f.Quad(P(x0, z0, lo), P(x1, z0, lo), P(x1, z1, lo), P(x0, z1, lo), Vector3.Down, new(x0, z0), new(x1, z0), new(x1, z1), new(x0, z1));
        f.Quad(P(x0, z0, lo), P(x1, z0, lo), P(x1, z0, hi), P(x0, z0, hi), -forward, new(x0, -lo), new(x1, -lo), new(x1, -hi), new(x0, -hi));
        f.Quad(P(x0, z1, lo), P(x1, z1, lo), P(x1, z1, hi), P(x0, z1, hi), forward, new(x0, -lo), new(x1, -lo), new(x1, -hi), new(x0, -hi));
        f.Quad(P(x0, z0, lo), P(x0, z1, lo), P(x0, z1, hi), P(x0, z0, hi), -right, new(z0, -lo), new(z1, -lo), new(z1, -hi), new(z0, -hi));
        f.Quad(P(x1, z0, lo), P(x1, z1, lo), P(x1, z1, hi), P(x1, z0, hi), right, new(z0, -lo), new(z1, -lo), new(z1, -hi), new(z0, -hi));
        _ = w + d;
    }

    /// <summary>A sloping board under an open flight's side, from just above its nosings down under its treads.</summary>
    private static void Stringer(Faces f, Staircase s, StairRun r, float floor)
    {
        var a = s.At(r.A.X, r.A.Y);
        var b = s.At(r.B.X, r.B.Y);
        var inward = s.Right * r.Inward.X + s.Forward * r.Inward.Y;
        a += inward * (StringerThickness / 2f);
        b += inward * (StringerThickness / 2f);
        Plate(f, a, b, floor + Mathf.Max(0f, r.H0 - StringerDepth), floor + r.H0 + 0.05f, floor + Mathf.Max(0f, r.H1 - StringerDepth), floor + r.H1 + 0.05f, StringerThickness);
    }

    /// <summary>A board standing on the line from a to b, its bottom and top sloping from one end's heights to the other's, so thick (centred on the line).</summary>
    private static void Plate(Faces f, Vector2 a, Vector2 b, float aLo, float aHi, float bLo, float bHi, float thickness)
    {
        var length = a.DistanceTo(b);
        if (length < 0.001f)
            return;
        var d = (b - a) / length;
        var n2 = new Vector2(-d.Y, d.X) * (thickness / 2f);
        var n = new Vector3(n2.X, 0f, n2.Y).Normalized();
        var along = new Vector3(d.X, 0f, d.Y);
        Vector3 V(Vector2 p, float y) => new(p.X, y, p.Y);
        var (a0, a1, b0, b1) = (a - n2, a + n2, b - n2, b + n2);
        f.Quad(V(a0, aLo), V(b0, bLo), V(b0, bHi), V(a0, aHi), -n, new(0f, -aLo), new(length, -bLo), new(length, -bHi), new(0f, -aHi));
        f.Quad(V(a1, aLo), V(b1, bLo), V(b1, bHi), V(a1, aHi), n, new(0f, -aLo), new(length, -bLo), new(length, -bHi), new(0f, -aHi));
        var up = new Vector3(0f, 1f, 0f);
        var slopeUp = (V(b, bHi) - V(a, aHi)).Cross(n).Normalized();
        if (slopeUp.Y < 0f)
            slopeUp = -slopeUp;
        f.Quad(V(a0, aHi), V(b0, bHi), V(b1, bHi), V(a1, aHi), slopeUp, a0, b0, b1, a1);
        f.Quad(V(a0, aLo), V(b0, bLo), V(b1, bLo), V(a1, aLo), -slopeUp, a0, b0, b1, a1);
        f.Quad(V(a0, aLo), V(a1, aLo), V(a1, aHi), V(a0, aHi), -along, new(0f, -aLo), new(thickness, -aLo), new(thickness, -aHi), new(0f, -aHi));
        f.Quad(V(b0, bLo), V(b1, bLo), V(b1, bHi), V(b0, bHi), along, new(0f, -bLo), new(thickness, -bLo), new(thickness, -bHi), new(0f, -bHi));
        _ = up;
    }

    /// <summary>A railing from a to b (on the ground), standing on heights h0 and h1 there: posts at its ends (and between, at most a metre and a half apart), balusters, and a handrail, set in from the edge.</summary>
    /// <param name="inside">Whether it runs round the inside of its corners (a staircase's sides: its ends drawn in along it) or the outside (a hole's: its ends drawn out past them), so where two runs meet at a corner their end posts are one.</param>
    private static void RailingRun(Faces f, Vector2 a, Vector2 b, float h0, float h1, Vector2 inward, bool inside)
    {
        var length = a.DistanceTo(b);
        if (length < 0.15f)
            return;
        var h = StairPost / 2f;
        var inset = inward.Normalized() * h;
        var along = (b - a) / length * (inside ? h : -h);
        a += inset + along;
        b += inset - along;
        length = a.DistanceTo(b);
        float H(float t) => Mathf.Lerp(h0, h1, t);
        // The handrail, sloping with the steps.
        Plate(f, a, b, h0 + StairRailHeight - StairRail, h0 + StairRailHeight, h1 + StairRailHeight - StairRail, h1 + StairRailHeight, StairRail);
        var posts = Mathf.Max(1, Mathf.CeilToInt(length / 1.5f));
        for (var k = 0; k <= posts; k++)
        {
            var t = (float)k / posts;
            StairUpright(f, a.Lerp(b, t), H(t), H(t) + StairRailHeight, StairPost);
        }

        var balusters = Mathf.FloorToInt(length / BalusterSpacing);
        for (var k = 1; k < balusters; k++)
        {
            var t = (float)k / balusters;
            StairUpright(f, a.Lerp(b, t), H(t), H(t) + StairRailHeight - StairRail, Baluster);
        }
    }

    /// <summary>A square upright, so wide, from one height to another.</summary>
    private static void StairUpright(Faces f, Vector2 at, float lo, float hi, float width)
    {
        var h = width / 2f;
        Vector2[] ring = [at + new Vector2(-h, -h), at + new Vector2(h, -h), at + new Vector2(h, h), at + new Vector2(-h, h)];
        f.Band(Outward(ring), lo, hi);
        f.Cap(ring, hi, up: true);
    }
}

public partial class Building
{
    private Godot.Collections.Array<Staircase> _stairs = [];
    private Material? _stairMaterial;
    private Material? _stairRailingMaterial;

    /// <summary>Its staircases, each from a storey up to the next: drawn in the 3D view (Stairs, with a storey picked). Only built when it is enterable.</summary>
    [ExportGroup("Interior")]
    [Export]
    public Godot.Collections.Array<Staircase> Stairs
    {
        get => _stairs;
        set
        {
            foreach (var s in _stairs.Where(s => s is not null))
                if (s.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    s.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            _stairs = value ?? [];
            foreach (var s in _stairs.Where(s => s is not null))
                if (!s.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    s.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>What its stairs' steps are made of; with none, its floors'.</summary>
    [Export]
    public Material? StairMaterial
    {
        get => _stairMaterial;
        set
        {
            _stairMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What its stairs' railings are made of; with none, its stairs'.</summary>
    [Export]
    public Material? StairRailingMaterial
    {
        get => _stairRailingMaterial;
        set
        {
            _stairRailingMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>The staircases that can be built: from a storey with one above it.</summary>
    public IEnumerable<Staircase> PlacedStairs() => _stairs.Where(s => s is not null && s.Storey < _storeys - 1);

    /// <summary>A staircase laid out for its storey's height.</summary>
    public StairLayout LayOut(Staircase s) => StairPlan.Lay(s, _storeyHeight);

    /// <summary>The holes the stairs need in a storey's floor, in the building's own space (kept inside its walls).</summary>
    public List<Vector2[]> StairHoles(int storey, Vector2[] inner)
    {
        var holes = new List<Vector2[]>();
        foreach (var s in PlacedStairs().Where(s => s.Storey == storey - 1))
        {
            var hole = LayOut(s).Hole.Select(p => s.At(p.X, p.Y)).ToArray();
            holes.AddRange(Geometry2D.IntersectPolygons(inner, hole));
        }

        return holes;
    }

    /// <summary>Whether a line on a storey's floor runs along a wall (the outer walls' inner faces, or an interior wall's side).</summary>
    public bool AgainstWall(int storey, Vector2 a, Vector2 b, Vector2[] inner)
    {
        var mid = (a + b) / 2f;
        var d = (b - a).Normalized();
        bool Along(Vector2 p, Vector2 q, float reach)
        {
            var e = (q - p).Normalized();
            return Mathf.Abs(e.Cross(d)) < 0.05f && Geometry2D.GetClosestPointToSegment(mid, p, q).DistanceTo(mid) < reach;
        }

        for (var i = 0; i < inner.Length; i++)
            if (Along(inner[i], inner[(i + 1) % inner.Length], 0.15f))
                return true;
        return _walls.Any(w => w is not null && w.Storey == storey && w.A.DistanceTo(w.B) > 0.01f && Along(w.A, w.B, w.Thickness / 2f + 0.15f));
    }
}
