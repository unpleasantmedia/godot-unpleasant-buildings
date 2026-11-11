#if TOOLS
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>Puts the plugin's header (from start, from end, centring) at the top of a selected door's, window's, detail's, balcony's, awning's or porch's Inspector.</summary>
[Tool]
public partial class ItemInspector : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject @object) => @object is BuildingOpening or WallDetail or BuildingProjection;

    public override void _ParseBegin(GodotObject @object)
    {
        if (UnpleasantBuildingsPlugin.Current?.ItemHeader(@object) is { } header)
            AddCustomControl(header);
    }
}
#endif
