using System.Text;

namespace Sidekick.Core;

public sealed record CharacterIdentity
{
    private CharacterIdentity(string displayName, string firstName, string lastName, uint homeWorldId)
    {
        DisplayName = displayName;
        FirstName = firstName;
        LastName = lastName;
        HomeWorldId = homeWorldId;
    }

    public string DisplayName { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public uint HomeWorldId { get; }

    public static CharacterIdentity? FromName(string? name, uint homeWorldId = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var parts = name.Trim().Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // FFXIV names contain two components. Do not guess fragments of unusual identities.
        return parts.Length switch
        {
            1 => new CharacterIdentity(parts[0], parts[0], string.Empty, homeWorldId),
            2 => new CharacterIdentity(string.Join(' ', parts), parts[0], parts[1], homeWorldId),
            _ => null,
        };
    }
}

