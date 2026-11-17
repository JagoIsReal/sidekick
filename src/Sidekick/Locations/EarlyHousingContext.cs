using Sidekick.Core;

namespace Sidekick.Locations;

internal sealed record HousingZoneNotice(uint TerritoryId, uint WeatherId);

// Copied values only. No native pointers or player wrappers survive a sample.
internal sealed record OutdoorHousingAddress(uint DistrictId, uint WorldId, int WardIndex, int Division)
{
    internal bool IsValid => DistrictId is > 0 and < ushort.MaxValue && WorldId is > 0 and < ushort.MaxValue
        && WardIndex is >= 0 and < 30 && Division is 1 or 2;
}

internal sealed class EarlyHousingContext
{
    private OutdoorHousingAddress? baseline;
    private bool observedBaseline;
    private bool baselineUnavailable;
    private bool transitioning;
    private bool sawUnavailable;
    private HousingZoneNotice? destination;

    internal void Reset()
    {
        baseline = null;
        observedBaseline = false;
        baselineUnavailable = false;
        transitioning = false;
        sawUnavailable = false;
        destination = null;
    }

    internal LocationSnapshot? Observe(bool loading, CharacterIdentity? character, uint nativeTerritory,
        OutdoorHousingAddress? address, bool outdoorUnavailable, HousingZoneNotice? notice)
    {
        if (!loading)
        {
            baseline = address is { IsValid: true } ? address : null;
            observedBaseline = true;
            baselineUnavailable = outdoorUnavailable;
            transitioning = false;
            sawUnavailable = outdoorUnavailable;
            destination = null;
            return null;
        }
        if (!transitioning)
        {
            transitioning = true;
            sawUnavailable = observedBaseline && baselineUnavailable || outdoorUnavailable;
            destination = null;
        }
        if (notice is not null) destination = notice;
        if (outdoorUnavailable) sawUnavailable = true;

        // ZoneInit alone does not prove HousingManager has replaced the previous ward.
        // Require an observed teardown or a changed address, including same-district travel.
        if (character is null || destination is null || address is not { IsValid: true }
            || nativeTerritory != destination.TerritoryId || address.DistrictId != destination.TerritoryId
            || !(sawUnavailable || observedBaseline && baseline is not null && address != baseline))
            return null;

        return new LocationSnapshot(destination.TerritoryId, address.WorldId, 0,
            HousingDistrictId: address.DistrictId, Ward: address.WardIndex + 1, Division: address.Division,
            Indoors: false, Character: character, WeatherId: destination.WeatherId is > 0 and <= byte.MaxValue
                ? destination.WeatherId : null);
    }
}
