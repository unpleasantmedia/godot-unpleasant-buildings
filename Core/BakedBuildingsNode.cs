using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A building cluster's look baked into a few meshes (see <see cref="Bake"/>, Bake Buildings on a
/// BuildingCluster): every building's walls, roofs, windows, doors' frames, balconies, insides and
/// stairs, and its wall details, merged by material in squares of the ground, so they cost a few
/// draw calls rather than many each. The buildings stay as they are, to be edited and baked again:
/// in play their own look is hidden (see Building.HideBaked) and this one shown; in the editor,
/// theirs shows, and this does not. Their collision, doors (to swing) and props are their own still.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/unpleasant_buildings/Icons/Building.svg")]
public partial class BakedBuildingsNode : Node3D
{
    /// <summary>Where baked buildings' meshes are saved.</summary>
    public static string MeshFolder => BuildingsLinks.BakedFolder ?? "res://BakedBuildings";

    /// <summary>The cluster as it was when baked (see <see cref="Fingerprint"/>): changed since, this is out of date.</summary>
    [Export]
    public string Print { get; set; } = "";

    /// <summary>What was in each baked square when it was baked, by its key: a square whose buildings are the same now is not baked again.</summary>
    [Export]
    public Godot.Collections.Dictionary<string, string> SquarePrints { get; set; } = new();

    /// <summary>The metadata on each baked mesh's node naming its square, as its key.</summary>
    private const string SquareMeta = "baked_square";

    /// <summary>Stands for no material, to merge surfaces with none by (baked with none, Godot's own default drawn, as it was).</summary>
    private static readonly Material Unmaterialled = new StandardMaterial3D();

    public override void _Ready()
    {
        // Only in play: in the editor the buildings' own look is shown, to be edited.
        Visible = !Engine.IsEditorHint();
        if (Engine.IsEditorHint() || GetParent() is not BuildingCluster cluster)
            return;
        if (Fingerprint(cluster) != Print)
        {
            Visible = false;
            GD.PushWarning($"{cluster.Name}: its buildings have changed since they were baked; showing them unbaked (slower). Press Bake Buildings in the cluster's Inspector and save the level.");
            return;
        }

        foreach (var building in cluster.Buildings)
            building.HideBaked();
    }

    /// <summary>A print of a cluster's buildings (see Building.Fingerprint), all of them, in order.</summary>
    public static string Fingerprint(BuildingCluster cluster) =>
        string.Join("|", cluster.Buildings.Select(b => $"{cluster.GetPathTo(b)}:{b.Fingerprint()}").Order()).Md5Text();

    /// <summary>A square's key: x and z, and how it casts shadows; each has a mesh of its own.</summary>
    private static string KeyOf(Vector2I square, GeometryInstance3D.ShadowCastingSetting shadow) => $"{square.X},{square.Y},{(int)shadow}";

