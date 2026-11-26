using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;

namespace ShaderMatchPatcher;

public class Settings
{
    [SynthesisOrder]
    [SynthesisSettingName("Target mod")]
    [SynthesisTooltip("The one plugin whose weapon meshes get patched (e.g. LSB_BattleHammersV6.esp). " +
                      "Only weapons this plugin defines are touched. Leave empty and the patcher does nothing.")]
    public ModKey TargetMod = ModKey.Null;

    [SynthesisOrder]
    [SynthesisSettingName("Output folder")]
    [SynthesisTooltip("Where patched meshes are written, e.g. a new MO2 mod folder " +
                      "(...\\mods\\Shader Match Output). Empty = the Data folder, which under MO2 is the overwrite folder.")]
    public string OutputFolder = "";

    [SynthesisOrder]
    [SynthesisSettingName("Infer from other shapes")]
    [SynthesisTooltip("Resolve shapes with no clear vanilla match from the shapes around them: which vanilla mesh " +
                      "the other shapes in the same mesh matched, the same texture resolved in another mesh, and " +
                      "shapes the mod author gave identical shader settings.")]
    public bool InferFromOtherShapes = true;

    [SynthesisOrder]
    [SynthesisSettingName("Infer from weapon material")]
    [SynthesisTooltip("For shapes with the mod's own textures: use the settings vanilla weapons of the same " +
                      "material keyword (WeapMaterialSteel, ...) and same shader type use, when they clearly agree.")]
    public bool InferFromWeaponMaterial = true;

    [SynthesisOrder]
    [SynthesisSettingName("Use most common when vanilla disagrees")]
    [SynthesisTooltip("Last resort for shapes still ambiguous after inference: take the most common vanilla " +
                      "version even without a clear majority.")]
    public bool UseMostCommonWhenAmbiguous = false;

    [SynthesisOrder]
    [SynthesisSettingName("Fill empty texture slots")]
    [SynthesisTooltip("When a shape matches vanilla by texture, fill its empty texture slots (cubemap, " +
                      "environment mask, glow) from the vanilla texture set. Never done for inferred matches.")]
    public bool FillEmptyTextureSlots = true;

    [SynthesisOrder]
    [SynthesisSettingName("Include Creation Club weapons as reference")]
    public bool IncludeCreationClub = true;

    [SynthesisOrder]
    [SynthesisSettingName("Extra reference plugins")]
    [SynthesisTooltip("Plugins whose weapons also count as reference, beside the base game, DLC and Creation Club.")]
    public List<ModKey> ExtraReferencePlugins = [];
}
