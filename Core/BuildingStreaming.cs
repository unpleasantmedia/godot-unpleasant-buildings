using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// In the game, a baked building (its look drawn by its cluster's bake) built only when something
/// is near it that needs it solid (see BuildingsLinks.Anchors): its collision, its doors' leaves (to
/// swing) and its wall details' bodies. Far from everything again, all that is let go of.
/// </summary>
public partial class Building
{
    /// <summary>Whether it is built in the game though baked (something near it needs it solid).</summary>
    private bool _streamedIn;

    /// <summary>Whether it is built now, as far as its baked cluster is concerned (not baked: always).</summary>
    public bool StreamedIn => !_bakedAway || _streamedIn;

    /// <summary>Whether it waits to be streamed in rather than built (baked, nothing near it).</summary>
    private bool Waits => _bakedAway && !_streamedIn && !Engine.IsEditorHint();

    /// <summary>Built, as something needs it solid nearby.</summary>
    public void StreamIn()
    {
        if (_streamedIn)
            return;
        _streamedIn = true;
        Build();
    }

    /// <summary>Let go of (nothing near needs it solid): its collision, doors and details, its look the bake's alone.</summary>
    public void StreamOut()
    {
        if (!_streamedIn)
            return;
        _streamedIn = false;
        LetGo();
    }

    /// <summary>All it built let go of: its mesh, its collision, its doors' leaves and its details.</summary>
    private void LetGo()
    {
        Mesh = null;
        _triangles = null;
        if (_instance.IsValid)
            RenderingServer.InstanceSetBase(_instance, default);
        Free(ref _body, PhysicsServer3D.FreeRid);
        _shape = null;
        HangDoors([]);
        foreach (var (_, shown) in _shown)
            if (IsInstanceValid(shown.Node))
                shown.Node.QueueFree();
        _shown.Clear();
    }

    /// <summary>Where it is in the world, and how far out from there it reaches, for its distance from things.</summary>
    public (Vector3 Middle, float Reach) Extent()
    {
        if (_outline.Length == 0)
            return (GlobalPosition, 0f);
        var middle2 = _outline.Aggregate(Vector2.Zero, (s, c) => s + c) / _outline.Length;
        // Out to the furthest corner of any of its parts.
        var reach = AllParts().SelectMany(p => p.Outline).Max(c => c.DistanceTo(middle2));
        return (ToGlobal(new Vector3(middle2.X, 0f, middle2.Y)), reach);
    }
}

public partial class BakedBuildingsNode
{
    /// <summary>How long between looks at what is near what (in seconds of real time).</summary>
    private const double LookEvery = 0.25;

    /// <summary>How much of a frame streaming buildings in may take, in milliseconds (one at least).</summary>
    private const double FrameBudget = 4.0;

    private double _untilLook;
    private bool _first = true;

    /// <summary>The cluster's buildings to stream in, nearest first (and how near), as found at the last look.</summary>
    private readonly List<(Building Building, float Distance)> _due = [];

    /// <summary>How near something a building is streamed in at once, whatever the frame's budget (somebody standing right by it).</summary>
    private const float AtOnce = 15f;

    private List<Building>? _buildings;

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint() || !Visible || GetParent() is not BuildingCluster cluster)
            return;
        _buildings ??= cluster.Buildings.ToList();
        if ((_untilLook -= delta) <= 0)
        {
            _untilLook = LookEvery;
            Look(cluster);
        }

        // Streamed in nearest first, as many as fit in the frame (everything at once the first time:
        // nobody stands on a floor that isn't there yet).
        var started = Time.GetTicksUsec();
        while (_due.Count > 0 && (_first || _due[0].Distance < AtOnce || (Time.GetTicksUsec() - started) / 1000.0 < FrameBudget))
        {
            var (next, _) = _due[0];
            _due.RemoveAt(0);
            if (IsInstanceValid(next))
                next.StreamIn();
        }

        _first = false;
    }

    /// <summary>Which buildings are near something needing them solid (streamed in, nearest first), and which are far from all (let go of).</summary>
    private void Look(BuildingCluster cluster)
    {
        var anchors = BuildingsLinks.Anchors?.Invoke(this)?.ToList() ?? [];
        if (anchors.Count == 0 && GetViewport()?.GetCamera3D() is { } camera)
            anchors.Add(camera.GlobalPosition);
        _due.Clear();
        var near = new List<(Building Building, float Distance)>();
        foreach (var building in _buildings!.Where(IsInstanceValid))
        {
            var (middle, reach) = building.Extent();
            var distance = anchors.Count == 0 ? float.PositiveInfinity
                : anchors.Min(a => new Vector2(a.X - middle.X, a.Z - middle.Z).Length()) - reach;
            if (distance <= cluster.CollisionRange)
            {
                if (!building.StreamedIn)
                    near.Add((building, distance));
            }
            // Further than that and a margin (no flickering in and out at its edge): let go of.
            else if (distance > cluster.CollisionRange + cluster.CollisionMargin)
                building.StreamOut();
        }

        _due.AddRange(near.OrderBy(n => n.Distance));
    }
}
