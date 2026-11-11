using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>What sticks out of a wall.</summary>
public enum ProjectionKind
{
    Balcony,
    Awning,

    /// <summary>On the ground storey: a deck, posts and a roof over it, with steps, a railing and a skirt.</summary>
    Porch,
}

/// <summary>A porch's roof.</summary>
public enum PorchRoofStyle
{
    /// <summary>A lean-to, sloping down and away from the wall.</summary>
    Shed,

    /// <summary>Flat.</summary>
    Flat,
}

/// <summary>A balcony's railing.</summary>
public enum RailingStyle
{
    /// <summary>Upright bars between a top and a bottom rail.</summary>
    Bars,

    /// <summary>A low solid wall.</summary>
    Solid,

    /// <summary>Glass panels under a top rail, with a post at each corner.</summary>
    Glass,
}

/// <summary>An awning's shape.</summary>
public enum AwningStyle
{
    /// <summary>A canvas sloping down and out from the wall, with a flap at its front.</summary>
    Sloped,

    /// <summary>A thin flat canopy (a shop front's).</summary>
    Flat,
}

/// <summary>
/// A balcony or awning on one of a building's walls, made by the building: on a side (its number in
/// the outline), on a storey, its left edge some way along the side from its start, as wide as set
/// and reaching out as far as set. A balcony's slab top is <see cref="Up"/> above its storey's floor;
/// an awning's top, where it meets the wall, is. Lengths are in metres, set in 10 cm steps in the 3D view.
/// </summary>
[Tool]
[GlobalClass]
public partial class BuildingProjection : Resource
{
    private ProjectionKind _kind;
    private int _side;
    private int _storey = 1;
    private bool _allStoreys = true;
    private float _from = 1f;
    private float _width = 2f;
    private float _depth = 1.2f;
    private float _up;
    private RailingStyle _railing;
    private float _railingHeight = 1f;
    private AwningStyle _awningStyle;
    private bool _cheeks = true;
    private float _drop = 0.6f;
    private PorchRoofStyle _porchRoof;
    private float _roofHeight = 2.6f;
    private bool _hasRoof = true;
    private bool _hasRailing = true;
    private bool _steps = true;
    private float _stepsFrom = 0.9f;
    private float _stepsWidth = 1.2f;
    private bool _skirt = true;

