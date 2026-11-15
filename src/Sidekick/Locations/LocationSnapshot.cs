namespace Sidekick.Locations;

// User-facing housing numbers are one-based; unavailable data stays null.
internal sealed record LocationSnapshot(uint TerritoryId, uint WorldId, uint PublicInstanceId,
    uint? HousingDistrictId = null, int? Ward = null, int? Plot = null, int? Room = null,
    int? Division = null, bool? Indoors = null)
{
    internal LocationSnapshot WithHousing(uint districtId, int wardIndex, int plotIndex, int room,
        int division, bool indoors)
    {
        if (wardIndex is < 0 or >= 30)
            return this;
        var effectiveDivision = plotIndex is -128 or -127 ? plotIndex + 129
            : indoors && plotIndex >= 0 ? (plotIndex >= 30 ? 2 : 1) : division;
        return this with
        {
            HousingDistrictId = districtId is 0 or ushort.MaxValue ? null : districtId,
            Ward = wardIndex + 1,
            Plot = plotIndex is >= 0 and < 60 ? plotIndex + 1 : null,
            Room = room is > 0 and <= 1023 ? room : null,
            Division = effectiveDivision is 1 or 2 ? effectiveDivision : null,
            Indoors = indoors,
        };
    }
}

public sealed record LocationCondition
{
    public uint? TerritoryId { get; set; }
    public uint? WorldId { get; set; }
    public uint? PublicInstanceId { get; set; }
    public uint? HousingDistrictId { get; set; }
    public int? Ward { get; set; }
    public int? Plot { get; set; }
    public int? Room { get; set; }
    public int? Division { get; set; }
    public bool? Indoors { get; set; }

    internal bool IsValid => (TerritoryId.HasValue || WorldId.HasValue || PublicInstanceId.HasValue
        || HousingDistrictId.HasValue || Ward.HasValue || Plot.HasValue || Room.HasValue
        || Division.HasValue || Indoors.HasValue)
        && TerritoryId is not 0 && WorldId is not 0 && HousingDistrictId is not 0
        && PublicInstanceId is null or >= 1 and <= 9
        && Ward is null or >= 1 and <= 30 && Plot is null or >= 1 and <= 60
        && Room is null or >= 1 and <= 1023 && Division is null or 1 or 2;

    internal bool Matches(LocationSnapshot location) => IsValid
        // A residential district contains its house/room interiors even though the client
        // gives those interiors separate territory IDs. Exact interior selections still work.
        && (!TerritoryId.HasValue || TerritoryId == location.TerritoryId || TerritoryId == location.HousingDistrictId)
        && (!WorldId.HasValue || WorldId == location.WorldId)
        && (!PublicInstanceId.HasValue || PublicInstanceId == location.PublicInstanceId)
        && (!HousingDistrictId.HasValue || HousingDistrictId == location.HousingDistrictId)
        && (!Ward.HasValue || Ward == location.Ward)
        && (!Plot.HasValue || Plot == location.Plot)
        && (!Room.HasValue || Room == location.Room)
        && (!Division.HasValue || Division == location.Division)
        && (!Indoors.HasValue || Indoors == location.Indoors);

    internal static LocationCondition Capture(LocationSnapshot location) => new()
    {
        // Capture the residential district so entering another room or renovating the
        // interior does not change the address. Non-housing territories remain exact.
        TerritoryId = location.HousingDistrictId ?? location.TerritoryId,
        WorldId = location.WorldId,
        PublicInstanceId = location.PublicInstanceId == 0 ? null : location.PublicInstanceId,
        HousingDistrictId = location.HousingDistrictId,
        Ward = location.Ward,
        Plot = location.Plot,
        Room = location.Room is > 0 ? location.Room : null,
        Division = location.Division,
        Indoors = location.Indoors,
    };
}
