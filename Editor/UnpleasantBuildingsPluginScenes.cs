#if TOOLS
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A building saved as a scene of its own (Save as Scene, in its Inspector), as Godot's Save Branch
/// as Scene does: it and everything under it written to a file (with its own copies of its doors,
/// windows, details, balconies, walls, stairs, rooms: none shared with the level it came from), and
/// an instance of that put in its place, where it was. One step to undo (the file stays).
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    private Building? _saving;
    private EditorFileDialog? _saveDialog;

    private void AddSceneSaver() => Building.SceneSaver = AskWhereToSave;

    private void RemoveSceneSaver()
    {
        Building.SceneSaver = null;
        _saveDialog?.QueueFree();
        _saveDialog = null;
    }

    /// <summary>Asks where to save a building as a scene, then saves it there.</summary>
    private void AskWhereToSave(Building building)
    {
        if (EditorInterface.Singleton.GetEditedSceneRoot() is not { } root || building == root)
        {
            GD.PushWarning("Save as Scene: the building must be in a scene (and not its root).");
            return;
        }

        _saving = building;
        if (_saveDialog is null || !IsInstanceValid(_saveDialog))
        {
            _saveDialog = new EditorFileDialog
            {
                FileMode = EditorFileDialog.FileModeEnum.SaveFile,
                Access = EditorFileDialog.AccessEnum.Resources,
                Title = "Save Building as Scene",
            };
            _saveDialog.AddFilter("*.tscn", "Scene");
            _saveDialog.Connect(EditorFileDialog.SignalName.FileSelected, new Callable(this, MethodName.SaveTo));
            EditorInterface.Singleton.GetBaseControl().AddChild(_saveDialog);
        }

        _saveDialog.CurrentFile = $"{building.Name}.tscn";
        _saveDialog.PopupFileDialog();
    }

    private void SaveTo(string path)
    {
        if (_saving is not { } building || !IsInstanceValid(building) || building.GetParent() is not { } parent
            || EditorInterface.Singleton.GetEditedSceneRoot() is not { } root)
            return;
        _saving = null;

        // A copy of it, at the scene's own origin, owning all under it, with its own copies of its parts.
        var copy = (Building)building.Duplicate((int)(DuplicateFlags.Signals | DuplicateFlags.Groups | DuplicateFlags.Scripts | DuplicateFlags.UseInstantiation));
        copy.Transform = Transform3D.Identity;
        copy.Openings = [.. building.Openings.Where(o => o is not null).Select(o => (BuildingOpening)o.Duplicate(true))];
        copy.Details = [.. building.Details.Where(d => d is not null).Select(d => (WallDetail)d.Duplicate(true))];
        copy.Projections = [.. building.Projections.Where(p => p is not null).Select(p => (BuildingProjection)p.Duplicate(true))];
        copy.Walls = [.. building.Walls.Where(w => w is not null).Select(w => (InteriorWall)w.Duplicate(true))];
        copy.Stairs = [.. building.Stairs.Where(s => s is not null).Select(s => (Staircase)s.Duplicate(true))];
        copy.Rooms = [.. building.Rooms.Where(r => r is not null).Select(r => (Room)r.Duplicate(true))];
        // Owned by it (to be saved with it): all under it but the insides of instances (a prop's
        // model), which their instance owns still.
        foreach (var inside in copy.FindChildren("*", "", true, false))
            if (inside.Owner is null || (inside.Owner != copy && !copy.IsAncestorOf(inside.Owner)))
                inside.Owner = copy;
        var packed = new PackedScene();
        var packing = packed.Pack(copy);
        copy.Free();
        if (packing != Error.Ok || ResourceSaver.Save(packed, path) != Error.Ok)
        {
            GD.PushError($"Save as Scene: could not save {path}.");
            return;
        }

        EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(path);
        // Its instance in its place: where it was, as it was called, at its place among the others.
        var scene = ResourceLoader.Load<PackedScene>(path, null, ResourceLoader.CacheMode.Replace);
        if (scene?.Instantiate(PackedScene.GenEditState.Instance) is not Node3D instance)
            return;
        instance.Name = building.Name;
        instance.Transform = building.Transform;
        var index = building.GetIndex();
        var undo = GetUndoRedo();
        undo.CreateAction($"Save {building.Name} as Scene", UndoRedo.MergeMode.Disable, root);
        undo.AddDoMethod(parent, Node.MethodName.RemoveChild, building);
        undo.AddDoMethod(parent, Node.MethodName.AddChild, instance, true);
        undo.AddDoMethod(parent, Node.MethodName.MoveChild, instance, index);
        undo.AddDoProperty(instance, Node.PropertyName.Owner, root);
        undo.AddDoReference(instance);
        undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, instance);
        undo.AddUndoMethod(parent, Node.MethodName.AddChild, building, true);
        undo.AddUndoMethod(parent, Node.MethodName.MoveChild, building, index);
        undo.AddUndoProperty(building, Node.PropertyName.Owner, building.Owner);
        undo.AddUndoReference(building);
        undo.CommitAction();
        EditorInterface.Singleton.EditNode(instance);
        GD.Print($"Save as Scene: {building.Name} saved as {path}, an instance of it in its place.");
    }
}
#endif