    [Export]
    public ProjectionKind Kind
    {
        get => _kind;
        set
        {
            Set(ref _kind, value);
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

    /// <summary>For a balcony: on every storey from its own up, not just its own.</summary>
    [Export]
    public bool AllStoreys
    {
        get => _allStoreys;
        set => Set(ref _allStoreys, value);
    }

    /// <summary>How far along its side its left edge is, from the side's start, in metres.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public float From
    {
        get => _from;
        set => Set(ref _from, value);
    }

    [Export(PropertyHint.Range, "0.3,30,0.1,suffix:m")]
    public float Width
    {
        get => _width;
        set => Set(ref _width, Mathf.Max(0.3f, value));
    }

    /// <summary>How far out of the wall it reaches, in metres.</summary>
    [Export(PropertyHint.Range, "0.2,10,0.1,suffix:m")]
    public float Depth
    {
        get => _depth;
        set => Set(ref _depth, Mathf.Max(0.2f, value));
    }

    /// <summary>How far above its storey's floor it is, in metres: a balcony's slab top, an awning's top at the wall.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public float Up
    {
        get => _up;
        set => Set(ref _up, value);
    }

    [Export]
    public RailingStyle Railing
    {
        get => _railing;
        set => Set(ref _railing, value);
    }

    [Export(PropertyHint.Range, "0.3,2,0.1,suffix:m")]
    public float RailingHeight
    {
        get => _railingHeight;
        set => Set(ref _railingHeight, Mathf.Max(0.3f, value));
    }

    [Export]
    public AwningStyle AwningStyle
    {
        get => _awningStyle;
        set
        {
            Set(ref _awningStyle, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>For a sloped awning: closed sides.</summary>
    [Export]
    public bool Cheeks
    {
        get => _cheeks;
        set => Set(ref _cheeks, value);
    }

    /// <summary>For a sloped awning: how far its front edge is below its top at the wall, in metres.</summary>
    [Export(PropertyHint.Range, "0,3,0.1,suffix:m")]
    public float Drop
    {
        get => _drop;
        set => Set(ref _drop, Mathf.Max(0f, value));
    }

    /// <summary>For a porch: a roof over it, on posts.</summary>
    [Export]
    public bool HasRoof
    {
        get => _hasRoof;
        set
        {
            Set(ref _hasRoof, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>For a porch: its roof's shape.</summary>
    [Export]
    public PorchRoofStyle PorchRoof
    {
        get => _porchRoof;
        set => Set(ref _porchRoof, value);
    }

    /// <summary>For a porch: how far above its deck its roof's underside is at the front, in metres.</summary>
    [Export(PropertyHint.Range, "1.8,6,0.1,suffix:m")]
    public float RoofHeight
    {
        get => _roofHeight;
        set => Set(ref _roofHeight, Mathf.Max(1.8f, value));
    }

    /// <summary>For a porch: a railing round its open sides (with a gap at its steps).</summary>
    [Export]
    public bool HasRailing
    {
        get => _hasRailing;
        set
        {
            Set(ref _hasRailing, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>For a porch: steps down from its front to the ground.</summary>
    [Export]
    public bool Steps
    {
        get => _steps;
        set
        {
            Set(ref _steps, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>For a porch: how far along its front its steps' start is, from its own start, in metres.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public float StepsFrom
    {
        get => _stepsFrom;
        set => Set(ref _stepsFrom, Mathf.Max(0f, value));
    }

    [Export(PropertyHint.Range, "0.6,10,0.1,suffix:m")]
    public float StepsWidth
    {
        get => _stepsWidth;
        set => Set(ref _stepsWidth, Mathf.Max(0.6f, value));
    }

    /// <summary>For a porch: its deck closed down to the ground.</summary>
    [Export]
    public bool Skirt
    {
        get => _skirt;
        set => Set(ref _skirt, value);
    }

    /// <summary>For a porch: its steps as they fit its front (from its start along its side), if it has any.</summary>
    public (float From, float To)? StepsSpan
    {
        get
        {
            if (!_steps)
                return null;
            var width = Mathf.Min(_stepsWidth, _width);
            var from = Mathf.Clamp(_stepsFrom, 0f, _width - width);
            return (from, from + width);
        }
    }

    /// <summary>How far below its <see cref="Up"/> it reaches, and above: what it takes up of its wall, up it.</summary>
    public (float Below, float Above) Reach => _kind == ProjectionKind.Porch
        ? (BuildingMesher.SlabThickness, _hasRoof ? _roofHeight + BuildingMesher.PorchRise(this) + BuildingMesher.PorchRoofThickness : _hasRailing ? _railingHeight + 0.05f : 0.3f)
        : _kind == ProjectionKind.Balcony
        ? (BuildingMesher.SlabThickness, _railingHeight)
        : (_awningStyle == AwningStyle.Sloped ? _drop + BuildingMesher.Valance : BuildingMesher.CanopyThickness, 0f);

    /// <summary>A new balcony or awning of the usual size, its left edge at a place along a side.</summary>
    public static BuildingProjection Make(ProjectionKind kind, int side, int storey, float from) => kind == ProjectionKind.Porch
        ? new BuildingProjection { _kind = kind, _side = side, _storey = 0, _from = from, _width = 3f, _depth = 2f, _up = 0f, _allStoreys = false, _railingHeight = 0.9f }
        : kind == ProjectionKind.Balcony
        ? new BuildingProjection { _kind = kind, _side = side, _storey = storey, _from = from }
        : new BuildingProjection { _kind = kind, _side = side, _storey = storey, _from = from, _width = 2.4f, _depth = 1.2f, _up = 2.6f, _allStoreys = false };

    /// <summary>In the Inspector: only the settings its kind (and style) uses.</summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        var name = property["name"].AsStringName();
        var balcony = _kind == ProjectionKind.Balcony;
        var awning = _kind == ProjectionKind.Awning;
        var porch = _kind == ProjectionKind.Porch;
        var show = true;
        if (name == PropertyName.Storey)
            show = !porch;
        else if (name == PropertyName.AllStoreys)
            show = balcony;
        else if (name == PropertyName.Railing || name == PropertyName.RailingHeight)
            show = balcony || (porch && _hasRailing);
        else if (name == PropertyName.AwningStyle)
            show = awning;
        else if (name == PropertyName.Cheeks || name == PropertyName.Drop)
            show = awning && _awningStyle == AwningStyle.Sloped;
        else if (name == PropertyName.PorchRoof || name == PropertyName.RoofHeight)
            show = porch && _hasRoof;
        else if (name == PropertyName.HasRoof || name == PropertyName.HasRailing || name == PropertyName.Steps || name == PropertyName.Skirt)
            show = porch;
        else if (name == PropertyName.StepsFrom || name == PropertyName.StepsWidth)
            show = porch && _steps;
        if (!show)
            property["usage"] = (int)((PropertyUsageFlags)property["usage"].AsInt32() & ~PropertyUsageFlags.Editor);
    }

    private void Set<T>(ref T field, T value)
    {
        field = value;
        EmitChanged();
    }
}
