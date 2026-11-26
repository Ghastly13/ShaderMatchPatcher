using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;

namespace ShaderMatchPatcher;

public class Settings
{
    [SynthesisOrder]
    [SynthesisSettingName("Target mod")]
    [SynthesisTooltip("The one plugin whose weapon meshes get patched (e.g. LSB_BattleHammersV6.esp). " +
                      "Only weapons this plugin defines are touched, and their loose mesh files are edited in " +
                      "place (originals are backed up first). Leave empty and the patcher does nothing.")]
    public ModKey TargetMod = ModKey.Null;

    [SynthesisOrder]
    [SynthesisSettingName("Restore originals")]
    [SynthesisTooltip("Instead of patching, put back the original of every mesh this patcher changed in the " +
                      "target mod, from its backup. Turn it off again afterwards.")]
    public bool RestoreOriginals = false;

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
