#if TOOLS
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Doors and windows: Window and Door in the toolbar place one of the usual size where a wall is
/// clicked (on the storey clicked); clicking one selects it, for its handles (its edges and its
/// middle, moved in 10 cm steps) and the strip in the toolbar (its distances from both ends of
/// its wall, its size, and buttons to centre it); Ctrl-clicking one takes it away.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    /// <summary>The step openings are placed, moved and sized in, in metres.</summary>
    public const float OpeningStep = 0.1f;

    private Button? _window;
    private Button? _door;
    private HBoxContainer? _panel;
    private SpinBox? _fromStart;
    private SpinBox? _fromEnd;
    private SpinBox? _width;
    private SpinBox? _height;
    private SpinBox? _sill;
    private SpinBox? _storey;
    private CheckBox? _allStoreys;
    private SpinBox? _panesAcross;
    private SpinBox? _panesUp;
    private OptionButton? _leaves;
    private CheckBox? _glazed;
    private Button? _centreUp;

    /// <summary>While filling the strip in: its changes aren't the user's.</summary>
    private bool _filling;

    /// <summary>The selected door or window, if any.</summary>
    public BuildingOpening? SelectedOpening { get; private set; }

    /// <summary>Where one would be placed now, while placing: a new one, on the storey under the mouse.</summary>
    public (BuildingOpening Opening, int Storey)? Ghost { get; private set; }

    /// <summary>What is being placed, if anything.</summary>
    private OpeningKind? Placing => _window?.ButtonPressed == true ? OpeningKind.Window : _door?.ButtonPressed == true ? OpeningKind.Door : null;

    /// <summary>A length in the steps openings go in.</summary>
    public static float Step(float metres) => Mathf.Snapped(metres, OpeningStep);

    private Button Toggle(string text, string tip, Color accent)
    {
        var button = new Button { Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = "FlatButton", TooltipText = tip };
        button.AddThemeColorOverride("font_pressed_color", accent);
        button.AddThemeColorOverride("font_hover_pressed_color", accent);
        return button;
    }

    private SpinBox Number(string label, float min, float max, float step, StringName method)
    {
        _panel!.AddChild(new Label { Text = label });
        var box = new SpinBox { MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(70, 0), SelectAllOnFocus = true };
        box.Connect(Godot.Range.SignalName.ValueChanged, new Callable(this, method));
        _panel.AddChild(box);
        return box;
    }

    /// <summary>The Window and Door buttons, and the strip for the selected one.</summary>
    private void AddOpeningTools(Color accent)
    {
        _window = Toggle("Window", "Click a wall to put a window there.", accent);
        _door = Toggle("Door", "Click a wall to put a door there.", accent);
        _window.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.WindowToggled));
        _door.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.DoorToggled));
        // Picked in the dock (Wall Details: Doors & Windows); these hold what is picked, out of sight.
        _window.Visible = _door.Visible = false;
        _toolbar!.AddChild(_window);
        _toolbar.AddChild(_door);

        _panel = new HBoxContainer { Visible = false };
        _toolbar.AddChild(_panel);
        _panel.AddChild(new VSeparator());
        _fromStart = Number("From start", 0, 1000, OpeningStep, MethodName.FromStartChanged);
        _fromEnd = Number("From end", 0, 1000, OpeningStep, MethodName.FromEndChanged);
        _width = Number("Width", 0.2f, 20, OpeningStep, MethodName.WidthChanged);
        _height = Number("Height", 0.2f, 20, OpeningStep, MethodName.HeightChanged);
        _sill = Number("Sill", 0, 20, OpeningStep, MethodName.SillChanged);
        _storey = Number("Storey", 0, 40, 1, MethodName.StoreyChanged);
        _allStoreys = new CheckBox { Text = "All storeys", FocusMode = Control.FocusModeEnum.None };
        _allStoreys.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.AllStoreysToggled));
        _panel.AddChild(_allStoreys);
        _panesAcross = Number("Panes", 1, 12, 1, MethodName.PanesChanged);
        _panesUp = Number("×", 1, 12, 1, MethodName.PanesChanged);
        _leaves = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _leaves.AddItem("Single door", (int)DoorLeaves.Single);
        _leaves.AddItem("Double doors", (int)DoorLeaves.Double);
        _leaves.AddItem("Open doorway", (int)DoorLeaves.None);
        _leaves.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.LeavesChanged));
        _panel.AddChild(_leaves);
        _glazed = new CheckBox { Text = "Glazed", FocusMode = Control.FocusModeEnum.None };
        _glazed.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.GlazedToggled));
        _panel.AddChild(_glazed);
        var across = new Button { Text = "Centre Across", FocusMode = Control.FocusModeEnum.None, TooltipText = "Puts it in the middle of its wall, along it." };
        across.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.CentreAcross));
        _panel.AddChild(across);
        _centreUp = new Button { Text = "Centre Up", FocusMode = Control.FocusModeEnum.None, TooltipText = "Puts it in the middle of its storey, up it." };
        _centreUp.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.CentreUp));
        _panel.AddChild(_centreUp);
    }

    private void WindowToggled(bool on) => PlacingToggled(on, _door);

    private void DoorToggled(bool on) => PlacingToggled(on, _window);

    private void PlacingToggled(bool on, Button? other)
    {
        if (on)
        {
            other?.SetPressedNoSignal(false);
            StopWalls();
        StopStairs();
        StopRooms();
        StopInner();
            StopPlacingDetail();
            StopPlacingProjection();
            SetDrawing(false);
            _side?.SetPressedNoSignal(false);
        }

        Ghost = null;
        ShowOpeningButtons();
        _building?.UpdateGizmos();
    }

    /// <summary>The dock's Window and Door buttons pressed as the toolbar's are.</summary>
    private void ShowOpeningButtons()
    {
        if (_detailSections is null)
            return;
        foreach (var button in _detailSections.FindChildren("*", nameof(Button), true, false).OfType<DetailButton>().Where(b => b.Index < 0))
            button.SetPressedNoSignal(button.Index == WindowButton ? _window?.ButtonPressed == true : _door?.ButtonPressed == true);
    }

    /// <summary>Placing stopped (another tool, or the building let go).</summary>
    private void StopPlacing()
    {
        _window?.SetPressedNoSignal(false);
        _door?.SetPressedNoSignal(false);
        Ghost = null;
        ShowOpeningButtons();
    }

    /// <summary>Selects a door or window (or none), and shows it in the strip.</summary>
    public void Select(BuildingOpening? opening)
    {
        if (SelectedOpening is { } was && IsInstanceValid(was) && was.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.Fill)))
            was.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.Fill));
        SelectedOpening = opening;
        if (opening is not null)
        {
            opening.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.Fill));
            SelectedWall = null;
            SelectedStairs = null;
            SelectedRoom = null;
            SelectedInner = null;
            SelectDetail(null);
            SelectProjection(null);
        }

        Fill();
        QueueInspect();
        _building?.UpdateGizmos();
    }

    /// <summary>The strip filled in from the selected one.</summary>
    private void Fill()
    {
        if (_panel is null)
            return;
        var o = SelectedOpening;
        // Its settings are in the Inspector.
        _panel.Visible = false;
        if (o is null || _building is null)
            return;
        _filling = true;
        var length = _building.SideLength(o.Side);
        _fromStart!.Value = o.From;
        _fromEnd!.Value = length - o.From - o.Width;
        _width!.Value = o.Width;
        _height!.Value = o.Height;
        _sill!.Value = o.Sill;
        _storey!.Value = o.Storey;
        _storey.MaxValue = Mathf.Max(0, _building.AllStoreys - 1);
        _allStoreys!.ButtonPressed = o.AllStoreys;
        _panesAcross!.Value = o.Panes.X;
        _panesUp!.Value = o.Panes.Y;
        _leaves!.Selected = (int)o.Leaves;
        _glazed!.ButtonPressed = o.Glazed;
        var window = o.Kind == OpeningKind.Window;
        foreach (var c in new Control[] { _sill, _allStoreys, _panesAcross, _panesUp })
            c.Visible = window;
        _sill.GetParent().GetChild(_sill.GetIndex() - 1).Set(CanvasItem.PropertyName.Visible, window);
        _panesAcross.GetParent().GetChild(_panesAcross.GetIndex() - 1).Set(CanvasItem.PropertyName.Visible, window);
        _panesUp.GetParent().GetChild(_panesUp.GetIndex() - 1).Set(CanvasItem.PropertyName.Visible, window);
        _leaves.Visible = _glazed.Visible = !window;
        _centreUp!.Visible = window;
        _filling = false;
    }

    /// <summary>Changes the selected one's settings, as a step to undo.</summary>
    private void Change(string action, params (StringName Property, Variant Value)[] changes)
    {
        if (_filling || SelectedOpening is not { } o)
            return;
        var undo = GetUndoRedo();
        undo.CreateAction(action, UndoRedo.MergeMode.Ends, _building);
        foreach (var (property, value) in changes)
        {
            undo.AddDoProperty(o, property, value);
            undo.AddUndoProperty(o, property, o.Get(property));
        }

        undo.CommitAction();
    }

    /// <summary>A start along its wall kept off the wall's ends (by its margin).</summary>
    private float Kept(BuildingOpening o, float from, float? width = null)
    {
        var length = _building!.SideLength(o.Side);
        var w = width ?? o.Width;
        return Mathf.Clamp(from, BuildingOpening.Margin, Mathf.Max(BuildingOpening.Margin, length - BuildingOpening.Margin - w));
    }

    private void FromStartChanged(double value)
    {
        if (SelectedOpening is { } o && _building is not null)
            Change("Move opening", (BuildingOpening.PropertyName.From, Kept(o, (float)value)));
    }

    private void FromEndChanged(double value)
    {
        if (SelectedOpening is { } o && _building is not null)
            Change("Move opening", (BuildingOpening.PropertyName.From, Kept(o, _building.SideLength(o.Side) - (float)value - o.Width)));
    }

    private void WidthChanged(double value)
    {
        if (SelectedOpening is not { } o || _building is null)
            return;
        // No wider than its wall leaves room for; moved back from the end if it would reach it.
        var width = Mathf.Min((float)value, _building.SideLength(o.Side) - 2f * BuildingOpening.Margin);
        Change("Resize opening", (BuildingOpening.PropertyName.Width, width), (BuildingOpening.PropertyName.From, Kept(o, o.From, width)));
    }

    private void HeightChanged(double value) => Change("Resize opening", (BuildingOpening.PropertyName.Height, (float)value));

    private void SillChanged(double value) => Change("Move opening", (BuildingOpening.PropertyName.Sill, (float)value));

    private void StoreyChanged(double value) => Change("Move opening", (BuildingOpening.PropertyName.Storey, (int)value));

    private void AllStoreysToggled(bool on) => Change("Change window", (BuildingOpening.PropertyName.AllStoreys, on));

    private void PanesChanged(double _) => Change("Change window", (BuildingOpening.PropertyName.Panes, new Vector2I((int)_panesAcross!.Value, (int)_panesUp!.Value)));

    private void LeavesChanged(long index) => Change("Change door", (BuildingOpening.PropertyName.Leaves, (int)index));

    private void GlazedToggled(bool on) => Change("Change door", (BuildingOpening.PropertyName.Glazed, on));

    private void CentreAcross()
    {
        if (SelectedOpening is { } o && _building is not null)
            Change("Centre opening", (BuildingOpening.PropertyName.From, (_building.SideLength(o.Side) - o.Width) / 2f));
    }

    private void CentreUp()
    {
        if (SelectedOpening is { Kind: OpeningKind.Window } o && _building is not null)
            Change("Centre opening", (BuildingOpening.PropertyName.Sill, Mathf.Max(0f, (_building.StoreyHeight - o.Height) / 2f)));
    }

    /// <summary>The ray under a place in the view, in the building's own space.</summary>
    private static (Vector3 From, Vector3 Direction) LocalRay(Building building, Camera3D camera, Vector2 screen)
    {
        var inverse = building.GlobalTransform.AffineInverse();
        return (inverse * camera.ProjectRayOrigin(screen), (inverse.Basis * camera.ProjectRayNormal(screen)).Normalized());
    }

    /// <summary>The door or window (and the storey of it) under a place in the view, if any.</summary>
    private (BuildingOpening Opening, int Storey)? OpeningAt(Building building, Camera3D camera, Vector2 screen)
    {
        var (from, direction) = LocalRay(building, camera, screen);
        if (building.WallAt(from, direction) is not { } hit)
            return null;
        foreach (var (o, storey) in building.Placed())
        {
            var v0 = building.StoreyFloor(storey) + o.Bottom;
            if (o.Side == hit.Side && hit.U >= o.From && hit.U <= o.From + o.Width && hit.V >= v0 && hit.V <= v0 + o.Height)
                return (o, storey);
        }

        return null;
    }

    /// <summary>The view's clicks and moves for doors and windows; whether it used them.</summary>
    private bool OpeningInput(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        if (Placing is { } kind)
        {
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    Ghost = GhostAt(building, kind, camera, motion.Position);
                    building.UpdateGizmos();
                    return false;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                    if (GhostAt(building, kind, camera, click.Position) is { } ghost)
                    {
                        var opening = ghost.Opening;
                        var undo = GetUndoRedo();
                        undo.CreateAction(kind == OpeningKind.Door ? "Add door" : "Add window", customContext: building);
                        undo.AddDoProperty(building, Building.PropertyName.Openings, new Godot.Collections.Array<BuildingOpening>(building.Openings.Append(opening).ToArray()));
                        undo.AddUndoProperty(building, Building.PropertyName.Openings, building.Openings);
                        undo.CommitAction();
                        Select(opening);
                    }

                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    StopPlacing();
                    building.UpdateGizmos();
                    return true;
            }

            return false;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
        {
            // On one of the selected one's handles: the editor's, to drag.
            if (!press.CtrlPressed && NearHandle(building, camera, press.Position))
                return false;
            if (OpeningAt(building, camera, press.Position) is { } at)
            {
                if (press.CtrlPressed)
                {
                    // Ctrl-click: taken away.
                    RemoveOpening(building, at.Opening);
                    return true;
                }

                Select(at.Opening);
                return true;
            }

            if (SelectedOpening is not null)
                Select(null);
        }

        return false;
    }

    /// <summary>
    /// Where along a wall of a length an opening of a width may start: its margin from each end
    /// (rounded in to the 10 cm steps). The first after the last if it doesn't fit.
    /// </summary>
    public static (float Low, float High) Room(float length, float width) =>
        (Mathf.Ceil(BuildingOpening.Margin / OpeningStep - 0.001f) * OpeningStep, Mathf.Floor((length - BuildingOpening.Margin - width) / OpeningStep + 0.001f) * OpeningStep);

    /// <summary>Takes a door or window out of a building, as a step to undo.</summary>
    private void RemoveOpening(Building building, BuildingOpening opening)
    {
        var undo = GetUndoRedo();
        undo.CreateAction(opening.Kind == OpeningKind.Door ? "Remove door" : "Remove window", customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Openings, new Godot.Collections.Array<BuildingOpening>(building.Openings.Where(o => o != opening).ToArray()));
        undo.AddUndoProperty(building, Building.PropertyName.Openings, building.Openings);
        undo.CommitAction();
        if (SelectedOpening == opening)
            Select(null);
    }

    /// <summary>Whether a place in the view is on one of the selected door's or window's handles.</summary>
    private bool NearHandle(Building building, Camera3D camera, Vector2 screen)
    {
        if (SelectedOpening is not { } o || o.Side >= building.Outline.Length)
            return false;
        var v0 = building.StoreyFloor(o.Storey) + o.Bottom;
        var (u0, u1, v1) = (o.From, o.From + o.Width, v0 + o.Height);
        var points = new[] { (u0, (v0 + v1) / 2f), (u1, (v0 + v1) / 2f), ((u0 + u1) / 2f, v1), ((u0 + u1) / 2f, v0), ((u0 + u1) / 2f, (v0 + v1) / 2f) };
        foreach (var (u, v) in points)
        {
            var world = building.ToGlobal(building.OnSide(o.Side, u, v));
            if (!camera.IsPositionBehind(world) && camera.UnprojectPosition(world).DistanceTo(screen) < ClickReach)
                return true;
        }

        return false;
    }

    /// <summary>A new door or window of the usual size centred under the mouse on the wall it is over, on its storey; none off the walls, or where it doesn't fit.</summary>
    private static (BuildingOpening Opening, int Storey)? GhostAt(Building building, OpeningKind kind, Camera3D camera, Vector2 screen)
    {
        var (from, direction) = LocalRay(building, camera, screen);
        if (building.WallAt(from, direction) is not { } hit)
            return null;
        var storey = building.StoreyOn(hit.Side, hit.V);
        var opening = BuildingOpening.Make(kind, hit.Side, storey, 0f);
        var length = building.SideLength(hit.Side);
        var (low, high) = Room(length, opening.Width);
        if (high < low)
            return null;
        opening.From = Mathf.Clamp(Step(hit.U - opening.Width / 2f), low, high);
        return (opening, storey);
    }
}
#endif
