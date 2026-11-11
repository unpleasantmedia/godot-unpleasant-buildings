using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A room inside an enterable building: the space on one storey closed off by the outer walls and
/// the storey's interior walls round <see cref="Seed"/> (where it was clicked, in the building's own
/// space, on the ground: X and Z as a Vector2's X and Y), so it follows the walls as they move. Its
/// floor, its ceiling and the faces of the walls facing into it can have their own materials (with
/// none, the building's). Made in the 3D view with Rooms, a storey picked.
/// </summary>
[Tool]
[GlobalClass]
public partial class Room : Resource
{
    private int _storey;
    private Vector2 _seed;
    private string _name = "";
    private Material? _wallMaterial;
    private Material? _floorMaterial;
    private Material? _ceilingMaterial;

    /// <summary>What it is called (shown in the editor; see <see cref="Building.RoomAt"/> in the game).</summary>
    [Export]
    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? "");
    }

    /// <summary>Which storey it is on, from 0 (the ground floor).</summary>
    [Export]
    public int Storey
    {
        get => _storey;
        set => Set(ref _storey, Mathf.Max(0, value));
    }

    /// <summary>A place inside it: the space round it, between the walls, is the room.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public Vector2 Seed
    {
        get => _seed;
        set => Set(ref _seed, value);
    }

    /// <summary>What the faces of the walls facing into it are made of; with none, the building's interior walls'.</summary>
    [Export]
    public Material? WallMaterial
    {
        get => _wallMaterial;
        set => Set(ref _wallMaterial, value);
    }

    /// <summary>What its floor is made of; with none, the building's floors'.</summary>
    [Export]
    public Material? FloorMaterial
    {
        get => _floorMaterial;
        set => Set(ref _floorMaterial, value);
    }

    /// <summary>What its ceiling is made of; with none, the building's ceilings'.</summary>
    [Export]
    public Material? CeilingMaterial
    {
        get => _ceilingMaterial;
        set => Set(ref _ceilingMaterial, value);
    }

    private void Set<T>(ref T field, T value)
    {
        field = value;
        EmitChanged();
    }
}
