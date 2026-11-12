using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

public partial class Building
{
    private Godot.Collections.Array<WallDetail> _details = [];
    private WallDetailLibrary? _detailLibrary;

    /// <summary>Each detail's scene, as put in (internal children: not saved, and not in the Scene dock), with the scene it was made from.</summary>
    private readonly Dictionary<WallDetail, (Node3D Node, PackedScene Scene)> _shown = [];

    /// <summary>The metadata its details' scenes carry, to know them by.</summary>
    private const string DetailMeta = "unpleasant_wall_detail";

    /// <summary>Its wall details: put on, moved and sized in the 3D view (from the Wall Details dock).</summary>
    [ExportGroup("Details")]
    [Export]
    public Godot.Collections.Array<WallDetail> Details
    {
        get => _details;
        set
        {
            foreach (var d in _details.Where(d => d is not null))
                if (d.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.PlaceDetails)))
                    d.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.PlaceDetails));
            _details = value ?? [];
            foreach (var d in _details.Where(d => d is not null))
                if (!d.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.PlaceDetails)))
                    d.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.PlaceDetails));
            PlaceDetails();
            UpdateGizmos();
        }
    }

    /// <summary>The library its details are picked from; with none, the project's (Project Settings &gt; Unpleasant Buildings &gt; Detail Library).</summary>
    [Export]
    public WallDetailLibrary? DetailLibrary
    {
        get => _detailLibrary;
        set => _detailLibrary = value;
    }

    /// <summary>The library its details are picked from: its own, or the project's.</summary>
    public WallDetailLibrary? UsedDetailLibrary => _detailLibrary ?? WallDetailLibrary.ProjectLibrary();

    /// <summary>Where a detail sits, in the building's own space: on its side, its +Z out of the wall and its X along it.</summary>
    public Transform3D DetailTransform(WallDetail d)
    {
        var outward = SideOut(d.Side).Normalized();
        var across = Vector3.Up.Cross(outward).Normalized();
        // Back by its type's offset: its scene made for a wall in front of its origin.
        var back = outward * (d.Type?.WallOffset ?? 0f);
        return new Transform3D(new Basis(across, Vector3.Up, outward), OnSide(d.Side, d.Along, StoreyFloor(d.Storey) + d.Up) - back);
    }

    /// <summary>Which way a detail's X runs along its side: +1 the side's way (from its start), -1 back.</summary>
    public float DetailAcross(WallDetail d)
    {
        var a = OnSide(d.Side, 0f, 0f);
        var along = OnSide(d.Side, 1f, 0f) - a;
        return DetailTransform(d).Basis.X.Dot(along) >= 0f ? 1f : -1f;
    }

    /// <summary>Whether a detail is on a side and storey it has.</summary>
    public bool Fits(WallDetail d) => d.Type?.Scene is not null && _outline.Length >= 3 && Simple(_outline)
        && WallSide(d.Side) is { Outline.Length: >= 3 } w && d.Storey >= w.Floor && d.Storey < w.Top && Simple(w.Outline);

    /// <summary>A detail's scene as put in, if it is.</summary>
    public Node3D? DetailNode(WallDetail d) => _shown.TryGetValue(d, out var shown) ? shown.Node : null;

    /// <summary>
    /// A detail's size, in its own space: what its models take up (with none, half a metre about
    /// its origin).
    /// </summary>
    public Aabb DetailBounds(WallDetail d)
    {
        if (DetailNode(d) is not { } node)
            return new Aabb(new Vector3(-0.25f, -0.25f, 0f), new Vector3(0.5f, 0.5f, 0.2f));
        Aabb? box = null;
        var inverse = node.GlobalTransform.AffineInverse();
        foreach (var visual in node.FindChildren("*", nameof(VisualInstance3D), true, false).OfType<VisualInstance3D>().Prepend(node as VisualInstance3D).OfType<VisualInstance3D>())
        {
            var part = inverse * visual.GlobalTransform * visual.GetAabb();
            box = box is { } b ? b.Merge(part) : part;
        }

        return box ?? new Aabb(new Vector3(-0.25f, -0.25f, 0f), new Vector3(0.5f, 0.5f, 0.2f));
    }

    /// <summary>
    /// Its details' scenes put in, taken out and moved to match its list: a scene is only made when
    /// a detail is added (or given another type), so moving one is cheap.
    /// </summary>
    public void PlaceDetails()
    {
        if (!IsInsideTree())
            return;
        // Put in before the scripts reloaded (which forgets which they were): taken out, to be put in again.
        foreach (var stale in GetChildren(true).OfType<Node3D>().Where(n => n.HasMeta(DetailMeta) && !_shown.Values.Any(s => s.Node == n)))
            stale.QueueFree();
        foreach (var (d, (node, scene)) in _shown.ToList())
        {
            if (_details.Contains(d) && Fits(d) && d.Type!.Scene == scene)
                continue;
            if (IsInstanceValid(node))
                node.QueueFree();
            _shown.Remove(d);
        }

        foreach (var d in _details.Where(d => d is not null && Fits(d)))
        {
            if (!_shown.TryGetValue(d, out var shown))
            {
                var node = d.Type!.Scene!.Instantiate<Node3D>();
                // Out of physics in the editor: it never steps it, and bodies made there pile up.
                if (Engine.IsEditorHint())
                    foreach (var body in node.FindChildren("*", nameof(CollisionObject3D), true, false).OfType<CollisionObject3D>())
                        body.ProcessMode = ProcessModeEnum.Disabled;
                node.SetMeta(DetailMeta, true);
                AddChild(node, false, InternalMode.Back);
                OwnInternal(node);
                shown = (node, d.Type.Scene!);
                _shown[d] = shown;
            }

            Size(shown.Node, d.Type!.WidthProperty, d.Width);
            Size(shown.Node, d.Type.HeightProperty, d.Height);
            shown.Node.Transform = DetailTransform(d);
            // Above the storey being worked on (in the editor): out of the way.
            shown.Node.Visible = CutStorey < 0 || d.Storey <= CutStorey;
        }
    }

    /// <summary>Sets a detail's size by the property of its root its type names, in whole metres if that takes whole numbers.</summary>
    private static void Size(Node3D node, string property, float metres)
    {
        if (property.Length == 0)
            return;
        var now = node.Get(property);
        if (now.VariantType == Variant.Type.Int)
            node.Set(property, Mathf.Max(1, Mathf.RoundToInt(metres)));
        else if (now.VariantType == Variant.Type.Float)
            node.Set(property, metres);
    }
}
