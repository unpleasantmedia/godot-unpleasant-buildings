using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// The wall details there are to put on buildings, each a <see cref="WallDetailType"/>: one library
/// for a whole project (Project Settings &gt; Unpleasant Buildings &gt; Detail Library), or one
/// given to a building of its own.
/// </summary>
[Tool]
[GlobalClass]
public partial class WallDetailLibrary : Resource
{
    /// <summary>The project setting naming the project's library.</summary>
    public const string Setting = "unpleasant_buildings/detail_library";

    private Godot.Collections.Array<WallDetailType> _types = [];

    [Export]
    public Godot.Collections.Array<WallDetailType> Types
    {
        get => _types;
        set
        {
            _types = value ?? [];
            EmitChanged();
        }
    }

    /// <summary>The project's library, as Project Settings name it; null if none is set or it can't be loaded.</summary>
    public static WallDetailLibrary? ProjectLibrary() =>
        ProjectSettings.GetSetting(Setting, "").AsString() is { Length: > 0 } path && ResourceLoader.Exists(path)
            ? ResourceLoader.Load<WallDetailLibrary>(path)
            : null;
}
