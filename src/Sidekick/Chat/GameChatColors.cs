using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.Text;
using Lumina.Excel.Sheets;

namespace Sidekick.Chat;

// Values crossing this boundary are explicit 0xAARRGGBB, including alpha.
internal interface IChatColors
{
    uint? Channel(XivChatType channel);
    uint? Ui(uint id, bool edge);
    uint? Global(uint oneBasedIndex);
}

internal sealed class GameChatColors : IChatColors
{
    private readonly IGameConfig gameConfig;
    private readonly Dictionary<uint, (uint Foreground, uint Edge)> colors;

    internal GameChatColors(IGameConfig gameConfig, IDataManager data)
    {
        this.gameConfig = gameConfig;
        // UIColor stores RRGGBBAA, whereas Color macros contain AARRGGBB.
        colors = data.GetExcelSheet<UIColor>().ToDictionary(row => row.RowId,
            row => (FromSheet(row.Dark), FromSheet(row.Light)));
    }

    internal static uint FromSheet(uint rgba) => (rgba >> 8) | (rgba << 24);

    public uint? Ui(uint id, bool edge) => colors.TryGetValue(id, out var color)
        ? edge ? color.Edge : color.Foreground : null;

    public unsafe uint? Global(uint oneBasedIndex)
    {
        var module = RaptureTextModule.Instance();
        if (module == null || oneBasedIndex == 0 || oneBasedIndex > module->GlobalParameters.LongCount)
            return null;
        ref var parameter = ref module->GlobalParameters[(long)oneBasedIndex - 1];
        if (parameter.Type != TextParameterType.Integer)
            return null;
        return unchecked((uint)parameter.IntValue);
    }

    public uint? Channel(XivChatType channel)
    {
        var option = ChannelOption(channel);
        return option is not null && gameConfig.UiConfig.TryGetUInt(option, out var rgb)
            ? 0xFF000000u | (rgb & 0xFFFFFFu) : null;
    }

    internal static string? ChannelOption(XivChatType channel) => channel switch
    {
        XivChatType.Say => "ColorSay",
        XivChatType.Shout => "ColorShout",
        XivChatType.Yell => "ColorYell",
        XivChatType.CustomEmote => "ColorEmoteUser",
        XivChatType.StandardEmote => "ColorEmote",
        XivChatType.TellIncoming or XivChatType.TellOutgoing => "ColorTell",
        XivChatType.Party or XivChatType.CrossParty => "ColorParty",
        XivChatType.Alliance => "ColorAlliance",
        XivChatType.FreeCompany => "ColorFCompany",
        XivChatType.NoviceNetwork => "ColorBeginner",
        XivChatType.PvPTeam => "ColorPvPGroup",
        XivChatType.Ls1 => "ColorLS1",
        XivChatType.Ls2 => "ColorLS2",
        XivChatType.Ls3 => "ColorLS3",
        XivChatType.Ls4 => "ColorLS4",
        XivChatType.Ls5 => "ColorLS5",
        XivChatType.Ls6 => "ColorLS6",
        XivChatType.Ls7 => "ColorLS7",
        XivChatType.Ls8 => "ColorLS8",
        XivChatType.CrossLinkShell1 => "ColorCWLS",
        XivChatType.CrossLinkShell2 => "ColorCWLS2",
        XivChatType.CrossLinkShell3 => "ColorCWLS3",
        XivChatType.CrossLinkShell4 => "ColorCWLS4",
        XivChatType.CrossLinkShell5 => "ColorCWLS5",
        XivChatType.CrossLinkShell6 => "ColorCWLS6",
        XivChatType.CrossLinkShell7 => "ColorCWLS7",
        XivChatType.CrossLinkShell8 => "ColorCWLS8",
        _ => null,
    };
}
