#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>A building's parts beside its main one (see <see cref="BuildingWing"/>): outlined from their floor to their top, with their corners to drag.</summary>
public partial class BuildingGizmos
{
    /// <summary>
    /// The ids of the parts' handles start here: part i's corner c is this plus i times
    /// <see cref="PartStride"/> plus c; the middle of its side s, that plus <see cref="PartMiddles"/> plus s.
    /// </summary>
    private const int PartHandleIds = 1_000_000;

    private const int PartStride = 10_000;
    private const int PartMiddles = 5_000;

    /// <summary>A handle's part, and its corner (or, a side's middle, the side), if it is a part's.</summary>
    private static (int Part, int Index, bool Middle)? PartHandle(int id)
    {
        if (id < PartHandleIds)
            return null;
        var rest = id - PartHandleIds;
        var index = rest % PartStride;
        return (rest / PartStride, index % PartMiddles, index >= PartMiddles);
    }

    /// <summary>Its parts: each outlined from its floor up to its top, and (its own handles shown) its corners and sides' middles.</summary>
    private void Parts(EditorNode3DGizmo gizmo, Building building, bool own)
    {
        Vector3 At(Vector2 p, float y) => new(p.X, y, p.Y);
        var lines = new List<Vector3>();
        var loose = building.LooseParts().ToHashSet();
        var looseLines = new List<Vector3>();
        for (var i = 0; i < building.Parts.Count; i++)
        {
            if (building.Parts[i] is not { } part)
                continue;
            var outline = part.Outline;
            var floor = building.StoreyFloor(part.Floor);
            var top = building.StoreyFloor(part.Top);
            var drawing = _plugin is { DrawingPart: true } p && p.DrawnPart == i;
            var closed = outline.Length >= 3 && !drawing;
            var sides = closed ? outline.Length : outline.Length - 1;
            var into = loose.Contains(i) ? looseLines : lines;
            for (var s = 0; s < sides; s++)
            {
                var a = outline[s];
                var b = outline[(s + 1) % outline.Length];
                into.AddRange([At(a, floor), At(b, floor)]);
                if (closed)
                    into.AddRange([At(a, top), At(b, top)]);
            }

            if (closed)
            {
                foreach (var c in outline)
                    into.AddRange([At(c, floor), At(c, top)]);
            }

            // While drawing it: from its last corner to the mouse, and on to its first.
            if (drawing && _plugin!.Next is { } next && outline.Length > 0)
            {
                var preview = new List<Vector3> { At(outline[^1], floor), At(next, floor) };
                if (outline.Length >= 2)
                    preview.AddRange([At(next, floor), At(outline[0], floor)]);
                gizmo.AddLines([.. preview], GetMaterial("next", gizmo));
            }

            if (!own || outline.Length == 0)
                continue;
            var first = PartHandleIds + i * PartStride;
            AddHandles(gizmo, [.. outline.Select(c => At(c, floor))], GetMaterial("corners", gizmo), [.. Enumerable.Range(first, outline.Length)]);
            if (closed)
            {
                var middles = Enumerable.Range(0, outline.Length).Select(s => At((outline[s] + outline[(s + 1) % outline.Length]) / 2f, floor)).ToArray();
                AddHandles(gizmo, middles, GetMaterial("middles", gizmo), [.. Enumerable.Range(first + PartMiddles, outline.Length)]);
            }
        }

        if (lines.Count > 0)
            gizmo.AddLines([.. lines], GetMaterial("lines", gizmo));
        // Red while it isn't attached to the rest.
        if (looseLines.Count > 0)
            gizmo.AddLines([.. looseLines], GetMaterial("crossed", gizmo));
    }

    /// <summary>A part's corner dragged (or a new one, from a side's middle): on the level of its floor, so it stays under the mouse.</summary>
    private void DragPart(Building building, int part, int index, bool middle, Camera3D camera, Vector2 point)
    {
        if (part >= building.Parts.Count || building.Parts[part] is not { } wing)
            return;
        var level = new Plane(building.GlobalBasis.Y.Normalized(), building.ToGlobal(new Vector3(0f, building.StoreyFloor(wing.Floor), 0f)));
        if (level.IntersectsRay(camera.ProjectRayOrigin(point), camera.ProjectRayNormal(point)) is not { } world)
            return;
        var outline = wing.Outline.ToList();
        if (middle)
        {
            if (_added is null)
            {
                _added = index + 1;
                outline.Insert(_added.Value, UnpleasantBuildingsPlugin.Flat(building.ToLocal(world)));
            }

            index = _added.Value;
        }

        if (index >= outline.Count)
            return;
        outline[index] = _plugin?.PlacePartCorner(building, world, part, Input.IsKeyPressed(Key.Shift), camera, point)
            ?? UnpleasantBuildingsPlugin.Snapped(building, UnpleasantBuildingsPlugin.Flat(building.ToLocal(world)), Input.IsKeyPressed(Key.Shift));
        wing.Outline = [.. outline];
    }

    /// <summary>A part's corner let go of: one step to undo (or put back, cancelled).</summary>
    private void CommitPart(Building building, int part, bool middle, Variant restore, bool cancel)
    {
        var added = _added;
        _added = null;
        _plugin?.Guides.Clear();
        if (part >= building.Parts.Count || building.Parts[part] is not { } wing)
            return;
        var was = restore.AsVector2Array();
        var now = wing.Outline;
        wing.Outline = was;
        if (cancel)
            return;
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction(middle ? "Add part corner" : "Move part corner", UndoRedo.MergeMode.Disable, building);
        undo.AddDoProperty(wing, BuildingWing.PropertyName.Outline, now);
        undo.AddUndoProperty(wing, BuildingWing.PropertyName.Outline, was);
        // A corner put in: what is on the walls after it numbered on one (see Building.WallSide), and
        // what was on the side it split, past the new corner, onto the new side after it.
        if (middle && added is { } k && k > 0 && k < now.Length)
            _plugin?.Renumber(undo, building, UnpleasantBuildingsPlugin.Inserted(building.FirstSide(part) + k, now[k - 1].DistanceTo(now[k])));
        undo.CommitAction();
    }
}
#endif