    /// <summary>
    /// A cluster's look baked: its buildings' pieces (see Building.BakePieces) merged by square (by
    /// where each building's middle is), shadow and material into meshes saved in <see cref="MeshFolder"/>,
    /// a file each, under a BakedBuildingsNode in it. Baked before, only the squares whose buildings
    /// changed (or came, or went) are baked again; the first time, all of them.
    /// </summary>
    public static void Bake(BuildingCluster cluster, Node owner)
    {
        var name = $"{owner.SceneFilePath.GetFile().GetBaseName()}-{cluster.Name}";
        var size = Mathf.Max(8f, cluster.BakeSquare);
        var toCluster = cluster.GlobalTransform.AffineInverse();
        var existing = cluster.GetChildren().OfType<BakedBuildingsNode>().FirstOrDefault();
        var all = existing is null || existing.SquarePrints.Count == 0;

        // Each building's square (by its middle), and what is in each square now, as one number.
        var bySquare = new Dictionary<Vector2I, List<Building>>();
        foreach (var building in cluster.Buildings.Where(b => b.Outline.Length >= 3))
        {
            var middle = toCluster * building.ToGlobal(new Vector3(building.Outline.Average(c => c.X), 0f, building.Outline.Average(c => c.Y)));
            var square = new Vector2I(Mathf.FloorToInt(middle.X / size), Mathf.FloorToInt(middle.Z / size));
            if (!bySquare.TryGetValue(square, out var list))
                bySquare[square] = list = [];
            list.Add(building);
        }

        var prints = bySquare.ToDictionary(e => $"{e.Key.X},{e.Key.Y}",
            e => string.Join("|", e.Value.Select(b => b.Fingerprint()).Order()).Md5Text());
        var squaresBefore = existing?.SquarePrints ?? new();
        var dirty = all ? prints.Keys.ToHashSet() : prints.Keys.Union(squaresBefore.Keys)
            .Where(k => !prints.TryGetValue(k, out var now) || !squaresBefore.TryGetValue(k, out var then) || now != then).ToHashSet();
        if (!all && dirty.Count == 0)
        {
            existing!.Print = Fingerprint(cluster);
            GD.Print($"Bake Buildings: no square of {cluster.Name} has changed since it was baked.");
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(MeshFolder);
        BakedBuildingsNode baked;
        if (all)
        {
            using (var folder = DirAccess.Open(MeshFolder))
                foreach (var old in folder?.GetFiles().Where(f => f.StartsWith(name + "-")) ?? [])
                    folder!.Remove(old);
            foreach (var old in cluster.GetChildren().OfType<BakedBuildingsNode>().ToList())
            {
                cluster.RemoveChild(old);
                old.QueueFree();
            }

            baked = new BakedBuildingsNode { Name = "Baked" };
            cluster.AddChild(baked);
            baked.Owner = owner;
        }
        else
        {
            baked = existing!;
            foreach (var chunk in baked.GetChildren().OfType<MeshInstance3D>().ToList())
            {
                var key = chunk.GetMeta(SquareMeta, "").AsString();
                if (!dirty.Contains(key[..System.Math.Max(0, key.LastIndexOf(','))]))
                    continue;
                if (chunk.Mesh?.ResourcePath is { Length: > 0 } file && FileAccess.FileExists(file))
                    DirAccess.RemoveAbsolute(file);
                baked.RemoveChild(chunk);
                chunk.QueueFree();
            }
        }

        // Each square's buildings read on the main thread (their pieces are made from the scene), a few a
        // frame; merged and saved on threads; then put in here.
        var squares = bySquare.Where(e => dirty.Contains($"{e.Key.X},{e.Key.Y}")).ToList();
        var print = Fingerprint(cluster);
        BuildingJob.Start(cluster, $"Bake Buildings ({cluster.Name})", squares, entry =>
        {
            var (square, buildings) = entry;
            // Merged by shadow, then by material: each building's pieces, where they are in the cluster.
            var merged = new Dictionary<GeometryInstance3D.ShadowCastingSetting, Dictionary<Material, MergedSurface>>();
            foreach (var building in buildings.Where(GodotObject.IsInstanceValid))
            {
                var into = toCluster * building.GlobalTransform;
                foreach (var piece in building.BakePieces())
                {
                    if (!merged.TryGetValue(piece.Shadow, out var byMaterial))
                        merged[piece.Shadow] = byMaterial = [];
                    for (var surface = 0; surface < piece.Mesh.GetSurfaceCount(); surface++)
                    {
                        if (piece.Mesh is ArrayMesh made && made.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles)
                            continue;
                        var material = piece.MaterialOf(surface) ?? Unmaterialled;
                        if (!byMaterial.TryGetValue(material, out var merging))
                            byMaterial[material] = merging = new MergedSurface();
                        merging.Add(piece.Mesh, surface, into * piece.Place);
                    }
                }
            }

            return (square, merged);
        }, read =>
        {
            var (square, merged) = read;
            var saved = new List<(Vector2I Square, GeometryInstance3D.ShadowCastingSetting Shadow, string Path)>();
            foreach (var (shadow, byMaterial) in merged)
            {
                var mesh = new ArrayMesh();
                foreach (var (material, surface) in byMaterial)
                    surface.AddTo(mesh, material);
                if (mesh.GetSurfaceCount() == 0)
                    continue;
                var path = $"{MeshFolder}/{name}-{square.X}_{square.Y}-{(int)shadow}.res";
                if (ResourceSaver.Save(mesh, path, ResourceSaver.SaverFlags.Compress) != Error.Ok)
                    GD.PushError($"Bake Buildings: could not save {path}.");
                else
                    saved.Add((square, shadow, path));
            }

            return saved;
        }, results =>
        {
            if (!GodotObject.IsInstanceValid(baked))
                return;
            var meshes = 0;
            foreach (var (square, shadow, path) in results.SelectMany(r => r))
            {
                // The mesh as loaded from its file (the scene then refers to the file, not a copy of it).
                if (ResourceLoader.Load<ArrayMesh>(path, null, ResourceLoader.CacheMode.Replace) is not { } mesh)
                    continue;
                var chunk = new MeshInstance3D { Name = $"Buildings{square.X}_{square.Y}", Mesh = mesh, CastShadow = shadow };
                chunk.SetMeta(SquareMeta, KeyOf(square, shadow));
                baked.AddChild(chunk, true);
                chunk.Owner = owner;
                meshes++;
            }

            baked.Print = print;
            baked.SquarePrints = new Godot.Collections.Dictionary<string, string>(prints);
            // Not shown in the editor (the buildings' own look is): see _Ready.
            baked.Visible = false;
#if TOOLS
            EditorInterface.Singleton.MarkSceneAsUnsaved();
#endif
            GD.Print(all ? $"Bake Buildings: baked all of {cluster.Name} into {meshes} meshes." : $"Bake Buildings: {dirty.Count} squares of {cluster.Name} changed; baked them again ({meshes} meshes).");
        });
    }

    /// <summary>
    /// Surfaces with one material, merged into one: their corners moved to where they are, with
    /// whatever each has (normals, tangents, UVs, colours) and what the others have filled in.
    /// </summary>
    private sealed class MergedSurface
    {
        private readonly List<(Godot.Collections.Array Arrays, Transform3D Place)> _parts = [];

        public void Add(Mesh mesh, int surface, Transform3D place) => _parts.Add((mesh.SurfaceGetArrays(surface), place));

        public void AddTo(ArrayMesh mesh, Material material)
        {
            bool Has(int kind) => _parts.Any(p => p.Arrays[kind].VariantType != Variant.Type.Nil);
            var (hasTangents, hasColours, hasUv2) = (Has((int)Mesh.ArrayType.Tangent), Has((int)Mesh.ArrayType.Color), Has((int)Mesh.ArrayType.TexUV2));
            var points = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<float>();
            var colours = new List<Color>();
            var uvs = new List<Vector2>();
            var uv2s = new List<Vector2>();
            var indices = new List<int>();
            foreach (var (arrays, place) in _parts)
            {
                var at = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (at.Length == 0)
                    continue;
                var turn = place.Basis.Inverse().Transposed();
                var start = points.Count;
                var n = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var t = arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
                var c = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var uv2 = arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                for (var i = 0; i < at.Length; i++)
                {
                    points.Add(place * at[i]);
                    normals.Add(i < n.Length ? (turn * n[i]).Normalized() : Vector3.Up);
                    if (hasTangents)
                    {
                        var tangent = 4 * i + 3 < t.Length ? (place.Basis * new Vector3(t[4 * i], t[4 * i + 1], t[4 * i + 2])).Normalized() : Vector3.Right;
                        tangents.AddRange([tangent.X, tangent.Y, tangent.Z, 4 * i + 3 < t.Length ? t[4 * i + 3] : 1f]);
                    }

                    if (hasColours)
                        colours.Add(i < c.Length ? c[i] : Colors.White);
                    uvs.Add(i < uv.Length ? uv[i] : Vector2.Zero);
                    if (hasUv2)
                        uv2s.Add(i < uv2.Length ? uv2[i] : Vector2.Zero);
                }

                var index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if (index.Length > 0)
                    foreach (var k in index)
                        indices.Add(start + k);
                else
                    for (var k = 0; k < at.Length; k++)
                        indices.Add(start + k);
            }

            if (points.Count == 0)
                return;
            var merged = new Godot.Collections.Array();
            merged.Resize((int)Mesh.ArrayType.Max);
            merged[(int)Mesh.ArrayType.Vertex] = points.ToArray();
            merged[(int)Mesh.ArrayType.Normal] = normals.ToArray();
            if (hasTangents)
                merged[(int)Mesh.ArrayType.Tangent] = tangents.ToArray();
            if (hasColours)
                merged[(int)Mesh.ArrayType.Color] = colours.ToArray();
            merged[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
            if (hasUv2)
                merged[(int)Mesh.ArrayType.TexUV2] = uv2s.ToArray();
            merged[(int)Mesh.ArrayType.Index] = indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, merged);
            if (material != Unmaterialled)
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }
}
