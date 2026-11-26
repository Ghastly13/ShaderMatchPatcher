using System.Buffers.Binary;
using System.Text;

namespace ShaderMatchPatcher;

public sealed class NifException(string message) : Exception(message);

/// <summary>Little-endian cursor over a byte array.</summary>
internal sealed class NifReader(byte[] data, int pos = 0)
{
    public int Pos = pos;
    public int Length => data.Length;

    public ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || Pos + n > data.Length) throw new NifException("unexpected end of data");
        var s = new ReadOnlySpan<byte>(data, Pos, n);
        Pos += n;
        return s;
    }

    public byte[] TakeArray(int n) => Take(n).ToArray();
    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public string SizedString()
    {
        var n = U32();
        if (n > 0x100000) throw new NifException("string length out of range");
        return Encoding.Latin1.GetString(Take((int)n));
    }

    public string ExportString() => Encoding.Latin1.GetString(Take(U8()));
}

/// <summary>
/// Minimal NIF container for Skyrim LE/SE (20.2.0.7, user 12, stream 83/100): header,
/// raw block bytes and footer. Blocks are only interpreted where needed and are written
/// back byte-for-byte unless replaced; block sizes in the header are recomputed on save.
/// </summary>
public sealed class NifFile
{
    private readonly byte[] _data;
    private readonly int _sizesOffset;
    private readonly int _headerEnd;

    public uint BsVersion { get; }
    public IReadOnlyList<string> BlockTypes { get; }
    public IReadOnlyList<string> Strings { get; }
    public List<byte[]> Blocks { get; }
    public byte[] Footer { get; }
    public int NumBlocks => Blocks.Count;

    public NifFile(byte[] data)
    {
        _data = data;
        var nl = Array.IndexOf(data, (byte)'\n');
        if (nl < 0 || !Encoding.ASCII.GetString(data, 0, Math.Min(20, data.Length)).StartsWith("Gamebryo File Format"))
            throw new NifException("not a Gamebryo NIF");
        var r = new NifReader(data, nl + 1);
        var version = r.U32();
        if (version != 0x14020007) throw new NifException($"unsupported NIF version 0x{version:X8}");
        r.U8(); // endian
        var user = r.U32();
        var numBlocks = r.U32();
        BsVersion = r.U32();
        if (user != 12 || (BsVersion != 83 && BsVersion != 100))
            throw new NifException($"not a Skyrim LE/SE NIF (user {user}, stream {BsVersion})");
        r.ExportString(); // author
        r.ExportString(); // process script
        r.ExportString(); // export script
        var nTypes = r.U16();
        var typeNames = new string[nTypes];
        for (var i = 0; i < nTypes; i++) typeNames[i] = r.SizedString();
        var types = new string[numBlocks];
        for (var i = 0; i < numBlocks; i++)
        {
            var idx = r.U16() & 0x7FFF;
            if (idx >= nTypes) throw new NifException("block type index out of range");
            types[i] = typeNames[idx];
        }
        BlockTypes = types;
        _sizesOffset = r.Pos;
        var sizes = new uint[numBlocks];
        for (var i = 0; i < numBlocks; i++) sizes[i] = r.U32();
        var nStrings = r.U32();
        r.U32(); // max string length
        var strings = new string[nStrings];
        for (var i = 0; i < nStrings; i++) strings[i] = r.SizedString();
        Strings = strings;
        var nGroups = r.U32();
        r.Take(checked((int)nGroups * 4));
        _headerEnd = r.Pos;

        Blocks = new List<byte[]>((int)numBlocks);
        long pos = _headerEnd;
        foreach (var s in sizes)
        {
            if (pos + s > data.Length) throw new NifException("block sizes run past end of file");
            Blocks.Add(data.AsSpan((int)pos, (int)s).ToArray());
            pos += s;
        }
        Footer = data.AsSpan((int)pos).ToArray();
    }

    public string GetString(int idx) => idx >= 0 && idx < Strings.Count ? Strings[idx] : "";

