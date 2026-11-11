using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A building of any shape: its <see cref="Outline"/>, drawn on the ground in the 3D view, walled up
/// <see cref="Storeys"/> storeys of <see cref="StoreyHeight"/> each. Its ground floor is level, just
/// above the highest ground under it, and a plinth reaches down to the ground all round, however
/// the ground slopes. What it builds is drawn (and, in the game, made solid) straight through the
/// servers: nothing is added to the scene, and nothing but its settings is saved.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/unpleasant_buildings/Icons/Building.svg")]
public partial class Building : Node3D
{
    private Vector2[] _outline = [];
    private Material? _wallMaterial;
    private Material? _foundationMaterial;
    private int _storeys = 2;
    private float _storeyHeight = 3f;
    private float _plinthHeight = 0.15f;

    /// <summary>
    /// Its corners in order round it, in its own space, on the ground (X and Z, as a Vector2's X and
    /// Y). Either way round. Drawn and edited in the 3D view; with fewer than three, nothing is built.
    /// </summary>
    [Export]
    public Vector2[] Outline
    {
        get => _outline;
        set
        {
            _outline = value ?? [];
            QueueBuild();
            UpdateGizmos();
            UpdateConfigurationWarnings();
        }
    }

    [Export]
    public Material? WallMaterial
    {
        get => _wallMaterial;
        set
        {
            _wallMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What its foundation (the plinth below its walls, down to the ground) is made of; with none, its walls' material.</summary>
    [Export]
    public Material? FoundationMaterial
    {
        get => _foundationMaterial;
        set
        {
            _foundationMaterial = value;
            QueueBuild();
        }
    }

    [Export(PropertyHint.Range, "1,40,1")]
    public int Storeys
    {
        get => _storeys;
        set
        {
            _storeys = Mathf.Max(1, value);
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>How tall each storey is, floor to floor, in metres.</summary>
    [Export(PropertyHint.Range, "2,8,0.05,suffix:m")]
    public float StoreyHeight
    {
        get => _storeyHeight;
        set
        {
            _storeyHeight = Mathf.Max(1f, value);
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>How far its ground floor is above the highest ground under it, in metres.</summary>
    [Export(PropertyHint.Range, "0,2,0.05,suffix:m")]
    public float PlinthHeight
    {
        get => _plinthHeight;
        set
        {
            _plinthHeight = Mathf.Max(0f, value);
            QueueBuild();
            UpdateGizmos();
        }
    }

    private bool _enterable;

    /// <summary>Whether it can be gone into: with an inside (inner walls, a floor and a ceiling). Without, its doors and windows are closed off at the back.</summary>
    [Export]
    public bool Enterable
    {
        get => _enterable;
        set
        {
            _enterable = value;
            NotifyPropertyListChanged();
            QueueBuild();
        }
    }

    private Godot.Collections.Array<BuildingOpening> _openings = [];
    private Material? _frameMaterial;
    private Material? _glassMaterial;
    private Material? _doorMaterial;

    /// <summary>Its doors and windows: placed, moved and sized in the 3D view (with Window and Door in its toolbar).</summary>
    /// <summary>Moves its origin to the corner of its outline's bounds (lowest X and Z, all of it beyond), on the ground, without it moving.</summary>
    [ExportToolButton("Origin to Corner", Icon = "CenterView")]
    public Callable CentreOriginButton => new(this, MethodName.CentreOrigin);

    /// <summary>
    /// Saves it as a scene of its own (it and everything under it), and puts an instance of that
    /// in its place: for buildings used again and again, edited once in their scene.
    /// </summary>
    [ExportToolButton("Save as Scene", Icon = "PackedScene")]
    public Callable SaveAsSceneButton => new(this, MethodName.SaveAsScene);

    /// <summary>What saves a building as a scene: the editor plugin's (none in the game).</summary>
    internal static System.Action<Building>? SceneSaver { get; set; }

    private void SaveAsScene() => SceneSaver?.Invoke(this);

    /// <summary>Whether it has a foliage buffer of its own (see <see cref="FoliageBuffer"/>), not its cluster's.</summary>
    [ExportGroup("Foliage")]
    [Export]
    public bool OwnFoliageBuffer
    {
        get => _ownFoliageBuffer;
        set
        {
            _ownFoliageBuffer = value;
            FoliageBufferChanged();
        }
    }

    private bool _ownFoliageBuffer;

    /// <summary>How far past its walls no foliage is painted, in metres (with Own Foliage Buffer on; else its cluster's).</summary>
    [Export(PropertyHint.Range, "0,100,0.5,or_greater,suffix:m")]
    public float FoliageBuffer
    {
        get => _foliageBuffer;
        set
        {
            _foliageBuffer = Mathf.Max(0f, value);
            FoliageBufferChanged();
        }
    }

    private float _foliageBuffer = 4f;

    /// <summary>How far past its walls no foliage is painted: its own, or its cluster's (4 m outside one).</summary>
    public float FoliageBufferUsed => _ownFoliageBuffer ? _foliageBuffer : BuildingCluster.Of(GetParent())?.FoliageBuffer ?? 4f;

    /// <summary>Its buffer changed: told on (see BuildingsLinks.Placed), as where it stands is.</summary>
    public void FoliageBufferChanged()
    {
        if (IsInsideTree())
            BuildingsLinks.Placed?.Invoke(this);
    }

    [ExportGroup("Openings")]
    [Export]
    public Godot.Collections.Array<BuildingOpening> Openings
    {
        get => _openings;
        set
        {
            foreach (var o in _openings.Where(o => o is not null))
                if (o.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    o.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            _openings = value ?? [];
            foreach (var o in _openings.Where(o => o is not null))
                if (!o.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    o.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>What window frames, sills and door frames are made of; with none, the plugin's own.</summary>
    [Export]
    public Material? FrameMaterial
    {
        get => _frameMaterial;
        set
        {
            _frameMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What glass is made of; with none, the plugin's own.</summary>
    [Export]
    public Material? GlassMaterial
    {
        get => _glassMaterial;
        set
        {
            _glassMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What door leaves are made of; with none, the plugin's own.</summary>
    [Export]
    public Material? DoorMaterial
    {
        get => _doorMaterial;
        set
        {
            _doorMaterial = value;
            QueueBuild();
        }
    }

    private const string Materials = "res://addons/unpleasant_buildings/Materials";

    /// <summary>A place on a side's outer face, in its own space: u along it from its start, v up.</summary>
    public Vector3 OnSide(int side, float u, float v)
    {
        var a = _outline[side];
        var d = (_outline[(side + 1) % _outline.Length] - a).Normalized();
        var p = a + d * u;
        return new Vector3(p.X, v, p.Y);
    }

    /// <summary>A side's outward direction, in its own space.</summary>
    public Vector3 SideOut(int side)
    {
        var a = _outline[side];
        var b = _outline[(side + 1) % _outline.Length];
        var o = BuildingMesher.Out(a, b);
        return BuildingMesher.SignedArea(_outline) < 0f ? -o : o;
    }

    /// <summary>Where a ray (in its own space) first meets the outside of one of its walls: the side, how far along it, and how high.</summary>
    public (int Side, float U, float V, float Distance)? WallAt(Vector3 from, Vector3 direction)
    {
        (int, float, float, float)? best = null;
        if (_outline.Length < 3)
            return null;
        for (var i = 0; i < _outline.Length; i++)
        {
            var normal = SideOut(i);
            var facing = direction.Dot(normal);
            if (facing >= -1e-4f)
                continue;
            var plane = new Plane(normal, OnSide(i, 0f, 0f));
            if (plane.IntersectsRay(from, direction) is not { } hit)
                continue;
            var a = _outline[i];
            var d = (_outline[(i + 1) % _outline.Length] - a).Normalized();
            var u = (new Vector2(hit.X, hit.Z) - a).Dot(d);
            var distance = hit.DistanceTo(from);
            if (u < 0f || u > SideLength(i) || hit.Y < FloorHeight || hit.Y > TopHeight + 20f)
                continue;
            if (best is null || distance < best.Value.Item4)
                best = (i, u, hit.Y, distance);
        }

        return best;
    }

    /// <summary>A storey's floor, in its own space.</summary>
    public float StoreyFloor(int storey) => FloorHeight + storey * _storeyHeight;

    /// <summary>How long a side of its outline is.</summary>
    public float SideLength(int side) => _outline.Length == 0 ? 0f : _outline[side].DistanceTo(_outline[(side + 1) % _outline.Length]);

    /// <summary>
    /// Where its doors and windows go: each on its side, on its storey (a window on all storeys on
    /// each from its own up), as long as it fits in its side and under the top of the walls.
    /// </summary>
    public List<(BuildingOpening Opening, int Storey)> Placed()
    {
        var placed = new List<(BuildingOpening, int)>();
        foreach (var o in _openings.Where(o => o is not null))
        {
            if (o.Side < 0 || o.Side >= _outline.Length || o.From < BuildingOpening.Margin - 0.001f || o.From + o.Width > SideLength(o.Side) - BuildingOpening.Margin + 0.001f)
                continue;
            var last = o.Kind == OpeningKind.Window && o.AllStoreys ? _storeys - 1 : o.Storey;
            for (var storey = o.Storey; storey <= last && storey < _storeys; storey++)
                if (o.Bottom + o.Height <= _storeyHeight * (_storeys - storey) + 0.001f)
                    placed.Add((o, storey));
        }

        return placed;
    }

    private List<Hole> Holes()
    {
        var reversed = BuildingMesher.SignedArea(_outline) < 0f;
        return Placed().Select(p =>
        {
            var (o, storey) = p;
            var length = SideLength(o.Side);
            var (u0, u1) = reversed ? (length - o.From - o.Width, length - o.From) : (o.From, o.From + o.Width);
            var v0 = StoreyFloor(storey) + o.Bottom;
            return new Hole(BuildingMesher.OutwardSide(_outline, o.Side), u0, u1, v0, v0 + o.Height, o);
        }).ToList();
    }

    private RoofType _roof = RoofType.Gabled;
    private Material? _roofMaterial;
    private float _roofPitch = 35f;
    private float _roofOverhang = 0.3f;
    private float _parapetHeight = 0.5f;
    private int _roofSide = -1;

    [ExportGroup("Roof")]
    [Export]
    public RoofType Roof
    {
        get => _roof;
        set
        {
            _roof = value;
            NotifyPropertyListChanged();
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>What its roof is made of; with none, its walls' material.</summary>
    [Export]
    public Material? RoofMaterial
    {
        get => _roofMaterial;
        set
        {
            _roofMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>How steep a pitched roof's slopes are, in degrees.</summary>
    [Export(PropertyHint.Range, "5,70,1,suffix:°")]
    public float RoofPitch
    {
        get => _roofPitch;
        set
        {
            _roofPitch = Mathf.Clamp(value, 1f, 80f);
            QueueBuild();
        }
    }

    /// <summary>How far a pitched roof reaches out past the walls, in metres.</summary>
    [Export(PropertyHint.Range, "0,2,0.05,suffix:m")]
    public float RoofOverhang
    {
        get => _roofOverhang;
        set
        {
            _roofOverhang = Mathf.Max(0f, value);
            QueueBuild();
        }
    }

    /// <summary>How far a flat roof's walls go up past it, in metres; 0 for none.</summary>
    [Export(PropertyHint.Range, "0,2,0.05,suffix:m")]
    public float ParapetHeight
    {
        get => _parapetHeight;
        set
        {
            _parapetHeight = Mathf.Max(0f, value);
            QueueBuild();
        }
    }

    /// <summary>
    /// For gabled and shed roofs: the side (its number in the outline, from 0: the side from its
    /// corner of that number to the next) a gabled roof's ridge runs along, or a shed roof slopes
    /// down towards. Picked in the 3D view with Roof Side; -1 (or one it no longer has) for its longest side.
    /// </summary>
    [Export]
    public int RoofSide
    {
        get => _roofSide;
        set
        {
            _roofSide = value;
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>Whether its roof goes by a side (gabled and shed roofs do).</summary>
    public bool RoofHasSide => _roof is RoofType.Gabled or RoofType.Shed;

    /// <summary>The side its roof goes by: its own, or its longest.</summary>
    public int UsedRoofSide
    {
        get
        {
            var n = _outline.Length;
            if (_roofSide >= 0 && _roofSide < n)
                return _roofSide;
            var best = 0;
            for (var i = 1; i < n; i++)
                if (_outline[i].DistanceTo(_outline[(i + 1) % n]) > _outline[best].DistanceTo(_outline[(best + 1) % n]))
                    best = i;
            return best;
        }
    }

    /// <summary>Saved before Foundation Material had its name: taken as that.</summary>
    public override bool _Set(StringName property, Variant value)
    {
        if (property != "PlinthMaterial")
            return false;
        FoundationMaterial = value.As<Material?>();
        return true;
    }

    /// <summary>
    /// Its origin moved to the corner of its outline's bounds (its lowest X and Z, so all of it lies
    /// beyond, along +X and +Z), on the ground under its middle (its corners, interior walls and
    /// stairs staying where they are). In the editor, a step to undo.
    /// </summary>
    public void CentreOrigin()
    {
        if (_outline.Length < 3)
            return;
        var middle = _outline.Aggregate(Vector2.Zero, (sum, c) => sum + c) / _outline.Length;
        var corner = new Vector2(_outline.Min(c => c.X), _outline.Min(c => c.Y));
        var to = ToGlobal(new Vector3(corner.X, 0f, corner.Y));
        if (SceneRoot() is { } root && BuildingsLinks.Height?.Invoke(root, ToGlobal(new Vector3(middle.X, 0f, middle.Y))) is { } ground)
            to.Y = ground;
        if (to.DistanceTo(GlobalPosition) < 0.01f)
            return;
        var moved = GlobalTransform with { Origin = to };
        Vector2 Moved(Vector2 c)
        {
            var p = moved.AffineInverse() * ToGlobal(new Vector3(c.X, 0f, c.Y));
            return new Vector2(p.X, p.Z);
        }

        var corners = _outline.Select(Moved).ToArray();
        var walls = _walls.Where(w => w is not null).ToList();
        var stairs = _stairs.Where(s => s is not null).ToList();
        var rooms = _rooms.Where(r => r is not null).ToList();
#if TOOLS
        if (Engine.IsEditorHint())
        {
            var undo = EditorInterface.Singleton.GetEditorUndoRedo();
            undo.CreateAction("Centre building origin", UndoRedo.MergeMode.Disable, this);
            undo.AddDoProperty(this, Node3D.PropertyName.GlobalTransform, moved);
            undo.AddDoProperty(this, PropertyName.Outline, corners);
            undo.AddUndoProperty(this, Node3D.PropertyName.GlobalTransform, GlobalTransform);
            undo.AddUndoProperty(this, PropertyName.Outline, _outline);
            // Interior walls, stairs and rooms are placed in its own space too: kept where they are.
            foreach (var w in walls)
            {
                undo.AddDoProperty(w, InteriorWall.PropertyName.A, Moved(w.A));
                undo.AddDoProperty(w, InteriorWall.PropertyName.B, Moved(w.B));
                undo.AddUndoProperty(w, InteriorWall.PropertyName.A, w.A);
                undo.AddUndoProperty(w, InteriorWall.PropertyName.B, w.B);
            }

            foreach (var s in stairs)
            {
                undo.AddDoProperty(s, Staircase.PropertyName.Foot, Moved(s.Foot));
                undo.AddUndoProperty(s, Staircase.PropertyName.Foot, s.Foot);
            }

            foreach (var r in rooms)
            {
                undo.AddDoProperty(r, Room.PropertyName.Seed, Moved(r.Seed));
                undo.AddUndoProperty(r, Room.PropertyName.Seed, r.Seed);
            }

            undo.CommitAction();
            return;
        }
#endif
        foreach (var w in walls)
            (w.A, w.B) = (Moved(w.A), Moved(w.B));
        foreach (var s in stairs)
            s.Foot = Moved(s.Foot);
        foreach (var r in rooms)
            r.Seed = Moved(r.Seed);
        GlobalTransform = moved;
        Outline = corners;
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        var name = property["name"].AsStringName();
        var hide = (name == PropertyName.RoofPitch || name == PropertyName.RoofOverhang) && _roof == RoofType.Flat
            || name == PropertyName.ParapetHeight && _roof != RoofType.Flat
            || name == PropertyName.RoofSide && !RoofHasSide
            || !_enterable && (name == PropertyName.Walls || name == PropertyName.InteriorWallMaterial || name == PropertyName.FloorMaterial || name == PropertyName.CeilingMaterial
                || name == PropertyName.Stairs || name == PropertyName.StairMaterial || name == PropertyName.StairRailingMaterial || name == PropertyName.Rooms);
        if (hide)
            property["usage"] = (long)(property["usage"].AsInt64() & ~(long)PropertyUsageFlags.Editor);
    }

    /// <summary>How far its plinth goes into the ground below the lowest ground under it, in metres.</summary>
    private const float Buried = 0.5f;

    /// <summary>How far apart the ground is looked at along each side, in metres.</summary>
    private const float GroundStep = 1f;

    /// <summary>Its ground floor's height, in its own space, as last built.</summary>
    public float FloorHeight { get; private set; }

    /// <summary>Its walls' top, in its own space, as last built.</summary>
    public float TopHeight => FloorHeight + _storeys * _storeyHeight;

    /// <summary>What it built last, if anything.</summary>
    public ArrayMesh? Mesh { get; private set; }

    private Rid _instance;
    private Rid _body;
    private Shape3D? _shape;
    private bool _queued;

    public Building()
    {
        SetNotifyTransform(true);
        // Made anew as the scripts reload, already in the tree (so not entering it): back on the list.
        new Callable(this, MethodName.Rejoin).CallDeferred();
    }

    private void Rejoin()
    {
        if (IsInsideTree())
            InTree.Add(this);
    }

    /// <summary>Every building in the tree, for the editor to find them without going through a whole level.</summary>
    public static readonly HashSet<Building> InTree = [];

    public override void _EnterTree() => InTree.Add(this);

    public override void _ExitTree()
    {
        InTree.Remove(this);
        BuildingsLinks.Removed?.Invoke(this);
    }

    /// <summary>Where it stands: its outline's corners in the world, in X and Z.</summary>
    public Vector2[] Footprint() => [.. _outline.Select(c => ToGlobal(new Vector3(c.X, 0f, c.Y))).Select(p => new Vector2(p.X, p.Z))];

    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationEnterWorld:
                _instance = RenderingServer.InstanceCreate();
                RenderingServer.InstanceSetScenario(_instance, GetWorld3D().Scenario);
                Place();
                QueueBuild();
                break;
            case NotificationExitWorld:
                Free(ref _instance, RenderingServer.FreeRid);
                Free(ref _body, PhysicsServer3D.FreeRid);
                break;
            case NotificationTransformChanged:
                Place();
                // Moved: on other ground.
                QueueBuild();
                break;
            case NotificationEditorPreSave:
                Saving(true);
                break;
            case NotificationEditorPostSave:
                Saving(false);
                break;
            case NotificationVisibilityChanged:
                ShowOwnLook();
                break;
        }
    }

    private static void Free(ref Rid rid, System.Action<Rid> free)
    {
        if (rid.IsValid)
            free(rid);
        rid = default;
    }

    private void Place()
    {
        if (_instance.IsValid)
        {
            RenderingServer.InstanceSetTransform(_instance, GlobalTransform);
            RenderingServer.InstanceSetVisible(_instance, IsVisibleInTree());
        }

        if (_body.IsValid)
            PhysicsServer3D.BodySetState(_body, PhysicsServer3D.BodyState.Transform, GlobalTransform);
    }

    public override string[] _GetConfigurationWarnings()
    {
        if (_outline.Length < 3)
            return ["Draw its outline: select it, click Draw in the 3D view's toolbar, and click its corners on the ground."];
        if (!Simple(_outline))
            return ["Its outline crosses itself: move its corners so its sides don't cross."];
        return [];
    }

    /// <summary>Built again at the end of the frame (once, however many changes come before then).</summary>
    public void QueueBuild()
    {
        if (_queued || !IsInsideTree())
            return;
        _queued = true;
        // On the main thread whoever asks (a scene loaded on a thread, say).
        Callable.From(Build).CallDeferred();
    }

    /// <summary>Builds it now, from its settings and the ground under it.</summary>
    public void Build()
    {
        // The level is only looked at from the main thread (its children can't be read from others).
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
        {
            GD.PushWarning($"{Name}: asked to build from another thread; built on the main thread instead.{'\n'}{System.Environment.StackTrace}");
            Callable.From(Build).CallDeferred();
            return;
        }

        _queued = false;
        BuildingsLinks.Placed?.Invoke(this);
        if (!IsInsideTree() || !_instance.IsValid)
            return;
        // Baked, and nothing near needs it solid (in the game): not built till something does.
        if (Waits)
        {
            LetGo();
            return;
        }

        Mesh = null;
        _triangles = null;
        RenderingServer.InstanceSetBase(_instance, default);
        Free(ref _body, PhysicsServer3D.FreeRid);
        if (_outline.Length == 0)
            return;
        // With a corner or two (while being drawn): its floor height all the same, for them to be shown on the ground.
        var (low, high) = GroundRange();
        FloorHeight = high + _plinthHeight;
        if (_outline.Length < 3 || !Simple(_outline))
        {
            PlaceDetails();
            UpdateGizmos();
            return;
        }

        // In the game, an enterable building's doors are hung to swing (not built into it).
        var hung = _enterable && !Engine.IsEditorHint() ? new List<DoorLeaf>() : null;
        Mesh = MakeMesh(low, CutStorey >= 0, hung);
        if (hung is not null)
            HangDoors(hung);
        RenderingServer.InstanceSetBase(_instance, Mesh.GetRid());

        // Solid in the game only: the editor never steps physics, and bodies made there pile up.
        if (!Engine.IsEditorHint())
        {
            _shape = Mesh.CreateTrimeshShape();
            _body = PhysicsServer3D.BodyCreate();
            PhysicsServer3D.BodySetMode(_body, PhysicsServer3D.BodyMode.Static);
            PhysicsServer3D.BodyAddShape(_body, _shape.GetRid());
            PhysicsServer3D.BodySetSpace(_body, GetWorld3D().Space);
            PhysicsServer3D.BodyAttachObjectInstanceId(_body, GetInstanceId());
        }

        Place();
        // Its details onto its walls as they are now.
        PlaceDetails();
        // Baked (in the game): its own look hidden again, built anew.
        ShowOwnLook();
        UpdateGizmos();
    }

    /// <summary>
    /// Its mesh, from its plinth's foot (the lowest ground under it) up: cut open above the storey
    /// being worked on, or whole; its doors' leaves left out of it (into the list), or built in shut.
    /// </summary>
    private ArrayMesh MakeMesh(float low, bool open, List<DoorLeaf>? hung)
    {
        // Its number, for its windows' glass (see WindowSeed): from where it stands, so the same baked or not.
        var at = GlobalPosition;
        BuildingMesher.WindowSeed = (int)(BuildingMesher.StableHash(Mathf.RoundToInt(at.X), Mathf.RoundToInt(at.Z)) % 254u) + 1;
        var roof = new RoofSettings(_roof, BuildingMesher.OutwardSide(_outline, UsedRoofSide), _roofPitch, _roofOverhang, _parapetHeight, _roofMaterial);
        var openings = new OpeningMaterials(
            _frameMaterial ?? GD.Load<Material>($"{Materials}/Frame.tres"),
            _glassMaterial ?? GD.Load<Material>($"{Materials}/Glass.tres"),
            _doorMaterial ?? GD.Load<Material>($"{Materials}/Door.tres"),
            GD.Load<Material>($"{Materials}/Backing.tres"));
        // Cut open above the storey being worked on (in the editor): built up to it, no roof.
        var top = open ? StoreyFloor(ShownStoreys) : TopHeight;
        var holes = open ? Holes().Where(h => h.V0 < top).ToList() : Holes();
        var rooms = _enterable && _rooms.Count > 0 ? Rooms_() : null;
        var mesh = BuildingMesher.Shell(_outline, low - Buried, FloorHeight, top, _wallMaterial, _foundationMaterial ?? _wallMaterial, roof, holes, openings, _enterable, _interiorWallMaterial ?? _wallMaterial, open, rooms, hung);
        BuildingMesher.Ledges(mesh, BuildingMesher.Outward(_outline), open ? Ledges().Where(l => l.V < top).ToList() : Ledges(), UsedProjectionMaterials());
        BuildInterior(mesh, rooms, hung, open);
        return mesh;
    }

    /// <summary>The lowest and highest ground under its outline, in its own space; its own height (0) without ground.</summary>
    private (float Low, float High) GroundRange()
    {
        var heights = new List<float>();
        var root = SceneRoot();
        if (BuildingsLinks.Height is { } height && root is not null)
        {
            for (var i = 0; i < _outline.Length; i++)
            {
                var a = _outline[i];
                var b = _outline[(i + 1) % _outline.Length];
                var steps = Mathf.Max(1, Mathf.CeilToInt(a.DistanceTo(b) / GroundStep));
                for (var s = 0; s < steps; s++)
                {
                    var p = a.Lerp(b, s / (float)steps);
                    var world = ToGlobal(new Vector3(p.X, 0f, p.Y));
                    if (height(root, world) is { } h && !float.IsNaN(h))
                        heights.Add(ToLocal(new Vector3(world.X, h, world.Z)).Y);
                }
            }

            // Its porches' decks too: round their edges, from the wall out to their fronts.
            foreach (var porch in _projections.Where(p => p is not null && p.Kind == ProjectionKind.Porch && p.Side >= 0 && p.Side < _outline.Length))
            {
                var start = OnSide(porch.Side, porch.From, 0f);
                var along = OnSide(porch.Side, porch.From + 1f, 0f) - start;
                var out_ = SideOut(porch.Side).Normalized();
                var across = Mathf.Max(1, Mathf.CeilToInt(porch.Width / GroundStep));
                var deep = Mathf.Max(1, Mathf.CeilToInt(porch.Depth / GroundStep));
                for (var i = 0; i <= across; i++)
                {
                    for (var j = 0; j <= deep; j++)
                    {
                        if (i != 0 && i != across && j != 0 && j != deep)
                            continue;
                        var local = start + along * (porch.Width * i / across) + out_ * (porch.Depth * j / deep);
                        var world = ToGlobal(new Vector3(local.X, 0f, local.Z));
                        if (height(root, world) is { } h && !float.IsNaN(h))
                            heights.Add(ToLocal(new Vector3(world.X, h, world.Z)).Y);
                    }
                }
            }
        }

        return heights.Count == 0 ? (0f, 0f) : (heights.Min(), heights.Max());
    }

    /// <summary>The ground's height under a place in its own space (in its own space), if there is ground.</summary>
    public float? GroundUnder(Vector3 local)
    {
        if (BuildingsLinks.Height is not { } height || SceneRoot() is not { } root)
            return null;
        var world = ToGlobal(new Vector3(local.X, 0f, local.Z));
        return height(root, world) is { } h && !float.IsNaN(h) ? ToLocal(new Vector3(world.X, h, world.Z)).Y : null;
    }

    /// <summary>The root of the scene it is in: the one edited, or the one played.</summary>
    public Node? SceneRoot() =>
        Engine.IsEditorHint() ? GetTree()?.EditedSceneRoot : GetTree()?.CurrentScene ?? Owner;

    /// <summary>Whether an outline's sides only meet at its corners, one after the next.</summary>
    public static bool Simple(Vector2[] outline)
    {
        var n = outline.Length;
        if (n < 3)
            return false;
        for (var i = 0; i < n; i++)
        {
            if (outline[i].DistanceTo(outline[(i + 1) % n]) < 0.01f)
                return false;
            for (var j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                    continue;
                if (Geometry2D.SegmentIntersectsSegment(outline[i], outline[(i + 1) % n], outline[j], outline[(j + 1) % n]).VariantType != Variant.Type.Nil)
                    return false;
            }
        }

        return true;
    }
}
