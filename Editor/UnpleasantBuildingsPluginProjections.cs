#if TOOLS
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Balconies and awnings: Balcony and Awning in the Wall Details dock put one of the usual size
/// where a wall is clicked (on the storey clicked); clicking one selects it, for its handles (its
/// edges, how far out it reaches, and its middle, in 10 cm steps) and the strip in the toolbar;
/// Ctrl-clicking one, or Delete with it selected, takes it away.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    /// <summary>The dock's Balcony and Awning buttons, by the numbers they hold in place of a detail's.</summary>
    private const int BalconyButton = -3;
    private const int AwningButton = -4;
    private const int PorchButton = -5;

    private static ProjectionKind KindOf(int button) => button switch
    {
        BalconyButton => ProjectionKind.Balcony,
        AwningButton => ProjectionKind.Awning,
        _ => ProjectionKind.Porch,
    };

    private static string NameOf(ProjectionKind kind) => kind switch
    {
        ProjectionKind.Balcony => "balcony",
        ProjectionKind.Awning => "awning",
        _ => "porch",
    };

    private HBoxContainer? _ledgePanel;
    private SpinBox? _ledgeFromStart;
    private SpinBox? _ledgeFromEnd;
    private SpinBox? _ledgeWidth;
    private SpinBox? _ledgeDepth;
    private SpinBox? _ledgeUp;
    private SpinBox? _ledgeStorey;
    private CheckBox? _ledgeAllStoreys;
    private OptionButton? _ledgeRailing;
    private SpinBox? _ledgeRailingHeight;
    private OptionButton? _ledgeStyle;
    private CheckBox? _ledgeCheeks;
    private SpinBox? _ledgeDrop;
    private OptionButton? _porchRoof;
    private SpinBox? _porchRoofHeight;
    private CheckBox? _porchRailing;
    private CheckBox? _porchSteps;
    private SpinBox? _porchStepsFrom;
    private SpinBox? _porchStepsWidth;
    private CheckBox? _porchSkirt;

    /// <summary>What is being put on walls from the dock's Balcony or Awning, if either.</summary>
    public ProjectionKind? PlacingProjection { get; private set; }

    /// <summary>Where one would go now, while placing: a new one, on the storey under the mouse.</summary>
    public (BuildingProjection Projection, int Storey)? ProjectionGhost { get; private set; }

    /// <summary>The selected balcony or awning, if any.</summary>
    public BuildingProjection? SelectedProjection { get; private set; }

    private void AddProjectionTools()
    {
        _ledgePanel = new HBoxContainer { Visible = false };
        _toolbar!.AddChild(_ledgePanel);
        _ledgePanel.AddChild(new VSeparator());
        SpinBox Box(string label, float min, float max, float step, StringName method)
        {
            _ledgePanel.AddChild(new Label { Text = label });
            var box = new SpinBox { MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(70, 0), SelectAllOnFocus = true };
            box.Connect(Godot.Range.SignalName.ValueChanged, new Callable(this, method));
            _ledgePanel.AddChild(box);
            return box;
        }

        CheckBox Check(string text, StringName method)
        {
            var check = new CheckBox { Text = text, FocusMode = Control.FocusModeEnum.None };
            check.Connect(BaseButton.SignalName.Toggled, new Callable(this, method));
            _ledgePanel.AddChild(check);
            return check;
        }

        _ledgeFromStart = Box("From start", 0, 1000, OpeningStep, MethodName.LedgeFromStartChanged);
        _ledgeFromEnd = Box("From end", 0, 1000, OpeningStep, MethodName.LedgeFromEndChanged);
        _ledgeWidth = Box("Width", 0.3f, 30, OpeningStep, MethodName.LedgeWidthChanged);
        _ledgeDepth = Box("Out", 0.2f, 10, OpeningStep, MethodName.LedgeDepthChanged);
        _ledgeUp = Box("Up", -10, 20, OpeningStep, MethodName.LedgeUpChanged);
        _ledgeStorey = Box("Storey", 0, 40, 1, MethodName.LedgeStoreyChanged);
        _ledgeAllStoreys = new CheckBox { Text = "All storeys", FocusMode = Control.FocusModeEnum.None };
        _ledgeAllStoreys.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.LedgeAllStoreysToggled));
        _ledgePanel.AddChild(_ledgeAllStoreys);
        _porchRailing = Check("Railing", MethodName.PorchRailingToggled);
        _ledgeRailing = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _ledgeRailing.AddItem("Bars", (int)RailingStyle.Bars);
        _ledgeRailing.AddItem("Solid", (int)RailingStyle.Solid);
        _ledgeRailing.AddItem("Glass", (int)RailingStyle.Glass);
        _ledgeRailing.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.LedgeRailingChanged));
        _ledgePanel.AddChild(_ledgeRailing);
        _ledgeRailingHeight = Box("Railing", 0.3f, 2, OpeningStep, MethodName.LedgeRailingHeightChanged);
        _ledgeStyle = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _ledgeStyle.AddItem("Sloped", (int)AwningStyle.Sloped);
        _ledgeStyle.AddItem("Flat", (int)AwningStyle.Flat);
        _ledgeStyle.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.LedgeStyleChanged));
        _ledgePanel.AddChild(_ledgeStyle);
        _ledgeCheeks = new CheckBox { Text = "Sides", FocusMode = Control.FocusModeEnum.None };
        _ledgeCheeks.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.LedgeCheeksToggled));
        _ledgePanel.AddChild(_ledgeCheeks);
        _ledgeDrop = Box("Drop", 0, 3, OpeningStep, MethodName.LedgeDropChanged);
        _porchRoof = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _porchRoof.AddItem("Shed roof", (int)PorchRoofStyle.Shed);
        _porchRoof.AddItem("Flat roof", (int)PorchRoofStyle.Flat);
        _porchRoof.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.PorchRoofChanged));
        _ledgePanel.AddChild(_porchRoof);
        _porchRoofHeight = Box("Roof at", 1.8f, 6, OpeningStep, MethodName.PorchRoofHeightChanged);
        _porchSteps = Check("Steps", MethodName.PorchStepsToggled);
        _porchStepsFrom = Box("Steps at", 0, 30, OpeningStep, MethodName.PorchStepsFromChanged);
        _porchStepsWidth = Box("Steps width", 0.6f, 10, OpeningStep, MethodName.PorchStepsWidthChanged);
        _porchSkirt = Check("Skirt", MethodName.PorchSkirtToggled);
        var across = new Button { Text = "Centre Across", FocusMode = Control.FocusModeEnum.None, TooltipText = "Puts it in the middle of its wall, along it." };
        across.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.LedgeCentreAcross));
        _ledgePanel.AddChild(across);
    }

    /// <summary>The dock's Balcony or Awning toggled.</summary>
    private void ProjectionToggled(int index, bool on)
    {
        var kind = KindOf(index);
        if (!on)
        {
            if (PlacingProjection == kind)
                StopPlacingProjection();
            return;
        }

        PlacingProjection = kind;
        StopWalls();
        StopStairs();
        StopRooms();
        StopInner();
        SetDrawing(false);
        StopPlacing();
        _side?.SetPressedNoSignal(false);
        PlacingDetail = null;
        DetailGhost = null;
    }

    private void StopPlacingProjection()
    {
        if (PlacingProjection is null && ProjectionGhost is null)
            return;
        PlacingProjection = null;
        ProjectionGhost = null;
        if (_detailSections is not null)
            foreach (var button in _detailSections.FindChildren("*", nameof(Button), true, false).OfType<DetailButton>().Where(b => b.Index is BalconyButton or AwningButton or PorchButton))
                button.SetPressedNoSignal(false);
        _building?.UpdateGizmos();
    }

    /// <summary>Selects a balcony or awning (or none), and shows it in the strip.</summary>
    public void SelectProjection(BuildingProjection? projection)
    {
        if (SelectedProjection is { } was && IsInstanceValid(was) && was.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.FillLedge)))
            was.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.FillLedge));
        SelectedProjection = projection;
        if (projection is not null)
        {
            projection.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.FillLedge));
            SelectedWall = null;
            SelectedStairs = null;
            SelectedRoom = null;
            SelectedInner = null;
            Select(null);
            SelectDetail(null);
        }

        FillLedge();
        QueueInspect();
        _building?.UpdateGizmos();
    }

    private void FillLedge()
    {
        if (_ledgePanel is null)
            return;
        var p = SelectedProjection;
        // Its settings are in the Inspector.
        _ledgePanel.Visible = false;
        if (p is null || _building is null)
            return;
        _filling = true;
        _ledgeFromStart!.Value = p.From;
        _ledgeFromEnd!.Value = _building.SideLength(p.Side) - p.From - p.Width;
        _ledgeWidth!.Value = p.Width;
        _ledgeDepth!.Value = p.Depth;
        _ledgeUp!.Value = p.Up;
        _ledgeStorey!.Value = p.Storey;
        _ledgeStorey.MaxValue = Mathf.Max(0, _building.AllStoreys - 1);
        _ledgeAllStoreys!.ButtonPressed = p.AllStoreys;
        _ledgeRailing!.Selected = (int)p.Railing;
        _ledgeRailingHeight!.Value = p.RailingHeight;
        _ledgeStyle!.Selected = (int)p.AwningStyle;
        _ledgeCheeks!.ButtonPressed = p.Cheeks;
        _ledgeDrop!.Value = p.Drop;
        _porchRoof!.Selected = (int)p.PorchRoof;
        _porchRoofHeight!.Value = p.RoofHeight;
        _porchRailing!.ButtonPressed = p.HasRailing;
        _porchSteps!.ButtonPressed = p.Steps;
        _porchStepsFrom!.MaxValue = Mathf.Max(0f, p.Width - p.StepsWidth);
        _porchStepsFrom.Value = p.StepsFrom;
        _porchStepsWidth!.MaxValue = p.Width;
        _porchStepsWidth.Value = p.StepsWidth;
        _porchSkirt!.ButtonPressed = p.Skirt;
        var balcony = p.Kind == ProjectionKind.Balcony;
        var porch = p.Kind == ProjectionKind.Porch;
        var awning = p.Kind == ProjectionKind.Awning;
        var sloped = awning && p.AwningStyle == AwningStyle.Sloped;
        void Show(Control c, bool show, bool labelled)
        {
            c.Visible = show;
            if (labelled)
                c.GetParent().GetChild(c.GetIndex() - 1).Set(CanvasItem.PropertyName.Visible, show);
        }

        Show(_ledgeStorey, !porch, true);
        Show(_ledgeAllStoreys, balcony, false);
        Show(_porchRailing, porch, false);
        Show(_ledgeRailing, balcony || (porch && p.HasRailing), false);
        Show(_ledgeRailingHeight, balcony || (porch && p.HasRailing), true);
        Show(_ledgeStyle, awning, false);
        Show(_porchRoof, porch, false);
        Show(_porchRoofHeight, porch, true);
        Show(_porchSteps, porch, false);
        Show(_porchStepsFrom, porch && p.Steps, true);
        Show(_porchStepsWidth, porch && p.Steps, true);
        Show(_porchSkirt, porch, false);
        Show(_ledgeCheeks, sloped, false);
        Show(_ledgeDrop, sloped, true);
        _filling = false;
    }

    /// <summary>Changes the selected one's settings, as a step to undo.</summary>
    private void ChangeLedge(string action, params (StringName Property, Variant Value)[] changes)
    {
        if (_filling || SelectedProjection is not { } p)
            return;
        var undo = GetUndoRedo();
        undo.CreateAction(action, UndoRedo.MergeMode.Ends, _building);
        foreach (var (property, value) in changes)
        {
            undo.AddDoProperty(p, property, value);
            undo.AddUndoProperty(p, property, p.Get(property));
        }

        undo.CommitAction();
    }

    private void LedgeFromStartChanged(double value) => ChangeLedge("Move balcony or awning", (BuildingProjection.PropertyName.From, (float)value));

    private void LedgeFromEndChanged(double value)
    {
        if (SelectedProjection is { } p && _building is not null)
            ChangeLedge("Move balcony or awning", (BuildingProjection.PropertyName.From, _building.SideLength(p.Side) - (float)value - p.Width));
    }

    private void LedgeWidthChanged(double value) => ChangeLedge("Resize balcony or awning", (BuildingProjection.PropertyName.Width, (float)value));

    private void LedgeDepthChanged(double value) => ChangeLedge("Resize balcony or awning", (BuildingProjection.PropertyName.Depth, (float)value));

    private void LedgeUpChanged(double value) => ChangeLedge("Move balcony or awning", (BuildingProjection.PropertyName.Up, (float)value));

    private void LedgeStoreyChanged(double value) => ChangeLedge("Move balcony or awning", (BuildingProjection.PropertyName.Storey, (int)value));

    private void LedgeAllStoreysToggled(bool on) => ChangeLedge("Change balcony", (BuildingProjection.PropertyName.AllStoreys, on));

    private void LedgeRailingChanged(long index) => ChangeLedge("Change balcony", (BuildingProjection.PropertyName.Railing, (int)index));

    private void LedgeRailingHeightChanged(double value) => ChangeLedge("Change balcony", (BuildingProjection.PropertyName.RailingHeight, (float)value));

    private void LedgeStyleChanged(long index) => ChangeLedge("Change awning", (BuildingProjection.PropertyName.AwningStyle, (int)index));

    private void LedgeCheeksToggled(bool on) => ChangeLedge("Change awning", (BuildingProjection.PropertyName.Cheeks, on));

    private void LedgeDropChanged(double value) => ChangeLedge("Change awning", (BuildingProjection.PropertyName.Drop, (float)value));

    private void PorchRoofChanged(long index) => ChangeLedge("Change porch", (BuildingProjection.PropertyName.PorchRoof, (int)index));

    private void PorchRoofHeightChanged(double value) => ChangeLedge("Change porch", (BuildingProjection.PropertyName.RoofHeight, (float)value));

    private void PorchRailingToggled(bool on) => ChangeLedge("Change porch", (BuildingProjection.PropertyName.HasRailing, on));

    private void PorchStepsToggled(bool on) => ChangeLedge("Change porch", (BuildingProjection.PropertyName.Steps, on));

    private void PorchStepsFromChanged(double value) => ChangeLedge("Move porch steps", (BuildingProjection.PropertyName.StepsFrom, (float)value));

    private void PorchStepsWidthChanged(double value) => ChangeLedge("Resize porch steps", (BuildingProjection.PropertyName.StepsWidth, (float)value));

    private void PorchSkirtToggled(bool on) => ChangeLedge("Change porch", (BuildingProjection.PropertyName.Skirt, on));

    private void LedgeCentreAcross()
    {
        if (SelectedProjection is { } p && _building is not null)
            ChangeLedge("Centre balcony or awning", (BuildingProjection.PropertyName.From, (_building.SideLength(p.Side) - p.Width) / 2f));
    }

    /// <summary>A side's own space, in the building's: X along it from its start, Y up, Z out of the wall.</summary>
    public static Transform3D SideSpace(Building building, int side)
    {
        var origin = building.OnSide(side, 0f, 0f);
        var along = building.OnSide(side, 1f, 0f) - origin;
        return new Transform3D(new Basis(along, Vector3.Up, building.SideOut(side).Normalized()), origin);
    }

    /// <summary>What a balcony or awning on a storey takes up, in its side's own space (see <see cref="SideSpace"/>).</summary>
    public static Aabb LedgeBox(Building building, BuildingProjection p, int storey)
    {
        var (below, above) = p.Reach;
        var v = building.StoreyFloor(storey) + p.Up;
        return new Aabb(new Vector3(p.From, v - below, 0f), new Vector3(p.Width, below + above, p.Depth));
    }

    /// <summary>The balcony or awning (and its storey) under a place in the view, if any (the nearest).</summary>
    private (BuildingProjection Projection, int Storey)? ProjectionAt(Building building, Camera3D camera, Vector2 screen)
    {
        var (from, direction) = LocalRay(building, camera, screen);
        (BuildingProjection, int)? best = null;
        var nearest = float.PositiveInfinity;
        foreach (var (p, storey) in building.PlacedProjections())
        {
            var space = SideSpace(building, p.Side);
            var inverse = space.AffineInverse();
            if (Hit(LedgeBox(building, p, storey).Grow(0.03f), inverse * from, inverse.Basis * direction) is { } hit && (space * hit - from).Length() < nearest)
            {
                nearest = (space * hit - from).Length();
                best = (p, storey);
            }
        }

        return best;
    }

    /// <summary>A new balcony or awning of the usual size centred under the mouse on the wall it is over, on its storey; none off the walls, or where it doesn't fit.</summary>
    private static (BuildingProjection, int)? ProjectionGhostAt(Building building, ProjectionKind kind, Camera3D camera, Vector2 screen)
    {
        var (from, direction) = LocalRay(building, camera, screen);
        if (building.WallAt(from, direction) is not { } hit)
            return null;
        var storey = kind == ProjectionKind.Porch ? 0 : building.StoreyOn(hit.Side, hit.V);
        var projection = BuildingProjection.Make(kind, hit.Side, storey, 0f);
        var length = building.SideLength(hit.Side);
        if (projection.Width > length)
            return null;
        projection.From = Mathf.Clamp(Step(hit.U - projection.Width / 2f), 0f, Mathf.Floor((length - projection.Width) / OpeningStep + 0.001f) * OpeningStep);
        return (projection, storey);
    }

    /// <summary>The view's clicks and moves for balconies and awnings; whether it used them.</summary>
    private bool ProjectionInput(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        if (PlacingProjection is { } kind)
        {
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    ProjectionGhost = ProjectionGhostAt(building, kind, camera, motion.Position);
                    building.UpdateGizmos();
                    return false;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                    if (ProjectionGhostAt(building, kind, camera, click.Position) is { } ghost)
                    {
                        var (projection, _) = ghost;
                        var undo = GetUndoRedo();
                        undo.CreateAction($"Add {NameOf(kind)}", customContext: building);
                        undo.AddDoProperty(building, Building.PropertyName.Projections, new Godot.Collections.Array<BuildingProjection>(building.Projections.Append(projection).ToArray()));
                        undo.AddUndoProperty(building, Building.PropertyName.Projections, building.Projections);
                        undo.CommitAction();
                        SelectProjection(projection);
                    }

                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    StopPlacingProjection();
                    return true;
            }

            return false;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
        {
            if (!press.CtrlPressed && NearLedgeHandle(building, camera, press.Position))
                return false;
            // A door, window or detail under the mouse comes first: a balcony's or porch's box takes in
            // the open space in front of the wall too (under its roof, behind its railing), so those
            // in it could never be picked otherwise.
            if (ProjectionAt(building, camera, press.Position) is { } at
                && DetailAt(building, camera, press.Position) is null && OpeningAt(building, camera, press.Position) is null)
            {
                if (press.CtrlPressed)
                    RemoveProjection(building, at.Projection);
                else
                    SelectProjection(at.Projection);
                return true;
            }

            if (SelectedProjection is not null)
                SelectProjection(null);
        }

        return false;
    }

    /// <summary>Takes a balcony or awning off a building, as a step to undo.</summary>
    private void RemoveProjection(Building building, BuildingProjection projection)
    {
        var undo = GetUndoRedo();
        undo.CreateAction($"Remove {NameOf(projection.Kind)}", customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Projections, new Godot.Collections.Array<BuildingProjection>(building.Projections.Where(p => p != projection).ToArray()));
        undo.AddUndoProperty(building, Building.PropertyName.Projections, building.Projections);
        undo.CommitAction();
        if (SelectedProjection == projection)
            SelectProjection(null);
    }

    /// <summary>
    /// The selected balcony's or awning's handles, in the building's own space, on its own storey:
    /// its left and right edges, how far out it reaches, and its middle.
    /// </summary>
    public static (int Id, Vector3 At)[] LedgeHandles(Building building, BuildingProjection p)
    {
        var space = SideSpace(building, p.Side);
        var box = LedgeBox(building, p, p.Storey);
        var (y, z) = (box.GetCenter().Y, box.GetCenter().Z);
        return
        [
            (0, space * new Vector3(box.Position.X, y, z)),
            (1, space * new Vector3(box.End.X, y, z)),
            (2, space * new Vector3(box.GetCenter().X, y, box.End.Z)),
            (3, space * box.GetCenter()),
        ];
    }

    private bool NearLedgeHandle(Building building, Camera3D camera, Vector2 screen) =>
        SelectedProjection is { } p && p.Side < building.SideCount && LedgeHandles(building, p).Any(h =>
        {
            var world = building.ToGlobal(h.At);
            return !camera.IsPositionBehind(world) && camera.UnprojectPosition(world).DistanceTo(screen) < ClickReach;
        });
}
#endif
