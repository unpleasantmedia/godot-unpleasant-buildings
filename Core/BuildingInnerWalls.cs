using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

public static partial class BuildingMesher
{
    /// <summary>How far a doorway's frame stands out from its wall's faces, and how wide its casing is round it.</summary>
    private const float CasingProud = 0.015f;

    private const float CasingWidth = 0.07f;

    /// <summary>How near its ceiling an opening may reach at most.</summary>
    private const float OpeningHeadroom = 0.05f;

    /// <summary>
    /// An interior wall: a box along its line, reaching half its thickness past each end (to close
    /// its corners), floor to ceiling, its doorways and arches cut through it (lined round inside,
    /// a doorway framed). With rooms, its faces are split where other walls meet them, each part
    /// going to the room it faces.
    /// </summary>
    private static void InnerWallMesh(Faces fallback, Routed? route, Faces frames, InnerWall w, RoomSet? rooms, List<DoorLeaf>? hung, Faces leaves, Faces leafGlass, LeafMaterials leafMaterials)
    {
        var length = w.A.DistanceTo(w.B);
        if (length < 0.01f)
            return;
        var d = (w.B - w.A) / length;
        var across = new Vector2(d.Y, -d.X);
        var half = w.Thickness / 2f;
        var f = new WallFrame(w.A, d, across);
        var (lo, hi) = (w.Floor, w.Ceiling);
        var along3 = f.Along;
        var side3 = f.Out;
        Faces Target(Vector3 middle, Vector3 normal) => route?.For(middle, normal) ?? fallback;

        // Its openings' outlines on its face (u along from A, v up from the floor), kept inside it.
        var most = hi - lo - OpeningHeadroom;
        var shapes = (w.Openings ?? [])
            .Where(o => o.From + o.Width <= length + 0.001f)
            .Select(o => (Opening: o, Outline: o.Outline(most).Select(p => new Vector2(p.X, lo + p.Y)).ToArray()))
            .ToList();
        var holes = shapes.Select(s => s.Outline).ToList();

        // Each face: in columns between where other walls meet it, each column with the openings cut out.
        foreach (var sign in new[] { -1f, 1f })
        {
            var normal = side3 * sign;
            var w0 = sign * half;
            var (s0, s1) = (w.A - d * half + across * w0, w.B + d * half + across * w0);
            var full = length + w.Thickness;
            var marks = (rooms?.Breaks(s0, s1) ?? []).Append(0f).Append(1f).Where(t => t >= 0f && t <= 1f)
                .Select(t => -half + full * t).Distinct().OrderBy(u => u).ToList();
            for (var k = 0; k + 1 < marks.Count; k++)
            {
                var (u0, u1) = (marks[k], marks[k + 1]);
                if (u1 - u0 < 1e-4f)
                    continue;
                var target = Target(f.At((u0 + u1) / 2f, (lo + hi) / 2f, w0), normal);
                Vector2[] column = [new(u0, lo), new(u1, lo), new(u1, hi), new(u0, hi)];
                foreach (var piece in Subtract(column, holes))
                    Planar(target, piece, p => f.At(p.X, p.Y, w0), normal);
            }
        }

        // Its ends, and its top (under the floor above, when it reaches into it).
        Vector3 P(Vector2 p, float y) => new(p.X, y, p.Y);
        var (a, b) = (w.A - d * half, w.B + d * half);
        Vector2[] plan = [a - across * half, b - across * half, b + across * half, a + across * half];
        var middle = (lo + hi) / 2f;
        Target(P((plan[0] + plan[3]) / 2f, middle), -along3).Quad(P(plan[0], lo), P(plan[3], lo), P(plan[3], hi), P(plan[0], hi), -along3, new(0f, -lo), new(w.Thickness, -lo), new(w.Thickness, -hi), new(0f, -hi));
        Target(P((plan[1] + plan[2]) / 2f, middle), along3).Quad(P(plan[1], lo), P(plan[2], lo), P(plan[2], hi), P(plan[1], hi), along3, new(0f, -lo), new(w.Thickness, -lo), new(w.Thickness, -hi), new(0f, -hi));
        var lid = w.Lid ?? hi;
        fallback.Quad(P(plan[0], lid), P(plan[1], lid), P(plan[2], lid), P(plan[3], lid), Vector3.Up, plan[0], plan[1], plan[2], plan[3]);

        // Round inside each opening (but along its foot, on the floor): the wall's thickness, facing in.
        foreach (var (opening, outline) in shapes)
        {
            var centre = outline.Aggregate(Vector2.Zero, (s, p) => s + p) / outline.Length;
            for (var i = 0; i < outline.Length; i++)
            {
                var (p, q) = (outline[i], outline[(i + 1) % outline.Length]);
                if (p.Y <= lo + 0.001f && q.Y <= lo + 0.001f)
                    continue;
                (p, q) = (p with { Y = Mathf.Max(p.Y, lo) }, q with { Y = Mathf.Max(q.Y, lo) });
                var edge = q - p;
                if (edge.Length() < 1e-4f)
                    continue;
                // Facing the opening's middle.
                var inward = new Vector2(-edge.Y, edge.X).Normalized();
                if (inward.Dot(centre - p) < 0f)
                    inward = -inward;
                var n = (along3 * inward.X + Vector3.Up * inward.Y).Normalized();
                var target = opening.Kind == InteriorOpeningKind.Doorway ? frames : fallback;
                target.Quad(f.At(p.X, p.Y, -half), f.At(q.X, q.Y, -half), f.At(q.X, q.Y, half), f.At(p.X, p.Y, half), n,
                    new(p.X + p.Y, -half), new(q.X + q.Y, -half), new(q.X + q.Y, half), new(p.X + p.Y, half));
            }

            if (opening.Kind == InteriorOpeningKind.Doorway)
            {
                Casing(frames, f, opening, outline, lo, half);
                if (opening.HasDoor)
                {
                    // Its leaf: in the opening, a little clear of its sides, head and floor; hung at A's end or B's.
                    var top = outline.Max(p => p.Y);
                    var (la, lb) = (opening.From + 0.005f, opening.From + opening.Width - 0.005f);
                    var (v0, v1) = (lo + 0.01f, top - 0.005f);
                    var (w0, w1) = (-InnerLeafThickness / 2f, InnerLeafThickness / 2f);
                    var hingeAtA = !opening.HingeRight;
                    if (hung is not null)
                    {
                        var doorway = hung.Select(l => l.Doorway).DefaultIfEmpty(-1).Max() + 1;
                        hung.Add(HungLeaf(doorway, f, (la + lb) / 2f, la, lb, v0, v1, w0, w1, false, hingeAtA, !opening.OpensBack, true, leafMaterials));
                    }
                    else
                    {
                        LeafFaces(leaves, leafGlass, frames, f, la, lb, v0, v1, w0, w1, false, hingeAtA, true);
                    }
                }
            }
        }
    }

