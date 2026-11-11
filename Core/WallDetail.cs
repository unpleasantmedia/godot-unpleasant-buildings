using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A detail on one of a building's walls: of a <see cref="WallDetailType"/>, on a side (its number
/// in the outline), on a storey, its origin some way along the side from its start and some way up
/// from its storey's floor. Lengths are in metres, set in 10 cm steps in the 3D view.
/// </summary>
[Tool]
[GlobalClass]
public partial class WallDetail : Resource
{
    private WallDetailType? _type;
    private int _side;
    private int _storey;
    private float _along = 1f;
    private float _up = 1f;
    private float _width = 1f;
    private float _height = 3f;

    [Export]
    public WallDetailType? Type
    {
        get => _type;
        set
        {
            Set(ref _type, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>Which side it is on: the side from the outline's corner of this number to the next.</summary>
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

    /// <summary>How far along its side its origin is, from the side's start, in metres.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public float Along
    {
        get => _along;
        set => Set(ref _along, value);
    }

    /// <summary>How far up from its storey's floor its origin is, in metres.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public float Up
    {
        get => _up;
        set => Set(ref _up, value);
    }

    /// <summary>For a type that can be made wider: how wide, in metres.</summary>
    [Export(PropertyHint.Range, "0.1,50,0.1,suffix:m")]
    public float Width
    {
        get => _width;
        set => Set(ref _width, Mathf.Max(0.1f, value));
    }

    /// <summary>For a type that can be made taller: how tall, in metres.</summary>
    [Export(PropertyHint.Range, "0.1,100,0.1,suffix:m")]
    public float Height
    {
        get => _height;
        set => Set(ref _height, Mathf.Max(0.1f, value));
    }

    /// <summary>In the Inspector: its width and height only where its type's scene takes them.</summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        var name = property["name"].AsStringName();
        if ((name == PropertyName.Width && string.IsNullOrEmpty(_type?.WidthProperty)) || (name == PropertyName.Height && string.IsNullOrEmpty(_type?.HeightProperty)))
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
