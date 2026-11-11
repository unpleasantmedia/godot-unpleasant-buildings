using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A balcony or awning as built: on a side of the outline turned outward (see
/// <see cref="BuildingMesher.Outward"/>), from U0 to U1 along it, at a height V (in the building's
/// own space: a balcony's slab top, an awning's top at the wall).
/// </summary>
/// <param name="Ground">The lowest ground under the building: what a porch's skirt and steps go down to.</param>
/// <param name="Reversed">Whether its side runs the other way round the ring than in the outline (for where a porch's steps are).</param>
/// <param name="StepsGround">For a porch's steps: the highest ground in front of its deck where they go, which they come down to (their sides still reaching down to <paramref name="Ground"/>).</param>
public readonly record struct Ledge(int Side, float U0, float U1, float V, BuildingProjection Projection, float Ground, bool Reversed, float StepsGround);

/// <summary>The materials of a building's balconies, awnings and porches.</summary>
public readonly record struct ProjectionMaterials(Material? Slab, Material? Railing, Material? Glass, Material? Awning, Material? Deck, Material? Post, Material? PorchRoof);

public static partial class BuildingMesher
{
    /// <summary>How thick a balcony's slab is, in metres.</summary>
    public const float SlabThickness = 0.2f;

    /// <summary>How deep a sloped awning's flap at its front is, in metres.</summary>
    public const float Valance = 0.25f;

    /// <summary>How thick a flat canopy is, in metres.</summary>
    public const float CanopyThickness = 0.15f;

    private const float Rail = 0.05f;
    private const float Bar = 0.02f;
    private const float BarSpacing = 0.12f;
    private const float SolidRailing = 0.1f;
    private const float Post = 0.06f;

    /// <summary>How thick a porch's roof is, in metres.</summary>
    public const float PorchRoofThickness = 0.12f;

    private const float PorchPost = 0.12f;
    private const float PorchPostSpacing = 2.5f;
    private const float PorchEaves = 0.2f;
    private const float StepRise = 0.18f;
    private const float StepTread = 0.28f;
    private const float RailPole = 0.08f;
    private const float MinRailing = 0.5f;

    /// <summary>How much higher a porch's shed roof is at the wall than over its front posts (a 15° slope), in metres.</summary>
    public static float PorchRise(BuildingProjection p) =>
        p.PorchRoof == PorchRoofStyle.Shed ? p.Depth * Mathf.Tan(Mathf.DegToRad(15f)) : 0f;

    /// <summary>Balconies and awnings: a surface for each material.</summary>
    public static void Ledges(ArrayMesh mesh, Vector2[] ring, List<Ledge> ledges, ProjectionMaterials materials)
    {
        var slabs = new Faces();
        var railings = new Faces();
        var glass = new Faces();
        var awnings = new Faces();
        var decks = new Faces();
        var posts = new Faces();
        var porchRoofs = new Faces();
        foreach (var l in ledges)
        {
            var f = FrameOf(ring, l.Side);
            var p = l.Projection;
            var (u0, u1, v, d) = (l.U0, l.U1, l.V, p.Depth);
            if (p.Kind == ProjectionKind.Porch)
            {
                Porch(decks, posts, porchRoofs, railings, glass, f, l);
                continue;
            }

            if (p.Kind == ProjectionKind.Balcony)
            {
                slabs.Box(f, u0, u1, v - SlabThickness, v, 0f, d);
                Railing(p.Railing == RailingStyle.Solid ? slabs : railings, glass, f, p, u0, u1, v, d);
                continue;
            }

            if (p.AwningStyle == AwningStyle.Flat)
            {
                awnings.Box(f, u0, u1, v - CanopyThickness, v, 0f, d);
                continue;
            }

            // The canvas, from the wall down to its front edge, seen from both sides; its flap; its cheeks.
            var low = v - p.Drop;
            var top = new[] { f.At(u0, v, 0f), f.At(u1, v, 0f), f.At(u1, low, d), f.At(u0, low, d) };
            var normal = (top[1] - top[0]).Cross(top[3] - top[0]).Normalized();
            if (normal.Y < 0f)
                normal = -normal;
            var slope = Mathf.Sqrt(d * d + p.Drop * p.Drop);
            Vector2[] uv = [new(u0, 0f), new(u1, 0f), new(u1, slope), new(u0, slope)];
            awnings.Quad(top[0], top[1], top[2], top[3], normal, uv[0], uv[1], uv[2], uv[3]);
            awnings.Quad(top[0], top[1], top[2], top[3], -normal, uv[0], uv[1], uv[2], uv[3]);
            awnings.Pane(f, u0, u1, low - Valance, low, d);
            if (p.Cheeks)
            {
                foreach (var u in new[] { u0, u1 })
                {
                    var (a, b, c) = (f.At(u, v, 0f), f.At(u, low, 0f), f.At(u, low, d));
                    awnings.Triangle(a, b, c, f.Along, new(0f, -v), new(0f, -low), new(d, -low));
                    awnings.Triangle(a, b, c, -f.Along, new(0f, -v), new(0f, -low), new(d, -low));
                }
            }
        }

        slabs.AddTo(mesh, materials.Slab);
        railings.AddTo(mesh, materials.Railing);
        glass.AddTo(mesh, materials.Glass);
        awnings.AddTo(mesh, materials.Awning);
        decks.AddTo(mesh, materials.Deck);
        posts.AddTo(mesh, materials.Post);
        porchRoofs.AddTo(mesh, materials.PorchRoof);
    }

    /// <summary>
    /// A porch: its deck (down to the ground if skirted), steps down from its front, posts along its
    /// front up to a roof over it, and a railing round its open sides with a gap at the steps.
    /// </summary>
    private static void Porch(Faces decks, Faces posts, Faces roofs, Faces railings, Faces glass, WallFrame f, Ledge l)
    {
        var p = l.Projection;
        var (u0, u1, v, d) = (l.U0, l.U1, l.V, p.Depth);
        var ground = Mathf.Min(l.Ground, v - SlabThickness);
        decks.Box(f, u0, u1, p.Skirt ? ground : v - SlabThickness, v, 0f, d);

        // The steps: each one lower and further out, down to the ground.
        (float From, float To)? steps = null;
        if (p.StepsSpan is { } span)
        {
            steps = l.Reversed ? (u1 - span.To, u1 - span.From) : (u0 + span.From, u0 + span.To);
            // Down to the ground where they are (not the building's lowest), so they aren't buried on a slope.
            var foot = Mathf.Clamp(l.StepsGround, ground, v - StepRise);
            var count = Mathf.Max(1, Mathf.CeilToInt((v - foot) / StepRise - 0.01f));
            var rise = (v - foot) / count;
            for (var k = 1; k <= count; k++)
                decks.Box(f, steps.Value.From, steps.Value.To, ground, v - k * rise, d + (k - 1) * StepTread, d + k * StepTread);
        }

        // Posts at the front corners and (under a roof) between them, none in front of the steps: up
        // to the roof, or without one just above the railing (none without either).
        var roofFront = v + p.RoofHeight;
        if (p.HasRoof || p.HasRailing)
        {
            var top = p.HasRoof ? roofFront : v + p.RailingHeight + 0.05f;
            var bays = p.HasRoof ? Mathf.Max(1, Mathf.CeilToInt((u1 - u0 - PorchPost) / PorchPostSpacing)) : 1;
            for (var k = 0; k <= bays; k++)
            {
                var u = Mathf.Lerp(u0, u1 - PorchPost, k / (float)bays);
                if (steps is { } s && u + PorchPost > s.From + 0.001f && u < s.To - 0.001f)
                    continue;
                posts.Box(f, u, u + PorchPost, v, top, d - PorchPost, d);
            }
        }

        if (p.HasRoof)
        {
            // The roof: a slab from the wall out past the posts, sloping down (a shed) or flat.
            var (r0, r1, w1) = (u0 - PorchEaves, u1 + PorchEaves, d + PorchEaves);
            var t = PorchRoofThickness;
            var fall = PorchRise(p) / d;
            Vector3 At(float u, float w, float above) => f.At(u, roofFront + fall * (d - w) + above, w);
            var c = new[] { At(r0, 0f, 0f), At(r1, 0f, 0f), At(r1, w1, 0f), At(r0, w1, 0f), At(r0, 0f, t), At(r1, 0f, t), At(r1, w1, t), At(r0, w1, t) };
            var up = (c[5] - c[4]).Cross(c[7] - c[4]).Normalized();
            if (up.Y < 0f)
                up = -up;
            Vector2[] plan = [new(r0, 0f), new(r1, 0f), new(r1, w1), new(r0, w1)];
            roofs.Quad(c[4], c[5], c[6], c[7], up, plan[0], plan[1], plan[2], plan[3]);
            roofs.Quad(c[0], c[1], c[2], c[3], -up, plan[0], plan[1], plan[2], plan[3]);
            roofs.Quad(c[3], c[2], c[6], c[7], f.Out, new(r0, 0f), new(r1, 0f), new(r1, t), new(r0, t));
            roofs.Quad(c[0], c[3], c[7], c[4], -f.Along, new(0f, 0f), new(w1, 0f), new(w1, t), new(0f, t));
            roofs.Quad(c[1], c[2], c[6], c[5], f.Along, new(0f, 0f), new(w1, 0f), new(w1, t), new(0f, t));
        }

        if (!p.HasRailing)
            return;
        var solid = p.Railing == RailingStyle.Solid ? decks : railings;
        var h = p.RailingHeight;
        // The railing: along the front either side of the steps (a pole where it stops at them), and out
        // along both sides (a pole at the wall); a run too short to be worth it left out.
        var front = new List<(float, float)>();
        if (steps is { } gap)
        {
            front.Add((u0 + PorchPost, gap.From));
            front.Add((gap.To, u1 - PorchPost));
        }
        else
        {
            front.Add((u0 + PorchPost, u1 - PorchPost));
        }

        var line = d - PorchPost / 2f;
        foreach (var (a, b) in front.Where(r => r.Item2 - r.Item1 >= MinRailing))
        {
            RailRun(solid, glass, p.Railing, v, h, f.At(a, 0f, line), f.At(b, 0f, line));
            if (steps is { } at && Mathf.Abs(b - at.From) < 0.001f)
                solid.Box(f, b - RailPole, b, v, v + h, line - RailPole / 2f, line + RailPole / 2f);
            if (steps is { } at2 && Mathf.Abs(a - at2.To) < 0.001f)
                solid.Box(f, a, a + RailPole, v, v + h, line - RailPole / 2f, line + RailPole / 2f);
        }

        if (d - PorchPost < MinRailing)
            return;
        foreach (var u in new[] { u0 + PorchPost / 2f, u1 - PorchPost / 2f })
        {
            RailRun(solid, glass, p.Railing, v, h, f.At(u, 0f, 0f), f.At(u, 0f, d - PorchPost));
            solid.Box(f, u - RailPole / 2f, u + RailPole / 2f, v, v + h, 0f, RailPole);
        }
    }

    /// <summary>
    /// A straight run of railing on a deck at height v, between two points (their heights ignored):
    /// rails and bars between them, a low wall, or a glass pane under a rail.
    /// </summary>
    private static void RailRun(Faces railings, Faces glass, RailingStyle style, float v, float h, Vector3 start, Vector3 end)
    {
        start.Y = end.Y = 0f;
        var length = start.DistanceTo(end);
        if (length < 0.02f)
            return;
        var axis = (end - start) / length;
        var across = axis.Cross(Vector3.Up).Normalized();

        // A box from one point to another, so thick across and along, between two heights.
        void Beam(Vector3 from, Vector3 to, float thick, float low, float high)
        {
            var side = across * (thick / 2f);
            Vector3 P(Vector3 x, float y, float s) => new Vector3(x.X, y, x.Z) + side * s;
            var c = new[] { P(from, low, -1f), P(to, low, -1f), P(to, low, 1f), P(from, low, 1f), P(from, high, -1f), P(to, high, -1f), P(to, high, 1f), P(from, high, 1f) };
            var l = from.DistanceTo(to);
            railings.Quad(c[4], c[5], c[6], c[7], Vector3.Up, new(0f, 0f), new(l, 0f), new(l, thick), new(0f, thick));
            railings.Quad(c[0], c[1], c[2], c[3], Vector3.Down, new(0f, 0f), new(l, 0f), new(l, thick), new(0f, thick));
            railings.Quad(c[0], c[1], c[5], c[4], -across, new(0f, -low), new(l, -low), new(l, -high), new(0f, -high));
            railings.Quad(c[3], c[2], c[6], c[7], across, new(0f, -low), new(l, -low), new(l, -high), new(0f, -high));
            railings.Quad(c[0], c[3], c[7], c[4], -axis, new(0f, -low), new(thick, -low), new(thick, -high), new(0f, -high));
            railings.Quad(c[1], c[2], c[6], c[5], axis, new(0f, -low), new(thick, -low), new(thick, -high), new(0f, -high));
        }

        switch (style)
        {
            case RailingStyle.Solid:
                Beam(start, end, SolidRailing, v, v + h);
                return;
            case RailingStyle.Glass:
            {
                Beam(start, end, Rail, v + h - Rail, v + h);
                var (low, high) = (v + 0.05f, v + h - Rail);
                Vector3[] q = [new(start.X, low, start.Z), new(end.X, low, end.Z), new(end.X, high, end.Z), new(start.X, high, start.Z)];
                glass.Quad(q[0], q[1], q[2], q[3], across, new(0f, 0f), new(length, 0f), new(length, h), new(0f, h));
                glass.Quad(q[0], q[1], q[2], q[3], -across, new(0f, 0f), new(length, 0f), new(length, h), new(0f, h));
                return;
            }
            default:
            {
                var bottom = v + 0.08f;
                Beam(start, end, Rail, bottom, bottom + Rail);
                Beam(start, end, Rail, v + h - Rail, v + h);
                var count = Mathf.Max(1, Mathf.RoundToInt(length / BarSpacing));
                for (var k = 1; k < count; k++)
                {
                    var x = start + axis * (length * k / count);
                    Beam(x - axis * (Bar / 2f), x + axis * (Bar / 2f), Bar, bottom + Rail, v + h - Rail);
                }

                return;
            }
        }
    }

    /// <summary>A balcony's railing round its three open sides, on its slab (a solid one of the slab's material, the others of the railings').</summary>
    private static void Railing(Faces railings, Faces glass, WallFrame f, BuildingProjection p, float u0, float u1, float v, float d)
    {
        var h = p.RailingHeight;
        switch (p.Railing)
        {
            case RailingStyle.Solid:
                railings.Box(f, u0, u1, v, v + h, d - SolidRailing, d);
                railings.Box(f, u0, u0 + SolidRailing, v, v + h, 0f, d - SolidRailing);
                railings.Box(f, u1 - SolidRailing, u1, v, v + h, 0f, d - SolidRailing);
                return;
            case RailingStyle.Glass:
                // Posts at the front corners, a rail along the top, glass under it.
                railings.Box(f, u0, u0 + Post, v, v + h, d - Post, d);
                railings.Box(f, u1 - Post, u1, v, v + h, d - Post, d);
                railings.Box(f, u0, u1, v + h - Rail, v + h, d - Rail, d);
                railings.Box(f, u0, u0 + Rail, v + h - Rail, v + h, 0f, d - Rail);
                railings.Box(f, u1 - Rail, u1, v + h - Rail, v + h, 0f, d - Rail);
                glass.Pane(f, u0 + Post, u1 - Post, v + 0.05f, v + h - Rail, d - Post / 2f);
                foreach (var u in new[] { u0 + Rail / 2f, u1 - Rail / 2f })
                {
                    var (a, b, c, e) = (f.At(u, v + 0.05f, 0f), f.At(u, v + 0.05f, d - Post), f.At(u, v + h - Rail, d - Post), f.At(u, v + h - Rail, 0f));
                    glass.Quad(a, b, c, e, f.Along, new(0f, 0f), new(d, 0f), new(d, -h), new(0f, -h));
                    glass.Quad(a, b, c, e, -f.Along, new(0f, 0f), new(d, 0f), new(d, -h), new(0f, -h));
                }

                return;
            default:
            {
                // Top and bottom rails, front and sides; bars between them.
                var bottom = v + 0.08f;
                foreach (var (low, high) in new[] { (bottom, bottom + Rail), (v + h - Rail, v + h) })
                {
                    railings.Box(f, u0, u1, low, high, d - Rail, d);
                    railings.Box(f, u0, u0 + Rail, low, high, 0f, d - Rail);
                    railings.Box(f, u1 - Rail, u1, low, high, 0f, d - Rail);
                }

                railings.Box(f, u0, u0 + Rail, v, v + h, d - Rail, d);
                railings.Box(f, u1 - Rail, u1, v, v + h, d - Rail, d);
                var (b0, b1) = (bottom + Rail, v + h - Rail);
                var across = Mathf.Max(1, Mathf.RoundToInt((u1 - u0) / BarSpacing));
                for (var k = 1; k < across; k++)
                {
                    var u = Mathf.Lerp(u0, u1, k / (float)across);
                    railings.Box(f, u - Bar / 2f, u + Bar / 2f, b0, b1, d - Rail / 2f - Bar / 2f, d - Rail / 2f + Bar / 2f);
                }

                var out_ = Mathf.Max(1, Mathf.RoundToInt(d / BarSpacing));
                for (var k = 1; k < out_; k++)
                {
                    var w = Mathf.Lerp(0f, d, k / (float)out_);
                    foreach (var u in new[] { u0 + Rail / 2f, u1 - Rail / 2f })
                        railings.Box(f, u - Bar / 2f, u + Bar / 2f, b0, b1, w - Bar / 2f, w + Bar / 2f);
                }

                return;
            }
        }
    }
}

public partial class Building
{
    private Godot.Collections.Array<BuildingProjection> _projections = [];
    private Material? _slabMaterial;
    private Material? _railingMaterial;
    private Material? _awningMaterial;
    private Material? _deckMaterial;
    private Material? _postMaterial;
    private Material? _porchRoofMaterial;

    /// <summary>Its balconies and awnings: put on, moved and sized in the 3D view (from the Wall Details dock).</summary>
    [ExportGroup("Balconies & Awnings")]
    [Export]
    public Godot.Collections.Array<BuildingProjection> Projections
    {
        get => _projections;
        set
        {
            foreach (var p in _projections.Where(p => p is not null))
                if (p.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    p.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            _projections = value ?? [];
            foreach (var p in _projections.Where(p => p is not null))
                if (!p.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    p.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>What balcony slabs are made of; with none, the plugin's own.</summary>
    [Export]
    public Material? SlabMaterial
    {
        get => _slabMaterial;
        set
        {
            _slabMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What balcony railings are made of; with none, the plugin's own.</summary>
    [Export]
    public Material? RailingMaterial
    {
        get => _railingMaterial;
        set
        {
            _railingMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What awnings are made of; with none, the plugin's own (striped).</summary>
    [Export]
    public Material? AwningMaterial
    {
        get => _awningMaterial;
        set
        {
            _awningMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What porch decks, skirts and steps are made of; with none, the plugin's own (wood).</summary>
    [Export]
    public Material? DeckMaterial
    {
        get => _deckMaterial;
        set
        {
            _deckMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What porch posts are made of; with none, the plugin's own (painted wood).</summary>
    [Export]
    public Material? PostMaterial
    {
        get => _postMaterial;
        set
        {
            _postMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What porch roofs are made of; with none, the building's roof's.</summary>
    [Export]
    public Material? PorchRoofMaterial
    {
        get => _porchRoofMaterial;
        set
        {
            _porchRoofMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>Where its balconies and awnings go: each on its side, on its storey (a balcony on all storeys on each from its own up), as long as it fits in its side.</summary>
    public List<(BuildingProjection Projection, int Storey)> PlacedProjections()
    {
        var placed = new List<(BuildingProjection, int)>();
        foreach (var p in _projections.Where(p => p is not null))
        {
            if (p.Side < 0 || p.Side >= _outline.Length || p.From < -0.001f || p.From + p.Width > SideLength(p.Side) + 0.001f)
                continue;
            if (p.Kind == ProjectionKind.Porch && p.Storey != 0)
                continue;
            var last = p.Kind == ProjectionKind.Balcony && p.AllStoreys ? _storeys - 1 : p.Storey;
            for (var storey = p.Storey; storey <= last && storey < _storeys; storey++)
                placed.Add((p, storey));
        }

        return placed;
    }

    private List<Ledge> Ledges()
    {
        var reversed = BuildingMesher.SignedArea(_outline) < 0f;
        var ground = GroundRange().Low;
        return PlacedProjections().Select(x =>
        {
            var (p, storey) = x;
            var length = SideLength(p.Side);
            var (u0, u1) = reversed ? (length - p.From - p.Width, length - p.From) : (p.From, p.From + p.Width);
            return new Ledge(BuildingMesher.OutwardSide(_outline, p.Side), u0, u1, StoreyFloor(storey) + p.Up, p, ground, reversed, StepsGround(p, ground));
        }).ToList();
    }

    /// <summary>For a porch with steps: the highest ground in front of its deck where they go (sampled out to 1.5 m); the lowest ground without.</summary>
    private float StepsGround(BuildingProjection p, float lowest)
    {
        if (p.Kind != ProjectionKind.Porch || p.StepsSpan is not { } span)
            return lowest;
        var start = OnSide(p.Side, p.From, 0f);
        var along = OnSide(p.Side, p.From + 1f, 0f) - start;
        var out_ = SideOut(p.Side).Normalized();
        var highest = float.NegativeInfinity;
        for (var i = 0; i <= 4; i++)
        {
            for (var j = 0; j <= 3; j++)
            {
                var place = start + along * Mathf.Lerp(span.From, span.To, i / 4f) + out_ * (p.Depth + 0.1f + 1.4f * j / 3f);
                if (GroundUnder(place) is { } h)
                    highest = Mathf.Max(highest, h);
            }
        }

        return float.IsNegativeInfinity(highest) ? lowest : highest;
    }

    private ProjectionMaterials UsedProjectionMaterials() => new(
        _slabMaterial ?? GD.Load<Material>($"{Materials}/Slab.tres"),
        _railingMaterial ?? GD.Load<Material>($"{Materials}/Railing.tres"),
        _glassMaterial ?? GD.Load<Material>($"{Materials}/Glass.tres"),
        _awningMaterial ?? GD.Load<Material>($"{Materials}/Awning.tres"),
        _deckMaterial ?? GD.Load<Material>($"{Materials}/PorchDeck.tres"),
        _postMaterial ?? GD.Load<Material>($"{Materials}/PorchPost.tres"),
        _porchRoofMaterial ?? _roofMaterial ?? _wallMaterial);
}
