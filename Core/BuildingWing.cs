using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A part of a building beside its main one (its <see cref="Building.Outline"/>): an outline of its
/// own, drawn on a floor of the building and walled up so many storeys from there. Where it meets the
/// building's other parts on a floor, they are one shape; where it stands above nothing, it is closed
/// underneath. Drawn above the ground floor, it has to be attached to another part: overlapping or
/// touching one on its own bottom floor, or resting on one on the floor below.
/// </summary>
[Tool]
[GlobalClass]
public partial class BuildingWing : Resource
{
    private Vector2[] _outline = [];
    private int _floor;
    private int _storeys = 1;
    private bool _ownRoof;
    private RoofType _roof = RoofType.Flat;
    private int _roofSide = -1;
    private float _roofPitch = 35f;
    private float _roofOverhang = 0.3f;
    private float _parapetHeight = 0.5f;
    private Material? _roofMaterial;
    private Material? _undersideMaterial;

    /// <summary>
    /// Its corners in order round it, in the building's own space, on the ground (X and Z, as a
    /// Vector2's X and Y). Either way round. With fewer than three, nothing is built of it.
    /// </summary>
    [Export]
    public Vector2[] Outline
    {
        get => _outline;
        set => Set(ref _outline, value ?? []);
    }

    /// <summary>The storey it starts on, from 0 (the ground floor).</summary>
    [Export(PropertyHint.Range, "0,40,1")]
    public int Floor
    {
        get => _floor;
        set => Set(ref _floor, Mathf.Max(0, value));
    }

    /// <summary>How many storeys it is tall, from its floor up.</summary>
    [Export(PropertyHint.Range, "1,40,1")]
    public int Storeys
    {
        get => _storeys;
        set => Set(ref _storeys, Mathf.Max(1, value));
    }

    /// <summary>The storey above its top one: where its roof is.</summary>
    public int Top => _floor + _storeys;

    /// <summary>What closes it underneath where nothing below holds it up; with none, its building's walls' material.</summary>
    [Export]
    public Material? UndersideMaterial
    {
        get => _undersideMaterial;
        set => Set(ref _undersideMaterial, value);
    }

    /// <summary>Whether it has roof settings of its own; without, its building's.</summary>
    [ExportGroup("Roof")]
    [Export]
    public bool OwnRoof
    {
        get => _ownRoof;
        set
        {
            Set(ref _ownRoof, value);
            NotifyPropertyListChanged();
        }
    }

    [Export]
    public RoofType Roof
    {
        get => _roof;
        set => Set(ref _roof, value);
    }

    /// <summary>For a gabled or shed roof: which side of its outline the roof goes by (-1: its longest).</summary>
    [Export]
    public int RoofSide
    {
        get => _roofSide;
        set => Set(ref _roofSide, value);
    }

    [Export(PropertyHint.Range, "5,70,1,suffix:°")]
    public float RoofPitch
    {
        get => _roofPitch;
        set => Set(ref _roofPitch, Mathf.Clamp(value, 1f, 80f));
    }

    [Export(PropertyHint.Range, "0,2,0.05,suffix:m")]
    public float RoofOverhang
    {
        get => _roofOverhang;
        set => Set(ref _roofOverhang, Mathf.Max(0f, value));
    }

    [Export(PropertyHint.Range, "0,2,0.05,suffix:m")]
    public float ParapetHeight
    {
        get => _parapetHeight;
        set => Set(ref _parapetHeight, Mathf.Max(0f, value));
    }

    [Export]
    public Material? RoofMaterial
    {
        get => _roofMaterial;
        set => Set(ref _roofMaterial, value);
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        // Its own roof settings shown only when it has its own roof.
        var name = property["name"].AsStringName();
        if (!_ownRoof && (name == PropertyName.Roof || name == PropertyName.RoofSide || name == PropertyName.RoofPitch
            || name == PropertyName.RoofOverhang || name == PropertyName.ParapetHeight || name == PropertyName.RoofMaterial))
            property["usage"] = (int)(property["usage"].As<PropertyUsageFlags>() & ~PropertyUsageFlags.Editor);
    }

    private void Set<T>(ref T field, T value)
    {
        field = value;
        EmitChanged();
    }
}
