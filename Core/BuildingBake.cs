using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>A piece of a building to bake: a mesh, where it is in the building's own space, what each of its surfaces is drawn with, and how it casts shadows.</summary>
public sealed record BakePiece(Mesh Mesh, Transform3D Place, System.Func<int, Material?> MaterialOf, GeometryInstance3D.ShadowCastingSetting Shadow);

/// <summary>
/// A building's part in its cluster's bake (see <see cref="BakedBuildingsNode"/>): what it looks
/// like as the game builds it (whole, its doors' leaves left to swing), and its wall details; a
/// print of all it is made of, to know when it has changed; and, in the game, its own look hidden
/// for the baked one, its collision, doors and props still its own.
/// </summary>
public partial class Building
{
    /// <summary>Whether its look is baked: its own hidden in the game, the baked one drawn instead.</summary>
    private bool _bakedAway;

    /// <summary>What it looks like, to bake: its mesh as built in the game (whole; doors' leaves left out, to swing), and its wall details' models.</summary>
    public List<BakePiece> BakePieces()
    {
        var pieces = new List<BakePiece>();
        if (_outline.Length < 3 || !Simple(_outline))
            return pieces;
        var (low, _) = GroundRange();
        var mesh = MakeMesh(low, false, _enterable ? [] : null);
        pieces.Add(new BakePiece(mesh, Transform3D.Identity, mesh.SurfaceGetMaterial, GeometryInstance3D.ShadowCastingSetting.On));
        foreach (var (detail, shown) in _shown)
        {
            if (!Fits(detail) || !IsInstanceValid(shown.Node))
                continue;
            foreach (var model in shown.Node.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            {
                if (model.Mesh is not { } m)
                    continue;
                // Where it is in the building: the detail's place, and its own in the detail.
                var at = shown.Node.Transform * Within(model, shown.Node);
                pieces.Add(new BakePiece(m, at, s => model.MaterialOverride ?? model.GetSurfaceOverrideMaterial(s) ?? m.SurfaceGetMaterial(s), model.CastShadow));
            }
        }

        return pieces;
    }

    /// <summary>Where a part sits in a node: the transforms from it up to the node.</summary>
    private static Transform3D Within(Node part, Node node)
    {
        var at = Transform3D.Identity;
        for (var at3 = part; at3 is not null && at3 != node; at3 = at3.GetParent())
            if (at3 is Node3D spatial)
                at = spatial.Transform * at;
        return at;
    }

    /// <summary>
    /// A print of all it is made of (its settings, and its doors', windows', details', balconies',
    /// walls', stairs' and rooms'), and where it stands: the same, its look is the same.
    /// </summary>
    public string Fingerprint()
    {
        var text = new System.Text.StringBuilder();
        // (Not the ground under it, which the game only knows once it is built: baked again by hand when that changes.)
        text.Append(GlobalTransform).Append('|');
        Print(this, text, 0);
        return text.ToString().Md5Text();
    }

    /// <summary>An object's stored settings, as text: resources in files by their path, those of its own by their settings in turn.</summary>
    private static void Print(GodotObject o, System.Text.StringBuilder text, int depth)
    {
        foreach (var property in o.GetPropertyList())
        {
            var usage = (PropertyUsageFlags)property["usage"].AsInt64();
            if (!usage.HasFlag(PropertyUsageFlags.Storage))
                continue;
            var name = property["name"].AsStringName();
            text.Append(name).Append('=');
            PrintValue(o.Get(name), text, depth);
            text.Append(';');
        }
    }

    private static void PrintValue(Variant value, System.Text.StringBuilder text, int depth)
    {
        switch (value.VariantType)
        {
            case Variant.Type.Object when value.AsGodotObject() is Resource r:
                if (r.ResourcePath is { Length: > 0 } path && !path.Contains("::"))
                    text.Append(path);
                else if (depth < 6)
                {
                    text.Append('{');
                    Print(r, text, depth + 1);
                    text.Append('}');
                }

                break;
            case Variant.Type.Array:
                text.Append('[');
                foreach (var item in value.AsGodotArray())
                {
                    PrintValue(item, text, depth);
                    text.Append(',');
                }

                text.Append(']');
                break;
            default:
                text.Append(value.ToString());
                break;
        }
    }

    /// <summary>In the game: its own look hidden, its cluster's baked one drawn instead (its collision, doors and props its own still).</summary>
    public void HideBaked()
    {
        _bakedAway = true;
        // Built already (before its cluster's bake was found good): let go of, till something near needs it solid.
        if (!_streamedIn && !Engine.IsEditorHint())
            LetGo();
        ShowOwnLook();
    }

    /// <summary>Its own look shown or not, as it is baked or not: its mesh, and its details' models.</summary>
    private void ShowOwnLook()
    {
        if (_instance.IsValid)
            RenderingServer.InstanceSetVisible(_instance, !_bakedAway && IsVisibleInTree());
        if (!_bakedAway)
            return;
        foreach (var (_, shown) in _shown)
            if (IsInstanceValid(shown.Node))
                shown.Node.Visible = false;
    }
}
