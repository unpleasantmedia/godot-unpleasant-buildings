#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Edits a selected <see cref="Building"/> in the 3D view. With Draw on (in the view's toolbar),
/// each click on the ground adds a corner to its outline; clicking its first corner, Enter, Escape
/// or a right click ends drawing. Its corners are handles, dragged over the ground; dragging the
/// handle in the middle of a side adds a corner there; Ctrl-clicking a corner takes it away. Each a
/// step to undo.
/// </summary>
[Tool]
public partial class UnpleasantBuildingsPlugin : EditorPlugin
{
    private BuildingGizmos? _gizmos;
    private HBoxContainer? _toolbar;

    /// <summary>The splitter between Draw and Snap and the building's own tools.</summary>
    private VSeparator? _afterSnap;
    private Button? _draw;
    private Building? _building;
    private Button? _side;
    private Button? _snap;

    /// <summary>The lines the corner being drawn or dragged last snapped to, from above in the world (shown until it is let go).</summary>
    public List<BuildingSnapping.Guide> Guides { get; } = [];

    /// <summary>
    /// Where a corner goes, in the building's own space, from where the mouse puts it: snapped to
    /// other buildings with Snap on (see <see cref="BuildingSnapping"/>), else to the grid with Shift.
    /// Its neighbours are the corners (by number) it is joined to, each with the one beyond it.
    /// </summary>
    public Vector2 Place(Building building, Vector3 world, bool shift, IEnumerable<(int Near, int Beyond)> neighbours, Camera3D? camera = null, Vector2 screen = default)
    {
        Guides.Clear();
        var local = Flat(building.ToLocal(world));
        if (_snap?.ButtonPressed == true)
        {
            var outline = building.Outline;
            Vector2 W(int i) { var w = building.ToGlobal(new Vector3(outline[i].X, 0f, outline[i].Y)); return new Vector2(w.X, w.Z); }
            var joined = neighbours
                .Where(n => n.Near >= 0 && n.Near < outline.Length)
                .Select(n => (W(n.Near), n.Beyond >= 0 && n.Beyond < outline.Length && n.Beyond != n.Near ? W(n.Beyond) : (Vector2?)null))
                .ToList();
            // Where the mouse's ray meets a level (without a camera, straight down from where it is).
            Vector2? Mouse(float height)
            {
                if (camera is null)
                    return new Vector2(world.X, world.Z);
                var hit = new Plane(Vector3.Up, height).IntersectsRay(camera.ProjectRayOrigin(screen), camera.ProjectRayNormal(screen));
                return hit is { } h ? new Vector2(h.X, h.Z) : null;
            }

            var (at, guides) = BuildingSnapping.Snap(building, Mouse, world.Y, joined);
            if (at.DistanceTo(new Vector2(world.X, world.Z)) > 1e-5f || guides.Count > 0)
            {
                Guides.AddRange(guides);
                return Flat(building.ToLocal(new Vector3(at.X, world.Y, at.Y)));
            }
        }

        return Snapped(local, shift);
    }

    /// <summary>Whether a side is being picked for the roof.</summary>
    public bool PickingSide => _side?.ButtonPressed == true;

    private void SideToggled(bool on)
    {
        if (on)
        {
            SetDrawing(false);
            StopPlacing();
            StopPlacingDetail();
        }

        _building?.UpdateGizmos();
    }

    /// <summary>The Roof Side button shown only for roofs that go by a side.</summary>
    public void ShowSideButton(Building building)
    {
        if (_side is null || building != _building)
            return;
        _side.Visible = building.RoofHasSide;
        if (!_side.Visible)
            _side.SetPressedNoSignal(false);
    }

    /// <summary>What is wrong with the selected building, in the toolbar, if anything.</summary>
    private Label? _problem;

    /// <summary>How near the mouse must be to a corner to click it (to end drawing, or take it away), in pixels.</summary>
    private const float ClickReach = 12f;