    public byte[] ToBytes()
    {
        var head = _data.AsSpan(0, _headerEnd).ToArray();
        for (var i = 0; i < Blocks.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(_sizesOffset + 4 * i), (uint)Blocks[i].Length);
        using var ms = new MemoryStream(head.Length + Blocks.Sum(b => b.Length) + Footer.Length);
        ms.Write(head);
        foreach (var b in Blocks) ms.Write(b);
        ms.Write(Footer);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ helpers

    private static readonly HashSet<string> TriShapeLike =
        ["BSTriShape", "BSDynamicTriShape", "BSSubIndexTriShape", "BSMeshLODTriShape", "BSLODTriShape"];
    private static readonly HashSet<string> NiGeometryLike = ["NiTriShape", "NiTriStrips"];

    /// <summary>Shader block index -> shape name. Unreadable shapes are left out.</summary>
    public Dictionary<int, string> ShapeNamesByShader()
    {
        var map = new Dictionary<int, string>();
        for (var i = 0; i < Blocks.Count; i++)
        {
            var t = BlockTypes[i];
            if (!TriShapeLike.Contains(t) && !NiGeometryLike.Contains(t)) continue;
            try
            {
                var r = new NifReader(Blocks[i]);
                var name = r.I32();
                r.Take(checked((int)r.U32() * 4)); // extra data
                r.I32();                             // controller
                r.U32();                             // flags
                r.Take(12 + 36 + 4);                 // translation, rotation, scale
                int shader;
                if (TriShapeLike.Contains(t))
                {
                    r.I32();       // collision
                    r.Take(16);    // bounding sphere
                    r.I32();       // skin
                    shader = r.I32();
                }
                else
                {
                    r.I32(); r.I32(); r.I32();      // collision, data, skin instance
                    var nMat = checked((int)r.U32());
                    r.Take(nMat * 8);                // material names + extra data
                    r.I32();                         // active material
                    r.U8();                          // dirty flag
                    shader = r.I32();
                }
                if (shader >= 0 && shader < Blocks.Count) map[shader] = GetString(name);
            }
            catch (NifException)
            {
            }
        }
        return map;
    }

    /// <summary>Every readable BSLightingShaderProperty with its texture set and shape name.</summary>
    public List<ShaderEntry> Shaders()
    {
        var names = ShapeNamesByShader();
        var list = new List<ShaderEntry>();
        for (var i = 0; i < Blocks.Count; i++)
        {
            if (BlockTypes[i] != "BSLightingShaderProperty") continue;
            var lsp = new LightingShader(Blocks[i]);
            var slots = Array.Empty<string>();
            var ts = lsp.TextureSet;
            if (ts >= 0 && ts < Blocks.Count && BlockTypes[ts] == "BSShaderTextureSet")
                slots = TextureSet.Parse(Blocks[ts]);
            list.Add(new ShaderEntry(i, lsp, slots, names.GetValueOrDefault(i, "")));
        }
        return list;
    }
}

public sealed record ShaderEntry(int Block, LightingShader Shader, string[] Slots, string ShapeName);

public static class TextureSet
{
    public static string[] Parse(byte[] block)
    {
        var r = new NifReader(block);
        var n = r.U32();
        if (n > 64) throw new NifException("texture set slot count out of range");
        var slots = new string[n];
        for (var i = 0; i < n; i++) slots[i] = r.SizedString();
        if (r.Pos != block.Length) throw new NifException("BSShaderTextureSet size mismatch");
        return slots;
    }

    public static byte[] Build(IReadOnlyList<string> slots)
    {
        using var ms = new MemoryStream();
        Span<byte> u = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u, (uint)slots.Count);
        ms.Write(u);
        foreach (var s in slots)
        {
            var b = Encoding.Latin1.GetBytes(s);
            BinaryPrimitives.WriteUInt32LittleEndian(u, (uint)b.Length);
            ms.Write(u);
            ms.Write(b);
        }
        return ms.ToArray();
    }

    public static string Normalize(string path)
    {
        var p = path.Trim().Replace('/', '\\').ToLowerInvariant().TrimStart('\\');
        if (p.StartsWith("data\\")) p = p[5..];
        if (p.Length > 0 && !p.StartsWith("textures\\")) p = "textures\\" + p;
        return p;
    }
}

/// <summary>BSLightingShaderProperty, Skyrim LE/SE layout.</summary>
public sealed class LightingShader
{
    public static readonly IReadOnlyDictionary<uint, string> TypeNames = new Dictionary<uint, string>
    {
        [0] = "Default", [1] = "EnvironmentMap", [2] = "GlowShader", [3] = "Parallax", [4] = "FaceTint",
        [5] = "SkinTint", [6] = "HairTint", [7] = "ParallaxOcc", [8] = "MultitextureLandscape",
        [9] = "LODLandscape", [10] = "Snow", [11] = "MultiLayerParallax", [12] = "TreeAnim",
        [13] = "LODObjects", [14] = "SparkleSnow", [15] = "LODObjectsHD", [16] = "EyeEnvmap",
        [17] = "Cloud", [18] = "LODLandscapeNoise", [19] = "MultitextureLandscapeLODBlend",
        [20] = "FO4Dismemberment",
    };

    // bytes of shader-type-specific data at the end of the block
    private static readonly IReadOnlyDictionary<uint, int> TypeTail = new Dictionary<uint, int>
    {
        [1] = 4, [5] = 12, [6] = 12, [7] = 8, [11] = 20, [14] = 16, [16] = 28,
    };

    // flag bits describing the shape's geometry, never copied from another mesh
    public const uint Slsf1Keep = (1u << 1) | (1u << 3);  // Skinned, Vertex_Alpha
    public const uint Slsf2Keep = (1u << 4) | (1u << 5);  // Double_Sided, Vertex_Colors

