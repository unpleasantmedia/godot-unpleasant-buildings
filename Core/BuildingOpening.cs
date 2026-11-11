using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>What an opening in a wall is.</summary>
public enum OpeningKind
{
    Window,
    Door,
}

/// <summary>What fills a doorway.</summary>
public enum DoorLeaves
{
    /// <summary>One door.</summary>
    Single,

    /// <summary>Two doors side by side, meeting in the middle.</summary>
    Double,

    /// <summary>No door: a framed gap to walk through.</summary>
    None,
}

/// <summary>
/// A door or window in one of a building's walls: on a side (its number in the outline), on a
/// storey, some way along the side from its start, with a width and height (and, for a window, a
/// sill height above its storey's floor). Lengths are in metres, and set in 10 cm steps in the 3D view.
/// </summary>
[Tool]
[GlobalClass]
public partial class BuildingOpening : Resource
{
    private OpeningKind _kind;
    private int _side;
    private int _storey;
    private bool _allStoreys = true;
    private float _from = 1f;
    private float _width = 1.2f;
    private float _height = 1.4f;
    private float _sill = 0.9f;
    private Vector2I _panes = new(2, 1);
    private DoorLeaves _leaves;
    private bool _glazed;

    [Export]
    public OpeningKind Kind
    {
        get => _kind;
        set
        {
            Set(ref _kind, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>Which side it is in: the side from the outline's corner of this number to the next.</summary>
    [Export]
    public int Side
    {
        get => _side;
        set => Set(ref _side, value);
    }

    /// <summary>Which storey it is on, from 0 (the ground floor).</summary>
    [Export]
    public int Storey
    {
        get => _storey;
        set => Set(ref _storey, Mathf.Max(0, value));
    }

    /// <summary>For a window: on every storey from its own up, not just its own.</summary>
    [Export]
    public bool AllStoreys
    {
        get => _allStoreys;
        set => Set(ref _allStoreys, value);
    }

    /// <summary>How far along its side its left edge is (seen from outside), from the side's start, in metres.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public float From
    {
        get => _from;
        set => Set(ref _from, value);
    }

    [Export(PropertyHint.Range, "0.2,10,0.1,suffix:m")]
    public float Width
    {
        get => _width;
        set => Set(ref _width, Mathf.Max(0.2f, value));
    }

    [Export(PropertyHint.Range, "0.2,10,0.1,suffix:m")]
    public float Height
    {
        get => _height;
        set => Set(ref _height, Mathf.Max(0.2f, value));
    }

    /// <summary>For a window: how far its bottom is above its storey's floor, in metres (a door's is 0).</summary>
    [Export(PropertyHint.Range, "0,5,0.1,suffix:m")]
    public float Sill
    {
        get => _sill;
        set => Set(ref _sill, Mathf.Max(0f, value));
    }

    /// <summary>For a window: how many panes it has across and up.</summary>
    [Export]
    public Vector2I Panes
    {
        get => _panes;
        set => Set(ref _panes, new Vector2I(Mathf.Clamp(value.X, 1, 12), Mathf.Clamp(value.Y, 1, 12)));
    }

    /// <summary>For a door: one, two, or none (a gap).</summary>
    [Export]
    public DoorLeaves Leaves
    {
        get => _leaves;
        set => Set(ref _leaves, value);
    }

    private bool _hingeRight;
    private bool _opensOut;

    /// <summary>For a single door: hung on its right (seen from outside) rather than its left.</summary>
    [Export]
    public bool HingeRight
    {
        get => _hingeRight;
        set => Set(ref _hingeRight, value);
    }

    /// <summary>For a door: swinging out (in the game) rather than in.</summary>
    [Export]
    public bool OpensOut
    {
        get => _opensOut;
        set => Set(ref _opensOut, value);
    }

    /// <summary>For a door: with a glass panel in it.</summary>
    [Export]
    public bool Glazed
    {
        get => _glazed;
        set => Set(ref _glazed, value);
    }

    /// <summary>How near a door or window may come to either end of its wall, in metres: the wall's thickness, and a little.</summary>
    public const float Margin = BuildingMesher.WallThickness + 0.05f;

    /// <summary>Its bottom above its storey's floor: a door's is on it.</summary>
    public float Bottom => _kind == OpeningKind.Door ? 0f : _sill;

    /// <summary>A new window or door of the usual size, its left edge at a place along a side.</summary>
    public static BuildingOpening Make(OpeningKind kind, int side, int storey, float from) => kind == OpeningKind.Door
        ? new BuildingOpening { _kind = kind, _side = side, _storey = storey, _from = from, _width = 1f, _height = 2.2f, _sill = 0f, _allStoreys = false }
        : new BuildingOpening { _kind = kind, _side = side, _storey = storey, _from = from };

    /// <summary>In the Inspector: a window's settings for windows, a door's for doors.</summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        var name = property["name"].AsStringName();
        var window = _kind == OpeningKind.Window;
        if ((!window && (name == PropertyName.Sill || name == PropertyName.AllStoreys || name == PropertyName.Panes))
            || (window && (name == PropertyName.Leaves || name == PropertyName.Glazed || name == PropertyName.HingeRight || name == PropertyName.OpensOut))
            || (name == PropertyName.HingeRight && _leaves != DoorLeaves.Single))
            Hide(property);
    }

    private static void Hide(Godot.Collections.Dictionary property)
    {
        property["usage"] = (int)((PropertyUsageFlags)property["usage"].AsInt32() & ~PropertyUsageFlags.Editor);
    }

    private void Set<T>(ref T field, T value)
    {
        field = value;
        EmitChanged();
    }
}