    /// <summary>A doorway's casing: boards round it on both faces of its wall (up its sides and over its head), standing a little proud.</summary>
    private static void Casing(Faces frames, WallFrame f, InteriorOpening o, Vector2[] outline, float lo, float half)
    {
        var (u0, u1) = (o.From, o.From + o.Width);
        var top = outline.Max(p => p.Y);
        foreach (var sign in new[] { -1f, 1f })
        {
            var (w0, w1) = sign > 0f ? (half, half + CasingProud) : (-half - CasingProud, -half);
            frames.Box(f, u0 - CasingWidth, u0, lo, top + CasingWidth, w0, w1);
            frames.Box(f, u1, u1 + CasingWidth, lo, top + CasingWidth, w0, w1);
            frames.Box(f, u0, u1, top, top + CasingWidth, w0, w1);
        }

    }

    /// <summary>A flat polygon on a face (in its own 2D, mapped into the building), facing along a normal, its UVs its 2D.</summary>
    private static void Planar(Faces faces, Vector2[] polygon, System.Func<Vector2, Vector3> map, Vector3 normal)
    {
        var corners = Geometry2D.TriangulatePolygon(polygon);
        for (var i = 0; i + 2 < corners.Length; i += 3)
        {
            var (a, b, c) = (polygon[corners[i]], polygon[corners[i + 1]], polygon[corners[i + 2]]);
            faces.Triangle(map(a), map(b), map(c), normal, new(a.X, -a.Y), new(b.X, -b.Y), new(c.X, -c.Y));
        }
    }
}
