using Mutagen.Bethesda.Skyrim;

namespace ShaderMatchPatcher;

/// <summary>What a mesh is used for: collected from every weapon record pointing at it.</summary>
public sealed class WeaponContext
{
    public HashSet<WeaponAnimationType> Anims { get; } = [];
    public HashSet<string> Materials { get; } = new(StringComparer.OrdinalIgnoreCase);   // WeapMaterial*
    public HashSet<string> Weapons { get; } = new(StringComparer.OrdinalIgnoreCase);     // editor IDs
    public bool UsedAsWorldModel { get; set; }        // a weapon's Model (3rd person, on the ground)
    public bool UsedAsFirstPersonModel { get; set; }  // a weapon's First Person Model
    /// <summary>Only ever seen in first person. Decided by use, not by file name.</summary>
    public bool FirstPerson => UsedAsFirstPersonModel && !UsedAsWorldModel;
}

public sealed class RefShape
{
    public required string Mesh { get; init; }
    public required string Shape { get; init; }
    public required LightingShader Shader { get; init; }
    public required string[] Slots { get; init; }
    public required WeaponContext Context { get; init; }
    public string Diffuse => Slots.Length > 0 ? TextureSet.Normalize(Slots[0]) : "";
    public string Normal => Slots.Length > 1 ? TextureSet.Normalize(Slots[1]) : "";
    public bool FirstPerson => Context.FirstPerson;
    public string Label => $"{Mesh} [{Shape}]";
}

public sealed class TargetShape
{
    public required string Mesh { get; init; }
    public required int Block { get; init; }
    public required string Shape { get; init; }
    public required LightingShader Original { get; init; }
    public required string[] Slots { get; init; }
    public required WeaponContext Context { get; init; }
    public string Diffuse => Slots.Length > 0 ? TextureSet.Normalize(Slots[0]) : "";
    public string Normal => Slots.Length > 1 ? TextureSet.Normalize(Slots[1]) : "";
    public bool FirstPerson => Context.FirstPerson;

    // result
    public LightingShader? Source;        // the vanilla shader whose material is used
    public string[]? SourceSlots;         // set only when empty slots may be filled from it
    public string SourceLabel = "";
    public string Method = "";
    public string Note = "";
    public List<RefShape>? Ambiguous;     // exact-texture candidates that disagree
    public bool Skipped;
    public bool Resolved => Source != null;

    public void Resolve(LightingShader source, string label, string method, string[]? slotsForFill, string note = "")
    {
        Source = source;
        SourceLabel = label;
        Method = method;
        SourceSlots = slotsForFill;
        Note = note;
        Ambiguous = null;
    }
}

/// <summary>
/// Decides which vanilla shader settings each target shape should get.
///  1. exact texture: vanilla shapes with the same diffuse (narrowed by normal map, scabbard,
///     1st/3rd person, weapon type, material) that agree, or clearly agree by majority;
///  2. other shapes (repeated until nothing changes):
///     a. ambiguous shapes take the variant from the vanilla mesh the rest of their mesh matched;
///     b. ambiguous shapes take the variant the same texture resolved to elsewhere in the mod;
///     c. shapes the mod author gave identical original settings to a resolved shape in the
///        same mesh (or with the same texture anywhere in the mod) follow that shape;
///  3. weapon material: shapes with the mod's own textures use the settings vanilla weapons of
///     the same material keyword and same shader type clearly agree on, then step 2 again;
///  4. optionally, the most common variant for anything still ambiguous.
/// Inferred results (2c, 3) never change the shader type and never fill texture slots.
/// </summary>
public static class Matcher
{
    private static readonly string[] ScabbardHints = ["scb", "scabbard", "sheath"];
    public static readonly string[] SkipTexturePrefixes = ["textures\\blood\\", "textures\\effects\\"];

    /// <summary>Vanilla shapes that must agree before a weapon-material inference is trusted.</summary>
    private const int MinMaterialSupport = 2;