    public override void _EnterTree()
    {
        Current = this;
        AddItemInspector();
        AddSceneSaver();
        // The grid shows on selected buildings: redrawn as the selection changes.
        EditorInterface.Singleton.GetSelection().Connect(EditorSelection.SignalName.SelectionChanged, new Callable(this, MethodName.SelectionChanged));
        _gizmos = new BuildingGizmos();
        AddNode3DGizmoPlugin(_gizmos);
        // Buildings keep the gizmos of the plugin before (the scripts reloaded): theirs made anew.
        // A frame on, once the buildings are back on their list (see Building.Rejoin).
        GetTree().Connect(SceneTree.SignalName.ProcessFrame, new Callable(this, MethodName.RenewGizmos), (uint)ConnectFlags.OneShot);
        _toolbar = new HBoxContainer { Visible = false };
        _toolbar.AddChild(new VSeparator());
        _draw = new Button
        {
            Text = "Draw",
            ToggleMode = true,
            // Never the keyboard's: Enter and Escape go on to the 3D view, to end drawing.
            FocusMode = Control.FocusModeEnum.None,
            // As the editor's own toolbar toggles, so being on shows.
            ThemeTypeVariation = "FlatButton",
            TooltipText = "Click corners on the ground to draw the building's outline. Click its first corner, or press Enter, to finish.",
        };
        // Blue when on, as the editor's own toggles are (they tint their icons; this tints its text).
        var accent = EditorInterface.Singleton.GetEditorTheme().GetColor("accent_color", "Editor");
        _draw.AddThemeColorOverride("font_pressed_color", accent);
        _draw.AddThemeColorOverride("font_hover_pressed_color", accent);
        _draw.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.DrawToggled));
        _toolbar.AddChild(_draw);
        _side = new Button
        {
            Text = "Roof Side",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "FlatButton",
            TooltipText = "Click a side of the building: a gabled roof's ridge runs along it, a shed roof slopes down towards it.",
        };
        _side.AddThemeColorOverride("font_pressed_color", accent);
        _side.AddThemeColorOverride("font_hover_pressed_color", accent);
        _side.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.SideToggled));
        _snap = new Button
        {
            Text = "Snap",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "FlatButton",
            TooltipText = "Corners drawn or dragged snap to other buildings: their corners, the lines of their sides, and square to them or to the building's own sides.",
        };
        _snap.AddThemeColorOverride("font_pressed_color", accent);
        _snap.AddThemeColorOverride("font_hover_pressed_color", accent);
        // Draw and Snap first, then a splitter, then the building's own tools.
        _toolbar.AddChild(_snap);
        _afterSnap = new VSeparator();
        _toolbar.AddChild(_afterSnap);
        _toolbar.AddChild(_side);
        AddOpeningTools(accent);
        AddDetailTools();
        AddProjectionTools();
        AddInteriorTools(accent);
        AddStairTools(accent);
        AddRoomTools(accent);
        AddInnerOpeningTools(accent);
        _problem = new Label { Visible = false };
        _problem.AddThemeColorOverride("font_color", EditorInterface.Singleton.GetEditorTheme().GetColor("error_color", "Editor"));
        _toolbar.AddChild(_problem);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
        // The 3D views are there once the editor is up: watched for clicks not passed on (see Recovery).
        Callable.From(WatchViews).CallDeferred();
    }

    public override void _ExitTree()
    {
        UnwatchViews();
        RemoveDetailTools();
        RemoveItemInspector();
        RemoveSceneSaver();
        if (_gizmos is not null)
        {
            RemoveNode3DGizmoPlugin(_gizmos);
            _gizmos = null;
            // None left with this plugin's gizmos, which would redraw through it once it's gone.
            RenewGizmos();
        }

        if (Current == this)
            Current = null;
        var selection = EditorInterface.Singleton.GetSelection();
        if (selection.IsConnected(EditorSelection.SignalName.SelectionChanged, new Callable(this, MethodName.SelectionChanged)))
            selection.Disconnect(EditorSelection.SignalName.SelectionChanged, new Callable(this, MethodName.SelectionChanged));
        if (_toolbar is not null)
        {
            RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
            _toolbar.QueueFree();
        }
    }

    /// <summary>The plugin running now: the gizmos reach it here, since the scripts reloading makes them anew without it.</summary>
    public static UnpleasantBuildingsPlugin? Current { get; private set; }

    /// <summary>Every building in the edited scene given gizmos anew, from the gizmo plugin there is now (if any).</summary>
    private void RenewGizmos()
    {
        // From the buildings in the tree: not by going through the level's nodes (which may not be
        // read from every thread the editor reloads plugins on).
        var buildings = Building.InTree.Where(IsInstanceValid).ToList();
        foreach (var building in buildings)
        {
            building.ClearGizmos();
            if (_gizmos is not null)
                // As the 3D editor does for a node coming into the scene.
                GetTree().CallGroup("_spatial_editor_group", "_request_gizmo", building);
        }

        // The scripts reloaded with a building selected: the editor doesn't pass its clicks on to
        // this (new) plugin until it is edited again, so it is, as if picked anew.
        if (_gizmos is not null && SelectedBuilding() is { } selected && _building is null)
            EditorInterface.Singleton.EditNode(selected);
    }

    /// <summary>The toolbar says what is wrong with the selected building, if anything.</summary>
    public void ShowProblem(Building building)
    {
        if (_problem is null || building != _building)
            return;
        var problem = building.Outline.Length >= 3 && !Building.Simple(building.Outline) ? "Its sides cross: move a corner so they don't." : "";
        _problem.Text = problem;
        _problem.Visible = problem != "";
    }

    /// <summary>Whether a building is selected in the editor.</summary>
    public static bool Selected(Building building) =>
        EditorInterface.Singleton.GetSelection().GetSelectedNodes().Contains(building);

    /// <summary>The building selected in the scene, if one alone is.</summary>
    private static Building? SelectedBuilding()
    {
        var nodes = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        return nodes.Count == 1 && nodes[0] is Building building && IsInstanceValid(building) ? building : null;
    }

    /// <summary>
    /// Whether the building is still being worked on: selected, or something in it is (a props
    /// node under it, say: the storey it was cut open at kept, to put things in it).
    /// </summary>
    private static bool StillOn(Building? building)
    {
        if (building is null || !IsInstanceValid(building))
            return false;
        var nodes = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        return nodes.Count == 1 && (nodes[0] == building || building.IsAncestorOf(nodes[0]));
    }

    private void SelectionChanged()
    {
        foreach (var building in Building.InTree.Where(IsInstanceValid))
            building.UpdateGizmos();
        // After the editor has had its say about the new selection: the building selected worked on.
        CallDeferred(MethodName.Resync);
    }

    /// <summary>
    /// Delete with a door, window or detail selected: that taken away, not the building (Godot's own
    /// Delete, in the Scene dock, would come first). Not while typing in a field.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete } || _building is null || !IsInstanceValid(_building))
            return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or Tree)
            return;
        // Only with the building itself selected: with something in it selected instead (a prop
        // under it), Delete is for that, not for whatever of the building's was selected before.
        if (SelectedBuilding() != _building)
            return;
        if (SelectedInner is { } inner)
        {
            RemoveInner(_building, inner);
            GetViewport().SetInputAsHandled();
        }
        else if (SelectedRoom is { } room)
        {
            RemoveRoom(_building, room);
            GetViewport().SetInputAsHandled();
        }
        else if (SelectedStairs is { } stairs)
        {
            RemoveStairs(_building, stairs);
            GetViewport().SetInputAsHandled();
        }
        else if (SelectedWall is { } wall)
        {
            RemoveWall(_building, wall);
            GetViewport().SetInputAsHandled();
        }
        else if (SelectedDetail is { } detail)
        {
            RemoveDetail(_building, detail);
            GetViewport().SetInputAsHandled();
        }
        else if (SelectedProjection is { } projection)
        {
            RemoveProjection(_building, projection);
            GetViewport().SetInputAsHandled();
        }
        else if (SelectedOpening is { } opening)
        {
            RemoveOpening(_building, opening);
            GetViewport().SetInputAsHandled();
        }
    }

    public override bool _Handles(GodotObject @object)
    {
        // Anything opened in the Inspector while a building is selected in the scene (its own door,
        // detail or balcony, or a resource in one of their settings): still that building's.
        // A building cluster, or anything in one but a building (Draw and Snap, to draw new buildings in it).
        return @object is Building || (@object is Resource && SelectedBuilding() is not null) || ClusterFor(@object) is not null;
    }

    public override void _Edit(GodotObject? @object)
    {
        // A resource opened in the Inspector (or nothing, for a moment) while the building is still
        // selected: carry on with it.
        if (@object is Resource || (@object is null && (StillOn(_building) || StillOnCluster(_cluster))))
            return;
        // A cluster (or something in one that isn't in a building): Draw and Snap, for new buildings in it.
        if (ClusterFor(@object) is { } cluster)
        {
            EditCluster(cluster);
            return;
        }

        // Something in a building (a prop under it): still that building.
        if (@object is Node inside and not Building && FindBuilding(inside) == _building && _building is not null)
            return;
        var building = @object as Building;
        _cluster = null;
        ToolbarFor(false);
        // Called again for the same building (as the editor refreshes): drawing carries on as it was.
        if (building == _building)
            return;
        // A new building with no outline yet: straight into drawing it (the one before finished first).
        SetDrawing(false);
        StopPlacing();
        Select(null);
        StopPlacingDetail();
        SelectDetail(null);
        StopPlacingProjection();
        SelectProjection(null);
        LeaveInterior();
        _building = building;
        _building?.UpdateGizmos();
        if (_building is not null)
            FillDetailDock();
        // Just made from a cluster, its first corner down: drawn on from there.
        SetDrawing(_building is { Outline.Length: 0 } || (_building is not null && _building == _drawNew));
        _drawNew = null;
    }

    public override void _MakeVisible(bool visible)
    {
        // Hidden while its building is still selected (the Inspector on something else): kept on.
        if (!visible && (StillOn(_building) || StillOnCluster(_cluster)))
            return;
        if (!visible)
        {
            _cluster = null;
            ToolbarFor(false);
        }

        if (_toolbar is not null)
            _toolbar.Visible = visible;
        ShowDetailDock(visible);
        if (!visible)
        {
            SetDrawing(false);
            StopPlacing();
            Select(null);
            SelectDetail(null);
            StopPlacingProjection();
            SelectProjection(null);
            LeaveInterior();
        }
    }

    /// <summary>Whether corners are being drawn.</summary>
    public bool Drawing => _draw?.ButtonPressed == true;

    /// <summary>Where the next corner would go, while drawing, in the building's own space.</summary>
    public Vector2? Next { get; private set; }

    private void SetDrawing(bool on)
    {
        if (on)
        {
            StopPlacing();
            StopPlacingDetail();
            StopPlacingProjection();
            StopWalls();
            StopStairs();
            StopRooms();
            StopInner();
        }

        if (on && _side is not null)
            _side.SetPressedNoSignal(false);
        if (_draw is not null)
            _draw.SetPressedNoSignal(on);
        DrawToggled(on);
    }

    private void DrawToggled(bool on)
    {
        Guides.Clear();
        // From the last corner, until the mouse moves.
        Next = on && _building is { Outline.Length: > 0 } b ? b.Outline[^1] : null;
        // However drawing ends (the button, a key, a click, the building deselected): its origin to its middle.
        if (!on && _drawingWas && _building is not null && IsInstanceValid(_building))
            CentreOrigin(_building);
        _drawingWas = on;
        _building?.UpdateGizmos();
    }

    /// <summary>Whether drawing was on, to know when it ends.</summary>
    private bool _drawingWas;

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent @event)
    {
        Forwarded(@event);
        // Another tool at work in the view (props being put in it, say): its clicks, not these.
        if (BuildingsLinks.Busy?.Invoke() == true)
            return (int)AfterGuiInput.Pass;

        // A cluster: Draw makes new buildings in it.
        if (_building is null && _cluster is not null && IsInstanceValid(_cluster))
            return ClusterInput(camera, @event);

        // Not told which building again (the scripts reloaded with one selected): the one selected.
        if (_building is null || !IsInstanceValid(_building))
        {
            _building = EditorInterface.Singleton.GetSelection().GetSelectedNodes().OfType<Building>().FirstOrDefault();
            if (_building is null)
                return (int)AfterGuiInput.Pass;
            if (_toolbar is not null)
                _toolbar.Visible = true;
        }

        if (!Drawing && HandleInput(camera, @event))
            return (int)AfterGuiInput.Stop;

        if (Drawing)
            return Draw(camera, @event);

        if (_building.CutStorey >= 0 && _building.Enterable && RoomsInput(camera, @event))
            return (int)AfterGuiInput.Stop;

        if (_building.CutStorey >= 0 && _building.Enterable && StairsInput(camera, @event))
            return (int)AfterGuiInput.Stop;

        if (_building.CutStorey >= 0 && _building.Enterable && InnerOpeningInput(camera, @event))
            return (int)AfterGuiInput.Stop;

        if (InteriorInput(camera, @event))
            return (int)AfterGuiInput.Stop;

        if (ProjectionInput(camera, @event) || DetailInput(camera, @event) || OpeningInput(camera, @event))
            return (int)AfterGuiInput.Stop;

        if (PickingSide)
        {
            switch (@event)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pick:
                    if (NearestSide(camera, pick.Position) is { } side)
                    {
                        var undo = GetUndoRedo();
                        undo.CreateAction("Set roof side", customContext: _building);
                        undo.AddDoProperty(_building, Building.PropertyName.RoofSide, side);
                        undo.AddUndoProperty(_building, Building.PropertyName.RoofSide, _building.RoofSide);
                        undo.CommitAction();
                        _side!.ButtonPressed = false;
                    }

                    return (int)AfterGuiInput.Stop;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    _side!.ButtonPressed = false;
                    return (int)AfterGuiInput.Stop;
            }
        }

        // Ctrl-click on a corner: taken away.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, CtrlPressed: true } click
            && Nearest(camera, click.Position) is { } corner && _building.Outline.Length > 3)
        {
            var outline = _building.Outline.ToList();
            outline.RemoveAt(corner);
            SetOutline("Remove building corner", [.. outline], sides: Removed(corner));
            return (int)AfterGuiInput.Stop;
        }

        return (int)AfterGuiInput.Pass;
    }

    private int Draw(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        switch (@event)
        {
            case InputEventMouseMotion motion:
                Next = DrawPoint(building, camera, motion.Position, motion.ShiftPressed) ?? Next;
                building.UpdateGizmos();
                return (int)AfterGuiInput.Pass;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                // On its first corner: done.
                if (building.Outline.Length >= 3 && Nearest(camera, click.Position) == 0)
                {
                    SetDrawing(false);
                    return (int)AfterGuiInput.Stop;
                }

                if (DrawPoint(building, camera, click.Position, click.ShiftPressed) is { } point)
                {
                    SetOutline("Add building corner", [.. building.Outline, point]);
                    Next = point;
                }

                return (int)AfterGuiInput.Stop;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
            case InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Escape }:
                SetDrawing(false);
                return (int)AfterGuiInput.Stop;
        }

        return (int)AfterGuiInput.Pass;
    }

    /// <summary>Drawing ended: the building's origin moved to the middle of its outline, so the move and turn tool is on it.</summary>
    private static void CentreOrigin(Building building) => building.CentreOrigin();

    /// <summary>
    /// Where a corner drawn at a place in the view goes, in the building's own space: the first on
    /// the ground; the rest on the level of its floor, where its corners and the line to the mouse
    /// are shown (so they stay under the mouse, and there is always somewhere). On the grid with Shift.
    /// </summary>
    private Vector2? DrawPoint(Building building, Camera3D camera, Vector2 screen, bool snap)
    {
        Vector3? world;
        if (building.Outline.Length == 0)
        {
            world = GroundPoint(building, camera, screen);
        }
        else
        {
            var floor = new Plane(building.GlobalBasis.Y.Normalized(), building.ToGlobal(new Vector3(0f, building.FloorHeight, 0f)));
            world = floor.IntersectsRay(camera.ProjectRayOrigin(screen), camera.ProjectRayNormal(screen));
        }

        if (world is not { } at)
            return null;
        // Joined to the last corner, and (closing it) the first.
        var n = building.Outline.Length;
        return Place(building, at, snap, [(n - 1, n - 2), (0, 1)], camera, screen);
    }

    /// <summary>The building's corner nearest a place in the view, if one is within reach.</summary>
    private int? Nearest(Camera3D camera, Vector2 screen)
    {
        var building = _building!;
        int? best = null;
        var reach = ClickReach;
        for (var i = 0; i < building.Outline.Length; i++)
        {
            var c = building.Outline[i];
            var world = building.ToGlobal(new Vector3(c.X, building.FloorHeight, c.Y));
            if (camera.IsPositionBehind(world))
                continue;
            var d = camera.UnprojectPosition(world).DistanceTo(screen);
            if (d < reach)
            {
                reach = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The building's side nearest a place in the view (along the top of its walls), if one is within reach.</summary>
    private int? NearestSide(Camera3D camera, Vector2 screen)
    {
        var building = _building!;
        var outline = building.Outline;
        int? best = null;
        var reach = ClickReach * 2f;
        for (var i = 0; i < outline.Length; i++)
        {
            var a = building.ToGlobal(new Vector3(outline[i].X, building.TopHeight, outline[i].Y));
            var b = building.ToGlobal(new Vector3(outline[(i + 1) % outline.Length].X, building.TopHeight, outline[(i + 1) % outline.Length].Y));
            if (camera.IsPositionBehind(a) || camera.IsPositionBehind(b))
                continue;
            var sa = camera.UnprojectPosition(a);
            var sb = camera.UnprojectPosition(b);
            var d = Geometry2D.GetClosestPointToSegment(screen, sa, sb).DistanceTo(screen);
            if (d < reach)
            {
                reach = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Sets a building's outline as a step to undo.</summary>
    /// <param name="sides">
    /// With corners added or taken away: where each old side's doors and windows (by their side and
    /// how far along it they start; and the roof's side) go: to which side now, and how much further along it; none if they go with it.
    /// </param>
    public void SetOutline(string action, Vector2[] outline, Vector2[]? was = null, Building? of = null, System.Func<int, float, (int Side, float Shift)?>? sides = null)
    {
        var building = of ?? _building!;
        var undo = GetUndoRedo();
        undo.CreateAction(action, customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Outline, outline);
        undo.AddUndoProperty(building, Building.PropertyName.Outline, was ?? building.Outline);
        if (sides is not null)
        {
            // Copies moved to their sides now, the old ones kept as they were for undoing.
            var openings = new Godot.Collections.Array<BuildingOpening>();
            foreach (var o in building.Openings.Where(o => o is not null))
            {
                if (sides(o.Side, o.From) is not { } to)
                    continue;
                var moved = (BuildingOpening)o.Duplicate();
                moved.Side = to.Side;
                moved.From = o.From + to.Shift;
                openings.Add(moved);
            }

            undo.AddDoProperty(building, Building.PropertyName.Openings, openings);
            undo.AddUndoProperty(building, Building.PropertyName.Openings, building.Openings);
            var details = new Godot.Collections.Array<WallDetail>();
            foreach (var d in building.Details.Where(d => d is not null))
            {
                if (sides(d.Side, d.Along) is not { } to)
                    continue;
                var moved = (WallDetail)d.Duplicate();
                moved.Side = to.Side;
                moved.Along = d.Along + to.Shift;
                details.Add(moved);
            }

            undo.AddDoProperty(building, Building.PropertyName.Details, details);
            undo.AddUndoProperty(building, Building.PropertyName.Details, building.Details);
            SelectDetail(null);
            var projections = new Godot.Collections.Array<BuildingProjection>();
            foreach (var p in building.Projections.Where(p => p is not null))
            {
                if (sides(p.Side, p.From) is not { } to)
                    continue;
                var moved = (BuildingProjection)p.Duplicate();
                moved.Side = to.Side;
                moved.From = p.From + to.Shift;
                projections.Add(moved);
            }

            undo.AddDoProperty(building, Building.PropertyName.Projections, projections);
            undo.AddUndoProperty(building, Building.PropertyName.Projections, building.Projections);
            SelectProjection(null);
            var roof = building.RoofSide >= 0 && sides(building.RoofSide, 0f) is { } r ? r.Side : -1;
            undo.AddDoProperty(building, Building.PropertyName.RoofSide, roof);
            undo.AddUndoProperty(building, Building.PropertyName.RoofSide, building.RoofSide);
            Select(null);
        }

        undo.CommitAction();
    }

    /// <summary>Where sides go with corner k taken away: the side starting at it goes (its doors and windows with it), those after it come down one.</summary>
    public static System.Func<int, float, (int, float)?> Removed(int k) => (side, _) => side == k ? null : (side > k ? side - 1 : side, 0f);

    /// <summary>
    /// Where sides go with a corner put in at k, splitting the side before it a length along: what
    /// was on that side past the new corner goes to the new side after it; those after it go up one.
    /// </summary>
    public static System.Func<int, float, (int, float)?> Inserted(int k, float split) => (side, from) =>
        side < k - 1 ? (side, 0f)
        : side > k - 1 ? (side + 1, 0f)
        : from >= split ? (k, -split) : (side, 0f);

    /// <summary>How far apart the grid's lines are, in metres (corners drawn with Shift held snap to them).</summary>
    public const float GridStep = 1f;

    /// <summary>A corner on the grid if Shift is held (the building's own: in its space, from its origin).</summary>
    public static Vector2 Snapped(Vector2 at, bool snap) => snap ? at.Snapped(new Vector2(GridStep, GridStep)) : at;

    /// <summary>The building being edited, if any.</summary>
    public Building? Edited => _building;

    /// <summary>A place in its own space on the ground, as an outline holds it.</summary>
    public static Vector2 Flat(Vector3 local) => new(local.X, local.Z);

    /// <summary>
    /// Where the ground is under a place in the view, in the world: as the project says (see
    /// <see cref="BuildingsLinks.Ground"/>), else anything solid, else the level of the building's own origin.
    /// </summary>
    public static Vector3? GroundPoint(Building building, Camera3D camera, Vector2 screen)
    {
        var from = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        if (building.SceneRoot() is { } root && BuildingsLinks.Ground?.Invoke(root, from, direction) is { } ground)
            return ground;
        var hit = building.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + direction * 4000f));
        if (hit.Count > 0)
            return hit["position"].AsVector3();
        var plane = new Plane(Vector3.Up, building.GlobalPosition.Y);
        return plane.IntersectsRay(from, direction);
    }
}
#endif
