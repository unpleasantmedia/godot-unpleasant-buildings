using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// An outline's straight skeleton: its sides moved inward all at the same speed, and the paths its
/// corners take until the sides have all met. Each side sweeps out one face, and a hipped roof is
/// those faces, each rising from its side at the pitch. Works for any simple outline, inner corners
/// included. Built step by step, an event at a time: two corners meeting (a side shrunk away), or
/// an inner corner reaching a side across from it (splitting what is left in two).
/// </summary>
public static class StraightSkeleton
{
    private const float Epsilon = 1e-4f;

    /// <summary>How far corners are moved at most, to keep events apart, in metres.</summary>
    private const float Nudge = 0.002f;

    private sealed class Corner
    {
        public Vector2 Start;
        public float Since;
        public int Left;  // The side ending at it.
        public int Right; // The side starting at it.
        public Corner Prev = null!;
        public Corner Next = null!;
        public bool Gone;
    }

    /// <summary>For each side of a ring turned outward (see <see cref="BuildingMesher.Outward"/>), the face it sweeps out, counter-clockwise from its own start and end.</summary>
    public static List<Vector2[]> Faces(Vector2[] exact)
    {
        var n = exact.Length;
        // Corners moved a fraction of a millimetre (the same each time), so that no two events
        // happen at exactly once: right-angled outlines are full of those (a wing as deep as two
        // of its corners are far apart), and taken one after another they go wrong. The faces are
        // given the exact corners back at the end.
        var ring = new Vector2[n];
        for (var i = 0; i < n; i++)
            ring[i] = exact[i] + new Vector2(Mathf.Sin(i * 12.9898f) * 43758.5453f % 1f, Mathf.Sin(i * 78.233f) * 12543.123f % 1f) * Nudge;
        var origin = new Vector2[n];
        var inward = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var d = (ring[(i + 1) % n] - ring[i]).Normalized();
            origin[i] = ring[i];
            inward[i] = new Vector2(-d.Y, d.X);
        }

        var traces = Enumerable.Range(0, n).Select(_ => new List<(Vector2, Vector2)>()).ToList();
        var corners = new List<Corner>();
        for (var i = 0; i < n; i++)
            corners.Add(new Corner { Start = ring[i], Since = 0f, Left = (i - 1 + n) % n, Right = i });
        for (var i = 0; i < n; i++)
        {
            corners[i].Next = corners[(i + 1) % n];
            corners[i].Prev = corners[(i - 1 + n) % n];
        }

        // Where a corner is at a time: on both its sides' lines, moved in that far.
        Vector2 At(Corner c, float t)
        {
            var nl = inward[c.Left];
            var nr = inward[c.Right];
            var det = nl.X * nr.Y - nl.Y * nr.X;
            if (Mathf.Abs(det) < 1e-6f)
                return c.Start + nl * (t - c.Since);
            var bl = t + nl.Dot(origin[c.Left]);
            var br = t + nr.Dot(origin[c.Right]);
            return new Vector2((bl * nr.Y - br * nl.Y) / det, (nl.X * br - nr.X * bl) / det);
        }

        // Where three sides' lines, moved in, meet, and when.
        (Vector2 P, float T)? Meet(int a, int b, int c)
        {
            var na = inward[a];
            var nb = inward[b];
            var nc = inward[c];
            float Det(Vector3 r0, Vector3 r1, Vector3 r2) => r0.Dot(r1.Cross(r2));
            var ra = new Vector3(na.X, na.Y, -1f);
            var rb = new Vector3(nb.X, nb.Y, -1f);
            var rc = new Vector3(nc.X, nc.Y, -1f);
            var d = Det(ra, rb, rc);
            if (Mathf.Abs(d) < 1e-6f)
                return null;
            var k = new Vector3(na.Dot(origin[a]), nb.Dot(origin[b]), nc.Dot(origin[c]));
            // Cramer's rule, by columns.
            var c0 = new Vector3(ra.X, rb.X, rc.X);
            var c1 = new Vector3(ra.Y, rb.Y, rc.Y);
            var c2 = new Vector3(ra.Z, rb.Z, rc.Z);
            float D3(Vector3 x, Vector3 y, Vector3 z) => x.Dot(y.Cross(z));
            var x = D3(k, c1, c2) / D3(c0, c1, c2);
            var y = D3(c0, k, c2) / D3(c0, c1, c2);
            var t = D3(c0, c1, k) / D3(c0, c1, c2);
            return (new Vector2(x, y), t);
        }

        bool Reflex(Corner c)
        {
            var dl = new Vector2(inward[c.Left].Y, -inward[c.Left].X);
            var dr = new Vector2(inward[c.Right].Y, -inward[c.Right].X);
            return dl.X * dr.Y - dl.Y * dr.X < -1e-6f;
        }

