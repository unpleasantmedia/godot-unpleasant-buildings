#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Wall details: the Wall Details dock (shown while a building is selected) lists its library's
/// types by category; one picked goes where a wall is clicked, its origin there (in 10 cm steps),
/// as many times as clicked. Clicking one selects it, for its handles (its middle, moved along and
/// up its wall or onto another; its width and height, for types that can be sized) and the strip
/// in the toolbar (its distances from both ends of its wall and from its floor, and buttons to
/// centre it); Ctrl-clicking one takes it away.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    private VBoxContainer? _detailDock;
    private VBoxContainer? _detailSections;
    private Label? _detailHint;
    private ButtonGroup? _detailButtons;
    private readonly List<WallDetailType> _detailTypes = [];
    private bool _detailDockShown;

    private HBoxContainer? _detailPanel;
    private SpinBox? _detailFromStart;
    private SpinBox? _detailFromEnd;
    private SpinBox? _detailUp;
    private SpinBox? _detailStorey;
    private SpinBox? _detailWidth;
    private SpinBox? _detailHeight;
    private Button? _detailCentreUp;

    /// <summary>The type picked in the dock to put on walls, if any.</summary>
    public WallDetailType? PlacingDetail { get; private set; }

    /// <summary>Where a picked detail would go now, if anywhere: a new one, not yet on the building.</summary>
    public WallDetail? DetailGhost { get; private set; }

    /// <summary>The selected wall detail, if any.</summary>
    public WallDetail? SelectedDetail { get; private set; }

    private void AddDetailTools()
    {
        if (!ProjectSettings.HasSetting(WallDetailLibrary.Setting))
        {
            ProjectSettings.SetSetting(WallDetailLibrary.Setting, "");
            ProjectSettings.SetInitialValue(WallDetailLibrary.Setting, "");
        }

        ProjectSettings.AddPropertyInfo(new Godot.Collections.Dictionary
        {
            ["name"] = WallDetailLibrary.Setting,
            ["type"] = (int)Variant.Type.String,
            ["hint"] = (int)PropertyHint.File,
            ["hint_string"] = "*.tres,*.res",
        });

        _detailDock = new VBoxContainer { Name = "Wall Details" };
        _detailHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _detailDock.AddChild(_detailHint);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _detailSections = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_detailSections);
        _detailDock.AddChild(scroll);
        // In a dock of its own, added once and then opened and closed (added and taken away again on
        // every selection, the editor laid out its docks anew each time, and closed its bottom panel).
        _detailHolder = new EditorDock { Title = "Wall Details", DefaultSlot = EditorDock.DockSlot.RightBl, Transient = true, Global = false };
        _detailHolder.AddChild(_detailDock);
        AddDock(_detailHolder);
        _detailHolder.Close();
        _detailButtons = new ButtonGroup { AllowUnpress = true };

        _detailPanel = new HBoxContainer { Visible = false };
        _toolbar!.AddChild(_detailPanel);
        _detailPanel.AddChild(new VSeparator());
        SpinBox Box(string label, float min, float max, float step, StringName method)
        {
            _detailPanel.AddChild(new Label { Text = label });
            var box = new SpinBox { MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(70, 0), SelectAllOnFocus = true, AllowLesser = true };
            box.Connect(Godot.Range.SignalName.ValueChanged, new Callable(this, method));
            _detailPanel.AddChild(box);
            return box;
        }

        _detailFromStart = Box("From start", -1000, 1000, OpeningStep, MethodName.DetailFromStartChanged);
        _detailFromEnd = Box("From end", -1000, 1000, OpeningStep, MethodName.DetailFromEndChanged);
        _detailUp = Box("Up", -100, 100, OpeningStep, MethodName.DetailUpChanged);
        _detailStorey = Box("Storey", 0, 40, 1, MethodName.DetailStoreyChanged);
        _detailWidth = Box("Width", 0.1f, 50, OpeningStep, MethodName.DetailWidthChanged);
        _detailHeight = Box("Height", 0.1f, 100, OpeningStep, MethodName.DetailHeightChanged);
        var across = new Button { Text = "Centre Across", FocusMode = Control.FocusModeEnum.None, TooltipText = "Puts it in the middle of its wall, along it." };
        across.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.DetailCentreAcross));
        _detailPanel.AddChild(across);
        _detailCentreUp = new Button { Text = "Centre Up", FocusMode = Control.FocusModeEnum.None, TooltipText = "Puts it in the middle of its storey, up it." };
        _detailCentreUp.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.DetailCentreUp));
        _detailPanel.AddChild(_detailCentreUp);
    }

    private void RemoveDetailTools()
    {
        ShowDetailDock(false);
        if (_detailHolder is not null && IsInstanceValid(_detailHolder))
        {
            RemoveDock(_detailHolder);
            _detailHolder.QueueFree();
        }
    }

    /// <summary>The dock the detail palette is in (see AddDetailTools).</summary>
    private EditorDock? _detailHolder;

    /// <summary>The dock shown while a building is selected, and filled from its library.</summary>
    private void ShowDetailDock(bool show)
    {
        if (_detailDock is null || show == _detailDockShown)
        {
            if (show)
                FillDetailDock();
            return;
        }

        _detailDockShown = show;
        if (show)
        {
            _detailHolder?.MakeVisible();
            FillDetailDock();
        }
        else
        {
            _detailHolder?.Close();
            StopPlacingDetail();
        }
    }

    private void FillDetailDock()
    {
        if (_detailSections is null || _detailHint is null)
            return;
        foreach (var child in _detailSections.GetChildren())
            child.QueueFree();
        _detailTypes.Clear();
        var library = _building?.UsedDetailLibrary;
        _detailHint.Text = library is null
            ? "No detail library: set one under Project Settings > Unpleasant Buildings > Detail Library, or give the building its own."
            : "Pick a detail, then click a wall to put it there. Right-click or Escape stops.";
        // Doors and windows first: picked here as with Window and Door in the toolbar.
        _detailSections.AddChild(new Label { Text = "Doors & Windows", ThemeTypeVariation = "HeaderSmall" });
        var openings = new HFlowContainer();
        _detailSections.AddChild(openings);
        foreach (var (index, text, icon) in new[] { (WindowButton, "Window", "Window"), (DoorButton, "Door", "Door") })
        {
            var button = new DetailButton
            {
                Index = index,
                Text = text,
                ToggleMode = true,
                ButtonGroup = _detailButtons,
                IconAlignment = HorizontalAlignment.Center,
                VerticalIconAlignment = VerticalAlignment.Top,
                // As the details' buttons: the icon fitted to the button.
                ExpandIcon = true,
                CustomMinimumSize = new Vector2(96, 96),
                FocusMode = Control.FocusModeEnum.None,
            };
            var path = $"res://addons/unpleasant_buildings/Icons/{icon}.svg";
            if (ResourceLoader.Exists(path))
                button.Icon = ResourceLoader.Load<Texture2D>(path);
            openings.AddChild(button);
            button.SetPressedNoSignal(index == WindowButton ? _window?.ButtonPressed == true : _door?.ButtonPressed == true);
        }

        // Balconies and awnings: made by the building.
        _detailSections.AddChild(new Label { Text = "Balconies, Awnings & Porches", ThemeTypeVariation = "HeaderSmall" });
        var ledges = new HFlowContainer();
        _detailSections.AddChild(ledges);
        foreach (var (index, text, icon) in new[] { (BalconyButton, "Balcony", "Balcony"), (AwningButton, "Awning", "Awning"), (PorchButton, "Porch", "Porch") })
        {
            var button = new DetailButton
            {
                Index = index,
                Text = text,
                ToggleMode = true,
                ButtonGroup = _detailButtons,
                IconAlignment = HorizontalAlignment.Center,
                VerticalIconAlignment = VerticalAlignment.Top,
                ExpandIcon = true,
                CustomMinimumSize = new Vector2(96, 96),
                FocusMode = Control.FocusModeEnum.None,
            };
            var path = $"res://addons/unpleasant_buildings/Icons/{icon}.svg";
            if (ResourceLoader.Exists(path))
                button.Icon = ResourceLoader.Load<Texture2D>(path);
            ledges.AddChild(button);
            button.SetPressedNoSignal(PlacingProjection == KindOf(index));
        }

        if (library is null)
            return;
        foreach (var category in library.Types.Where(t => t?.Scene is not null).GroupBy(t => t.Category.Length > 0 ? t.Category : "Other"))
        {
            _detailSections.AddChild(new Label { Text = category.Key, ThemeTypeVariation = "HeaderSmall" });
            var flow = new HFlowContainer();
            _detailSections.AddChild(flow);
            foreach (var type in category)
            {
                var button = new DetailButton
                {
                    Index = _detailTypes.Count,
                    Text = type.Shown,
                    ToggleMode = true,
                    ButtonGroup = _detailButtons,
                    IconAlignment = HorizontalAlignment.Center,
                    VerticalIconAlignment = VerticalAlignment.Top,
                    ExpandIcon = true,
                    CustomMinimumSize = new Vector2(96, 96),
                    FocusMode = Control.FocusModeEnum.None,
                };
                _detailTypes.Add(type);
                flow.AddChild(button);
                // The one in hand, still pressed (without picking it again).
                button.SetPressedNoSignal(type == PlacingDetail);
                EditorInterface.Singleton.GetResourcePreviewer().QueueResourcePreview(type.Scene!.ResourcePath, this, MethodName.DetailPreviewed, button);
            }
        }
    }

    // A scene's picture, or (with none: the editor only has pictures of scenes it saved) its biggest mesh's.
    private void DetailPreviewed(string path, Texture2D? preview, Texture2D? thumbnail, Variant button)
    {
        if (button.AsGodotObject() is not DetailButton b || !IsInstanceValid(b))
            return;
        if (preview is not null)
            b.Icon = preview;
        else if (b.Index < _detailTypes.Count && BiggestMesh(_detailTypes[b.Index]) is { } mesh)
            EditorInterface.Singleton.GetResourcePreviewer().QueueEditedResourcePreview(mesh, this, MethodName.MeshPreviewed, b);
    }

    private void MeshPreviewed(string path, Texture2D? preview, Texture2D? thumbnail, Variant button)
    {
        if (button.AsGodotObject() is DetailButton b && IsInstanceValid(b) && preview is not null)
            b.Icon = preview;
    }

    /// <summary>The biggest of a detail's meshes (by the size of its box), for its thumbnail.</summary>
    private static Mesh? BiggestMesh(WallDetailType type)
    {
        if (type.Scene?.Instantiate() is not Node node)
            return null;
        var mesh = node.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
            .Select(m => m.Mesh).OfType<Mesh>().OrderByDescending(m => m.GetAabb().Size.LengthSquared()).FirstOrDefault();
        node.Free();
        return mesh;
    }

    /// <summary>A detail's button in the dock toggled: that detail picked to put on walls, or (let go of) none.</summary>
    /// <summary>The dock's Window and Door buttons, by the numbers they hold in place of a detail's.</summary>
    private const int WindowButton = -1;
    private const int DoorButton = -2;

    public void DetailToggled(DetailButton button, bool on)
    {
        if (button.Index is BalconyButton or AwningButton or PorchButton)
        {
            ProjectionToggled(button.Index, on);
            return;
        }

        if (button.Index < 0)
        {
            // As the toolbar's: picking there too (which lets go of any detail).
            var tool = button.Index == WindowButton ? _window : _door;
            if (tool is not null && tool.ButtonPressed != on)
                tool.ButtonPressed = on;
            return;
        }

        var type = button.Index < _detailTypes.Count ? _detailTypes[button.Index] : null;
        if (!on)
        {
            // Let go of by another being picked: that one is in hand now.
            if (type == PlacingDetail)
                StopPlacingDetail();
            return;
        }

        PlacingDetail = type;
        StopWalls();
        StopStairs();
        StopRooms();
        StopInner();
        SetDrawing(false);
        StopPlacing();
        StopPlacingProjection();
        _side?.SetPressedNoSignal(false);
    }

    private void StopPlacingDetail()
    {
        PlacingDetail = null;
        DetailGhost = null;
        if (_detailButtons?.GetPressedButton() is { } pressed)
            pressed.SetPressedNoSignal(false);
        _building?.UpdateGizmos();
    }

    /// <summary>Selects a wall detail (or none), and shows it in the strip.</summary>
    public void SelectDetail(WallDetail? detail)
    {
        if (SelectedDetail is { } was && IsInstanceValid(was) && was.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.FillDetail)))
            was.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.FillDetail));
        SelectedDetail = detail;
        if (detail is not null)
        {
            detail.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.FillDetail));
            SelectedWall = null;
            SelectedStairs = null;
            SelectedRoom = null;
            SelectedInner = null;
            Select(null);
            SelectProjection(null);
        }

        FillDetail();
        QueueInspect();
        _building?.UpdateGizmos();
    }

    /// <summary>A detail's left and right edges along its side (from its start), and its bottom and top up its storey.</summary>
    public static (float U0, float U1, float V0, float V1) Edges(Building building, WallDetail d)
    {
        var bounds = building.DetailBounds(d);
        var s = building.DetailAcross(d);
        var (a, b) = (d.Along + s * bounds.Position.X, d.Along + s * bounds.End.X);
        return (Mathf.Min(a, b), Mathf.Max(a, b), d.Up + bounds.Position.Y, d.Up + bounds.End.Y);
    }

    private void FillDetail()
    {
        if (_detailPanel is null)
            return;
        var d = SelectedDetail;
        // Its settings are in the Inspector.
        _detailPanel.Visible = false;
        if (d is null || _building is null || !_building.Fits(d))
            return;
        _filling = true;
        var (u0, u1, v0, _) = Edges(_building, d);
        _detailFromStart!.Value = u0;
        _detailFromEnd!.Value = _building.SideLength(d.Side) - u1;
        _detailUp!.Value = v0;
        // Locked to its height: not moved up or down here.
        var locked = d.Type?.HeightLocked == true;
        _detailUp.Editable = !locked;
        _detailCentreUp!.Disabled = locked;
        _detailStorey!.Value = d.Storey;
        _detailStorey.MaxValue = Mathf.Max(0, _building.AllStoreys - 1);
        _detailWidth!.Value = d.Width;
        _detailHeight!.Value = d.Height;
        // Details aren't sized here.
        const bool wide = false;
        const bool tall = false;
        _detailWidth.Visible = wide;
        _detailWidth.GetParent().GetChild(_detailWidth.GetIndex() - 1).Set(CanvasItem.PropertyName.Visible, wide);
        _detailHeight.Visible = tall;
        _detailHeight.GetParent().GetChild(_detailHeight.GetIndex() - 1).Set(CanvasItem.PropertyName.Visible, tall);
        _filling = false;
    }

    /// <summary>Changes the selected detail's settings, as a step to undo.</summary>
    private void ChangeDetail(string action, params (StringName Property, Variant Value)[] changes)
    {
        if (_filling || SelectedDetail is not { } d)
            return;
        var undo = GetUndoRedo();
        undo.CreateAction(action, UndoRedo.MergeMode.Ends, _building);
        foreach (var (property, value) in changes)
        {
            undo.AddDoProperty(d, property, value);
            undo.AddUndoProperty(d, property, d.Get(property));
        }

        undo.CommitAction();
    }

    private void DetailFromStartChanged(double value)
    {
        if (SelectedDetail is { } d && _building is not null)
            ChangeDetail("Move detail", (WallDetail.PropertyName.Along, d.Along + (float)value - Edges(_building, d).U0));
    }

    private void DetailFromEndChanged(double value)
    {
        if (SelectedDetail is { } d && _building is not null)
            ChangeDetail("Move detail", (WallDetail.PropertyName.Along, d.Along + (_building.SideLength(d.Side) - (float)value) - Edges(_building, d).U1));
    }

    private void DetailUpChanged(double value)
    {
        if (SelectedDetail is { } d && _building is not null)
            ChangeDetail("Move detail", (WallDetail.PropertyName.Up, d.Up + (float)value - Edges(_building, d).V0));
    }

    private void DetailStoreyChanged(double value) => ChangeDetail("Move detail", (WallDetail.PropertyName.Storey, (int)value));

    private void DetailWidthChanged(double value) => ChangeDetail("Resize detail", (WallDetail.PropertyName.Width, (float)value));

    private void DetailHeightChanged(double value) => ChangeDetail("Resize detail", (WallDetail.PropertyName.Height, (float)value));

    private void DetailCentreAcross()
    {
        if (SelectedDetail is not { } d || _building is null)
            return;
        var (u0, u1, _, _) = Edges(_building, d);
        ChangeDetail("Centre detail", (WallDetail.PropertyName.Along, d.Along + (_building.SideLength(d.Side) - (u1 - u0)) / 2f - u0));
    }

    private void DetailCentreUp()
    {
        if (SelectedDetail is not { } d || _building is null || d.Type?.HeightLocked == true)
            return;
        var (_, _, v0, v1) = Edges(_building, d);
        ChangeDetail("Centre detail", (WallDetail.PropertyName.Up, d.Up + (_building.StoreyHeight - (v1 - v0)) / 2f - v0));
    }

    /// <summary>Each type's size, in its scene's own space, worked out once (for the outline of one being put on a wall).</summary>
    private static readonly Dictionary<WallDetailType, Aabb> TypeSizes = [];

    /// <summary>What a type's models take up, in its scene's own space (with none, half a metre about its origin).</summary>
    public static Aabb TypeSize(WallDetailType type)
    {
        if (TypeSizes.TryGetValue(type, out var known))
            return known;
        Aabb? box = null;
        if (type.Scene?.Instantiate() is Node3D root)
        {
            // Out of the tree: each part's place found by going up to the root.
            foreach (var visual in root.FindChildren("*", nameof(VisualInstance3D), true, false).OfType<VisualInstance3D>())
            {
                var t = Transform3D.Identity;
                for (Node? n = visual; n is not null && n != root; n = n.GetParent())
                    if (n is Node3D n3)
                        t = n3.Transform * t;
                var part = t * visual.GetAabb();
                box = box is { } bx ? bx.Merge(part) : part;
            }

            root.Free();
        }

        var size = box ?? new Aabb(new Vector3(-0.25f, -0.25f, 0f), new Vector3(0.5f, 0.5f, 0.2f));
        TypeSizes[type] = size;
        return size;
    }

    /// <summary>
    /// The wall detail under a place in the view, if any: as doors and windows are found, where the
    /// wall under the mouse is within its outline; else the nearest whose box the mouse is over (for
    /// what stands out from the wall, a hanging sign).
    /// </summary>
    private WallDetail? DetailAt(Building building, Camera3D camera, Vector2 screen)
    {
        var (localFrom, localDirection) = LocalRay(building, camera, screen);
        if (building.WallAt(localFrom, localDirection) is { } wall)
        {
            foreach (var d in building.Details.Where(d => d is not null && building.Fits(d) && d.Side == wall.Side))
            {
                var (u0, u1, v0, v1) = Edges(building, d);
                var floor = building.StoreyFloor(d.Storey);
                if (wall.U >= u0 && wall.U <= u1 && wall.V >= floor + v0 && wall.V <= floor + v1)
                    return d;
            }
        }

        var from = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        WallDetail? best = null;
        var nearest = float.PositiveInfinity;
        foreach (var d in building.Details.Where(d => d is not null && building.Fits(d)))
        {
            if (building.DetailNode(d) is not { } node)
                continue;
            var inverse = node.GlobalTransform.AffineInverse();
            var bounds = building.DetailBounds(d).Grow(0.02f);
            if (Hit(bounds, inverse * from, inverse.Basis * direction) is { } hit)
            {
                var distance = (node.GlobalTransform * hit - from).Length();
                if (distance < nearest)
                {
                    nearest = distance;
                    best = d;
                }
            }
        }

        return best;
    }

    /// <summary>Where a ray first meets a box, if it does (slab by slab).</summary>
    private static Vector3? Hit(Aabb box, Vector3 from, Vector3 direction)
    {
        var (near, far) = (0f, float.PositiveInfinity);
        for (var axis = 0; axis < 3; axis++)
        {
            var (o, d, lo, hi) = (from[axis], direction[axis], box.Position[axis], box.End[axis]);
            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi)
                    return null;
                continue;
            }

            var (t0, t1) = ((lo - o) / d, (hi - o) / d);
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            near = Mathf.Max(near, t0);
            far = Mathf.Min(far, t1);
            if (near > far)
                return null;
        }

        return from + direction * near;
    }

    /// <summary>A new detail of the picked type at the wall under the mouse, its origin there (in 10 cm steps, from the side's start and the storey's floor).</summary>
    private static WallDetail? DetailGhostAt(Building building, WallDetailType type, Camera3D camera, Vector2 screen)
    {
        var (from, direction) = LocalRay(building, camera, screen);
        if (building.WallAt(from, direction) is not { } hit)
            return null;
        var storey = building.StoreyOn(hit.Side, hit.V);
        var detail = new WallDetail { Type = type, Side = hit.Side, Storey = storey, Width = 1f, Height = building.StoreyHeight };
        // Its middle under the mouse (its origin may be anywhere in it: a drainpipe's is at its bottom).
        var middle = TypeSize(type).GetCenter();
        detail.Along = Step(hit.U - middle.X * building.DetailAcross(detail));
        detail.Up = type.HeightLocked ? type.Up : Step(hit.V - building.StoreyFloor(storey) - middle.Y);
        return detail;
    }

    /// <summary>The view's clicks and moves for wall details; whether it used them.</summary>
    private bool DetailInput(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        if (PlacingDetail is { } type)
        {
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    DetailGhost = DetailGhostAt(building, type, camera, motion.Position);
                    building.UpdateGizmos();
                    return false;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                    if (DetailGhostAt(building, type, camera, click.Position) is { } detail)
                    {
                        var undo = GetUndoRedo();
                        undo.CreateAction("Add wall detail", customContext: building);
                        undo.AddDoProperty(building, Building.PropertyName.Details, new Godot.Collections.Array<WallDetail>(building.Details.Append(detail).ToArray()));
                        undo.AddUndoProperty(building, Building.PropertyName.Details, building.Details);
                        undo.CommitAction();
                        SelectDetail(detail);
                    }

                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    StopPlacingDetail();
                    return true;
            }

            return false;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
        {
            // On the selected one's handles: the editor's, to drag.
            if (!press.CtrlPressed && NearDetailHandle(building, camera, press.Position))
                return false;
            if (DetailAt(building, camera, press.Position) is { } at)
            {
                if (press.CtrlPressed)
                {
                    RemoveDetail(building, at);
                    return true;
                }

                SelectDetail(at);
                return true;
            }

            if (SelectedDetail is not null)
                SelectDetail(null);
        }

        return false;
    }

    /// <summary>Takes a detail off a building, as a step to undo.</summary>
    private void RemoveDetail(Building building, WallDetail detail)
    {
        var undo = GetUndoRedo();
        undo.CreateAction("Remove wall detail", customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Details, new Godot.Collections.Array<WallDetail>(building.Details.Where(d => d != detail).ToArray()));
        undo.AddUndoProperty(building, Building.PropertyName.Details, building.Details);
        undo.CommitAction();
        if (SelectedDetail == detail)
            SelectDetail(null);
    }

    /// <summary>The selected detail's handles, in the building's own space: its middle, then its width's and its height's (if it can be sized), with their ids.</summary>
    public static List<(int Id, Vector3 At)> DetailHandles(Building building, WallDetail d)
    {
        var t = building.DetailTransform(d);
        var bounds = building.DetailBounds(d);
        var front = bounds.End.Z + 0.02f;
        var middle = bounds.GetCenter() with { Z = front };
        // Moved by its middle only (details aren't sized here).
        return [(0, t * middle)];
    }

    private bool NearDetailHandle(Building building, Camera3D camera, Vector2 screen) =>
        SelectedDetail is { } d && building.Fits(d) && DetailHandles(building, d).Any(h =>
        {
            var world = building.ToGlobal(h.At);
            return !camera.IsPositionBehind(world) && camera.UnprojectPosition(world).DistanceTo(screen) < ClickReach;
        });
}

#endif
