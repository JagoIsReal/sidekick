namespace Sidekick.Keywords;

[Serializable]
public sealed class KeywordRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public bool Account { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public uint HomeWorldId { get; set; }
    public bool Enabled { get; set; } = true;
}

public enum KeywordEditResult
{
    Success,
    InvalidText,
    Duplicate,
    ProfileUnavailable,
    LimitReached,
    RuleNotFound,
}