    public uint ShaderType;
    public int Name;
    public List<int> Extras = [];
    public int Controller;
    public uint Flags1, Flags2;
    public byte[] Uv = [];
    public int TextureSet;
    public byte[] Emissive = [], EmissiveMult = [], Clamp = [], Alpha = [], Refraction = [],
        Gloss = [], SpecColor = [], SpecStrength = [], Lighting = [], Tail = [];

    private LightingShader() { }

    public LightingShader(byte[] block)
    {
        var r = new NifReader(block);
        ShaderType = r.U32();
        Name = r.I32();
        var nExtra = r.U32();
        if (nExtra > 1024) throw new NifException("extra data count out of range");
        for (var i = 0; i < nExtra; i++) Extras.Add(r.I32());
        Controller = r.I32();
        Flags1 = r.U32();
        Flags2 = r.U32();
        Uv = r.TakeArray(16);
        TextureSet = r.I32();
        Emissive = r.TakeArray(12);
        EmissiveMult = r.TakeArray(4);
        Clamp = r.TakeArray(4);
        Alpha = r.TakeArray(4);
        Refraction = r.TakeArray(4);
        Gloss = r.TakeArray(4);
        SpecColor = r.TakeArray(12);
        SpecStrength = r.TakeArray(4);
        Lighting = r.TakeArray(8);
        Tail = r.TakeArray(block.Length - r.Pos);
        var expect = TypeTail.GetValueOrDefault(ShaderType, 0);
        if (Tail.Length != expect)
            throw new NifException($"BSLightingShaderProperty layout not recognised (shader type {ShaderType}, " +
                                   $"{Tail.Length} trailing bytes, expected {expect})");
    }

    public LightingShader Clone() => new()
    {
        ShaderType = ShaderType, Name = Name, Extras = [.. Extras], Controller = Controller,
        Flags1 = Flags1, Flags2 = Flags2, Uv = Uv, TextureSet = TextureSet, Emissive = Emissive,
        EmissiveMult = EmissiveMult, Clamp = Clamp, Alpha = Alpha, Refraction = Refraction,
        Gloss = Gloss, SpecColor = SpecColor, SpecStrength = SpecStrength, Lighting = Lighting, Tail = Tail,
    };

    /// <summary>The values this patcher copies, as a comparable key.</summary>
    public string MaterialKey =>
        $"{ShaderType}|{Flags1 & ~Slsf1Keep:X8}|{Flags2 & ~Slsf2Keep:X8}|" +
        Convert.ToHexString([.. Emissive, .. EmissiveMult, .. Refraction, .. Gloss, .. SpecColor,
            .. SpecStrength, .. Lighting, .. Tail]);

    /// <summary>Copies source's material onto this shader. Returns the names of what changed.</summary>
    public List<string> CopyMaterialFrom(LightingShader s)
    {
        var changes = new List<string>();
        var nf1 = (s.Flags1 & ~Slsf1Keep) | (Flags1 & Slsf1Keep);
        var nf2 = (s.Flags2 & ~Slsf2Keep) | (Flags2 & Slsf2Keep);
        if (ShaderType != s.ShaderType) changes.Add("type");
        if (nf1 != Flags1 || nf2 != Flags2) changes.Add("flags");
        ShaderType = s.ShaderType;
        Flags1 = nf1;
        Flags2 = nf2;
        void Copy(ref byte[] mine, byte[] theirs, string label)
        {
            if (mine.AsSpan().SequenceEqual(theirs)) return;
            changes.Add(label);
            mine = theirs;
        }
        Copy(ref Emissive, s.Emissive, "emissive");
        Copy(ref EmissiveMult, s.EmissiveMult, "emissive multiple");
        Copy(ref Refraction, s.Refraction, "refraction");
        Copy(ref Gloss, s.Gloss, "glossiness");
        Copy(ref SpecColor, s.SpecColor, "specular colour");
        Copy(ref SpecStrength, s.SpecStrength, "specular strength");
        Copy(ref Lighting, s.Lighting, "lighting effects");
        Copy(ref Tail, s.Tail, ShaderType == 1 ? "env map scale" : "type data");
        return changes;
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        var u = new byte[4];
        void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(u, v); ms.Write(u); }
        void I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(u, v); ms.Write(u); }
        U32(ShaderType);
        I32(Name);
        U32((uint)Extras.Count);
        foreach (var e in Extras) I32(e);
        I32(Controller);
        U32(Flags1);
        U32(Flags2);
        ms.Write(Uv);
        I32(TextureSet);
        foreach (var b in new[] { Emissive, EmissiveMult, Clamp, Alpha, Refraction, Gloss, SpecColor,
                     SpecStrength, Lighting, Tail })
            ms.Write(b);
        return ms.ToArray();
    }

    private static float F(byte[] b, int off = 0) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(off));

    public string Describe()
    {
        var s = $"{TypeNames.GetValueOrDefault(ShaderType, ShaderType.ToString())} gloss={F(Gloss):0.##} spec={F(SpecStrength):0.##}";
        if (ShaderType == 1) s += $" envscale={F(Tail):0.##}";
        return s;
    }
}
