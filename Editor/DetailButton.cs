#if TOOLS
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>A detail's button in the Wall Details dock: says itself when it is toggled, and which detail it is for.</summary>
[Tool]
public partial class DetailButton : Button
{
    /// <summary>Its detail's place in the dock's list.</summary>
    public int Index { get; set; }

    public override void _Toggled(bool toggledOn) => UnpleasantBuildingsPlugin.Current?.DetailToggled(this, toggledOn);
}
#endif