        void Trace(Corner c, Vector2 to)
        {
            if (c.Start.DistanceTo(to) < Epsilon)
                return;
            traces[c.Left].Add((c.Start, to));
            traces[c.Right].Add((c.Start, to));
        }

        var now = 0f;
        for (var guard = 0; guard < 10 * n * n + 100; guard++)
        {
            var live = corners.Where(c => !c.Gone).ToList();
            if (live.Count == 0)
                break;

            // A loop of two (or fewer) left: its corners meet where they are.
            var small = live.FirstOrDefault(c => c.Next.Next == c || c.Next == c);
            if (small is not null)
            {
                var other = small.Next;
                var p = (At(small, now) + At(other, now)) / 2f;
                Trace(small, p);
                Trace(other, p);
                small.Gone = other.Gone = true;
                continue;
            }

            float best = float.PositiveInfinity;
            Vector2 where = default;
            Corner? edgeA = null;
            Corner? split = null;
            Corner? splitSide = null;

            foreach (var c in live)
            {
                // Two corners meeting: the side between them shrunk away.
                if (Meet(c.Left, c.Right, c.Next.Right) is { } e && e.T >= now - Epsilon && e.T < best)
                {
                    best = e.T;
                    where = e.P;
                    edgeA = c;
                    split = null;
                }

                if (!Reflex(c))
                    continue;
                // An inner corner reaching a side across from it.
                for (var x = c.Next.Next; x != c.Prev; x = x.Next)
                {
                    var side = x.Right;
                    if (side == c.Left || side == c.Right)
                        continue;
                    if (Meet(c.Left, c.Right, side) is not { } s || s.T < now - Epsilon || s.T >= best)
                        continue;
                    // On that side as it is then, between its corners, and ahead of the inner corner.
                    var a = At(x, s.T);
                    var b = At(x.Next, s.T);
                    var along = b - a;
                    var f = along.LengthSquared() < 1e-10f ? -1f : (s.P - a).Dot(along) / along.LengthSquared();
                    if (f < -Epsilon || f > 1f + Epsilon)
                        continue;
                    best = s.T;
                    where = s.P;
                    split = c;
                    splitSide = x;
                    edgeA = null;
                }
            }

            if (float.IsPositiveInfinity(best))
                break;
            now = Mathf.Max(now, best);

            if (edgeA is { } u)
            {
                var w = u.Next;
                // The last three meeting at once: the top.
                if (w.Next == u.Prev)
                {
                    Trace(u, where);
                    Trace(w, where);
                    Trace(w.Next, where);
                    u.Gone = w.Gone = w.Next.Gone = true;
                    continue;
                }

                Trace(u, where);
                Trace(w, where);
                u.Gone = w.Gone = true;
                var z = new Corner { Start = where, Since = now, Left = u.Left, Right = w.Right, Prev = u.Prev, Next = w.Next };
                u.Prev.Next = z;
                w.Next.Prev = z;
                corners.Add(z);
            }
            else if (split is { } v && splitSide is { } x)
            {
                var y = x.Next;
                Trace(v, where);
                v.Gone = true;
                var z1 = new Corner { Start = where, Since = now, Left = v.Left, Right = x.Right, Prev = v.Prev, Next = y };
                var z2 = new Corner { Start = where, Since = now, Left = x.Right, Right = v.Right, Prev = x, Next = v.Next };
                v.Prev.Next = z1;
                y.Prev = z1;
                x.Next = z2;
                v.Next.Prev = z2;
                corners.Add(z1);
                corners.Add(z2);
            }
        }

        // Each side's face: from its start along it to its end, then back along its traces.
        var faces = new List<Vector2[]>();
        for (var i = 0; i < n; i++)
        {
            // (Found by the nudged corners; the exact ones put in after.)
            var face = new List<Vector2> { ring[i], ring[(i + 1) % n] };
            var left = traces[i].ToList();
            var at = ring[(i + 1) % n];
            for (var step = 0; step < left.Count + 2 && at.DistanceTo(ring[i]) > Epsilon * 10; step++)
            {
                var k = left.FindIndex(s => s.Item1.DistanceTo(at) < 1e-3f || s.Item2.DistanceTo(at) < 1e-3f);
                if (k < 0)
                    break;
                var (p, q) = left[k];
                left.RemoveAt(k);
                at = p.DistanceTo(at) < 1e-3f ? q : p;
                if (at.DistanceTo(ring[i]) > Epsilon * 10)
                    face.Add(at);
            }

            face[0] = exact[i];
            face[1] = exact[(i + 1) % n];
            faces.Add([.. face]);
        }

        return faces;
    }
}