    public static bool IsScabbard(string shape) =>
        ScabbardHints.Any(h => shape.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static List<RefShape> Narrow(List<RefShape> pool, Func<RefShape, bool> pred)
    {
        var sub = pool.Where(pred).ToList();
        return sub.Count > 0 ? sub : pool;
    }

    private static bool Distinct(List<RefShape> pool) =>
        pool.Select(c => c.Shader.MaterialKey).Distinct().Count() > 1;

    /// <summary>The material most candidates share. Null if there is no clear (&gt;50%) majority.</summary>
    private static (RefShape pick, int votes, int variants)? Majority(List<RefShape> pool, bool force = false)
    {
        var groups = pool.GroupBy(c => c.Shader.MaterialKey).OrderByDescending(g => g.Count()).ToList();
        var top = groups[0];
        if (groups.Count == 1 || top.Count() * 2 > pool.Count || force)
            return (top.First(), top.Count(), groups.Count);
        return null;
    }

    private static string Variants(List<RefShape> pool) =>
        string.Join(" | ", pool.GroupBy(c => c.Shader.MaterialKey).OrderByDescending(g => g.Count()).Take(4)
            .Select(g => $"{g.First().Shader.Describe()} x{g.Count()} e.g. {Path.GetFileName(g.First().Mesh)}"));

    public static void Run(List<TargetShape> targets, List<RefShape> reference, Settings settings)
    {
        var byDiffuse = reference.GroupBy(r => r.Diffuse).ToDictionary(g => g.Key, g => g.ToList());

        // ---- 1. exact texture
        foreach (var t in targets)
        {
            if (t.Diffuse.Length == 0) { t.Skipped = true; t.Note = "no diffuse texture"; continue; }
            if (SkipTexturePrefixes.Any(p => t.Diffuse.StartsWith(p)))
            {
                t.Skipped = true;
                t.Note = "shared effect texture";
                continue;
            }
            if (!byDiffuse.TryGetValue(t.Diffuse, out var pool)) continue;

            if (t.Normal.Length > 0) pool = Narrow(pool, c => c.Normal == t.Normal);
            if (IsScabbard(t.Shape)) pool = Narrow(pool, c => IsScabbard(c.Shape));
            if (Distinct(pool)) pool = Narrow(pool, c => c.FirstPerson == t.FirstPerson);
            if (Distinct(pool)) pool = Narrow(pool, c => c.Context.Anims.Overlaps(t.Context.Anims));
            if (Distinct(pool)) pool = Narrow(pool, c => c.Context.Materials.Overlaps(t.Context.Materials));

            var m = Majority(pool);
            if (m is { } r)
                t.Resolve(r.pick.Shader, r.pick.Label,
                    r.variants == 1 ? "exact texture" : $"exact texture, majority {r.votes} of {pool.Count}",
                    settings.FillEmptyTextureSlots ? r.pick.Slots : null);
            else
            {
                t.Ambiguous = pool;
                t.Note = "vanilla disagrees: " + Variants(pool);
            }
        }

        if (settings.InferFromOtherShapes) OtherShapes(targets, settings);

        // ---- 3. weapon material
        if (settings.InferFromWeaponMaterial)
        {
            foreach (var t in targets.Where(t => !t.Resolved && !t.Skipped && t.Ambiguous == null))
            {
                if (IsScabbard(t.Shape) || t.Context.Materials.Count == 0) continue;
                var pool = reference.Where(r =>
                        r.Shader.ShaderType == t.Original.ShaderType &&
                        r.Context.Materials.Overlaps(t.Context.Materials) &&
                        !IsScabbard(r.Shape) &&
                        !SkipTexturePrefixes.Any(p => r.Diffuse.StartsWith(p)))
                    .ToList();
                if (pool.Count < MinMaterialSupport) continue;
                var sameAnim = pool.Where(c => c.Context.Anims.Overlaps(t.Context.Anims)).ToList();
                if (sameAnim.Count >= MinMaterialSupport) pool = sameAnim;
                var samePerson = pool.Where(c => c.FirstPerson == t.FirstPerson).ToList();
                if (Distinct(pool) && samePerson.Count >= MinMaterialSupport) pool = samePerson;
                var m = Majority(pool);
                if (m is { } r && r.votes >= MinMaterialSupport)
                    t.Resolve(r.pick.Shader, r.pick.Label,
                        $"weapon material {string.Join("/", t.Context.Materials)} ({r.votes} of {pool.Count} vanilla shapes agree)",
                        null);
            }
            if (settings.InferFromOtherShapes) OtherShapes(targets, settings);
        }

        // ---- 4. most common, if asked
        if (settings.UseMostCommonWhenAmbiguous)
        {
            foreach (var t in targets.Where(t => !t.Resolved && t.Ambiguous != null))
            {
                var r = Majority(t.Ambiguous!, force: true)!.Value;
                t.Resolve(r.pick.Shader, r.pick.Label,
                    $"most common of disagreeing vanilla ({r.votes} of {t.Ambiguous!.Count})",
                    settings.FillEmptyTextureSlots ? r.pick.Slots : null);
            }
        }

        foreach (var t in targets.Where(t => !t.Resolved && !t.Skipped && t.Ambiguous == null && t.Note.Length == 0))
            t.Note = "no vanilla weapon uses this texture, and nothing to infer it from";
    }

    private static void OtherShapes(List<TargetShape> targets, Settings settings)
    {
        bool changed;
        var guard = 0;
        do
        {
            changed = false;
            foreach (var t in targets.Where(t => !t.Resolved && !t.Skipped))
            {
                var siblings = targets.Where(s => s.Mesh == t.Mesh && s != t && s.Resolved).ToList();

                if (t.Ambiguous != null)
                {
                    // a. the vanilla mesh(es) the rest of this mesh matched
                    var srcMeshes = siblings.Where(s => s.Method.StartsWith("exact"))
                        .Select(s => s.SourceLabel[..s.SourceLabel.LastIndexOf(" [", StringComparison.Ordinal)])
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var sub = t.Ambiguous.Where(c => srcMeshes.Contains(c.Mesh)).ToList();
                    if (sub.Count > 0 && !Distinct(sub))
                    {
                        t.Resolve(sub[0].Shader, sub[0].Label,
                            "other shapes in this mesh matched the same vanilla mesh",
                            settings.FillEmptyTextureSlots ? sub[0].Slots : null);
                        changed = true;
                        continue;
                    }
                    // b. the same texture resolved elsewhere in the mod
                    var keys = t.Ambiguous.Select(c => c.Shader.MaterialKey).ToHashSet();
                    var same = targets.FirstOrDefault(o => o.Resolved && o.Diffuse == t.Diffuse &&
                                                           keys.Contains(o.Source!.MaterialKey));
                    if (same != null)
                    {
                        var c = t.Ambiguous.First(c => c.Shader.MaterialKey == same.Source!.MaterialKey);
                        t.Resolve(c.Shader, c.Label, $"same texture resolved in {same.Mesh} [{same.Shape}]",
                            settings.FillEmptyTextureSlots ? c.Slots : null);
                        changed = true;
                    }
                    continue;
                }

                // c. shapes the author set up identically (same mesh first, then same texture mod-wide)
                // a scabbard and a blade never stand in for each other
                var key = t.Original.MaterialKey;
                var scb = IsScabbard(t.Shape);
                var peers = siblings.Where(s => s.Original.MaterialKey == key && IsScabbard(s.Shape) == scb).ToList();
                var where = "in this mesh";
                if (peers.Count == 0)
                {
                    peers = targets.Where(o => o != t && o.Resolved && o.Diffuse == t.Diffuse &&
                                               IsScabbard(o.Shape) == scb &&
                                               o.Original.MaterialKey == key).ToList();
                    where = "with the same texture";
                }
                if (peers.Count == 0) continue;
                var results = peers.GroupBy(p => p.Source!.MaterialKey).ToList();
                if (results.Count != 1) continue;
                var peer = peers[0];
                if (peer.Source!.ShaderType != t.Original.ShaderType) continue;
                t.Resolve(peer.Source, peer.SourceLabel,
                    $"same original settings as [{peer.Shape}] {where}", null);
                changed = true;
            }
        } while (changed && ++guard < 20);
    }
}
