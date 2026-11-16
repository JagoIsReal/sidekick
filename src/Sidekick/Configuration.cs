using Dalamud.Configuration;
using Sidekick.Core;
using Sidekick.Keywords;
using Sidekick.Mods;
using Sidekick.Focus;
using Sidekick.Localization;
using Sidekick.Rules;
using Sidekick.Appearance;

namespace Sidekick;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 9;

    public int Version { get; set; } = CurrentVersion;
    public bool Enabled { get; set; } = true;
    public string LanguageCode { get; set; } = LanguagePreference.DefaultCode;

    // Explicit 0xRRGGBB representation, independent of ImGui/native byte ordering.
    public uint HighlightColorRgb { get; set; } = RgbColor.DefaultRgb;
    public KeywordSettings Keywords { get; set; } = new();
    public ModRuleSettings ModRules { get; set; } = new();
    public FocusSettings Focus { get; set; } = new();
    public RuleProfileSettings RuleProfiles { get; set; } = new();
    public GlamourerSettings Glamourer { get; set; } = new();

    public void ValidateAndMigrate()
    {
        if (Version < 0 || Version > CurrentVersion)
            throw new NotSupportedException("Unsupported Sidekick configuration version.");

        if (HighlightColorRgb > 0xFFFFFF)
            HighlightColorRgb = RgbColor.DefaultRgb;
        LanguageCode = LanguagePreference.Normalize(LanguageCode);

        Keywords ??= new KeywordSettings();
        Keywords.Validate();
        ModRules ??= new ModRuleSettings();
        ModRules.Validate();
        Focus ??= new FocusSettings();
        Focus.Validate();
        RuleProfiles ??= new RuleProfileSettings();
        RuleProfiles.Validate();
        Glamourer ??= new GlamourerSettings();
        Glamourer.Validate();

        // Additive migrations preserve chat preferences and keep mod rules opt-in.
        Version = CurrentVersion;
    }

    public void Reset()
    {
        Enabled = true;
        HighlightColorRgb = RgbColor.DefaultRgb;
    }
}

