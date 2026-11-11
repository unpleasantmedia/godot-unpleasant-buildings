#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Building clusters: with one (or anything in it but a building) selected, the toolbar shows Draw
/// and Snap only; clicking the ground with Draw on makes a new building under it, its first corner
/// there, and drawing goes on in it as in any building.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    /// <summary>The cluster worked on, when one (or something in it but a building) is selected.</summary>
    private BuildingCluster? _cluster;

    /// <summary>A building just made from a cluster: drawn on as soon as it is selected.</summary>
    private Building? _drawNew;

    /// <summary>The toolbar's controls hidden for a cluster (all but Draw and Snap), to show again for a building.</summary>
    private readonly List<Control> _hiddenForCluster = [];

    /// <summary>The cluster a node selected is worked on as: one, or something in one that is not in a building.</summary>
    private static BuildingCluster? ClusterFor(GodotObject? @object) =>
        @object is Node node && node is not Building && FindBuilding(node) is null ? BuildingCluster.Of(node) : null;

    /// <summary>The building a node is in (itself, or the nearest above it), if any.</summary>
    private static Building? FindBuilding(Node? node)
    {
        for (var at = node; at is not null; at = at.GetParent())
            if (at is Building building)
                return building;
        return null;
    }

    /// <summary>Whether the cluster is still being worked on: it, or something in it but a building, alone selected.</summary>
    private static bool StillOnCluster(BuildingCluster? cluster)
    {
        if (cluster is null || !IsInstanceValid(cluster))
            return false;
        var nodes = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        return nodes.Count == 1 && ClusterFor(nodes[0]) == cluster;
    }

    /// <summary>The toolbar for a cluster (Draw and Snap only) or for a building (all of it, as it shows itself).</summary>
    private void ToolbarFor(bool cluster)
    {
        if (_toolbar is null)
            return;
        if (cluster)
        {
            foreach (var control in _toolbar.GetChildren().OfType<Control>())
            {
                if (control == _draw || control == _snap || control == _afterSnap || (control is VSeparator && control.GetIndex() == 0) || !control.Visible)
                    continue;
                control.Visible = false;
                _hiddenForCluster.Add(control);
            }
        }
        else
        {
            foreach (var control in _hiddenForCluster.Where(IsInstanceValid))
                control.Visible = true;
            _hiddenForCluster.Clear();
        }

        // The splitter after Draw and Snap: only with a building's tools after it.
        if (_afterSnap is not null)
            _afterSnap.Visible = !cluster;
    }

    /// <summary>A cluster (or something in it) to work on: no building any more, its toolbar shown.</summary>
    private void EditCluster(BuildingCluster cluster)
    {
        if (_building is not null)
        {
            SetDrawing(false);
            StopPlacing();
            Select(null);
            StopPlacingDetail();
            SelectDetail(null);
            StopPlacingProjection();
            SelectProjection(null);
            LeaveInterior();
            _building = null;
        }

        _cluster = cluster;
        ToolbarFor(true);
        if (_toolbar is not null)
            _toolbar.Visible = true;
        ShowDetailDock(false);
    }

    /// <summary>The view's clicks for a cluster: with Draw on, a click on the ground makes a building there and goes on drawing it.</summary>
    private int ClusterInput(Camera3D camera, InputEvent @event)
    {
        if (!Drawing || _cluster is not { } cluster || !IsInstanceValid(cluster))
            return (int)AfterGuiInput.Pass;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                if (GroundUnder(cluster, camera, click.Position) is { } at)
                    NewBuilding(cluster, at, click.ShiftPressed, camera, click.Position);
                return (int)AfterGuiInput.Stop;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
            case InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Escape }:
                SetDrawing(false);
                return (int)AfterGuiInput.Stop;
        }

        return (int)AfterGuiInput.Pass;
    }

    /// <summary>
    /// A new building under the cluster, its origin where the ground was clicked (one step to undo),
    /// then its first corner there (snapping as any corner drawn), selected, and drawn on.
    /// </summary>
    private void NewBuilding(BuildingCluster cluster, Vector3 at, bool shift, Camera3D camera, Vector2 screen)
    {
        if (EditorInterface.Singleton.GetEditedSceneRoot() is not { } root)
            return;
        var building = new Building { Name = "Building" };
        var undo = GetUndoRedo();
        undo.CreateAction("Add building", UndoRedo.MergeMode.Disable, root);
        undo.AddDoMethod(cluster, Node.MethodName.AddChild, building, true);
        undo.AddDoProperty(building, Node.PropertyName.Owner, root);
        undo.AddDoProperty(building, Node3D.PropertyName.GlobalPosition, at);
        undo.AddDoReference(building);
        undo.AddUndoMethod(cluster, Node.MethodName.RemoveChild, building);
        undo.CommitAction();

        // Its first corner: as drawing a building's first corner (snapped to the others, or the grid).
        var first = Place(building, at, shift, [], camera, screen);
        building.Outline = [first];
        _drawNew = building;
        EditorInterface.Singleton.EditNode(building);
    }

    /// <summary>Where the ground is under a place in the view, for a cluster: as for a building (see <see cref="GroundPoint"/>), else the cluster's own level.</summary>
    private static Vector3? GroundUnder(BuildingCluster cluster, Camera3D camera, Vector2 screen)
    {
        var from = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        if (EditorInterface.Singleton.GetEditedSceneRoot() is { } root && BuildingsLinks.Ground?.Invoke(root, from, direction) is { } ground)
            return ground;
        var hit = cluster.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + direction * 4000f));
        if (hit.Count > 0)
            return hit["position"].AsVector3();
        return new Plane(Vector3.Up, cluster.GlobalPosition.Y).IntersectsRay(from, direction);
    }
}
#endif
