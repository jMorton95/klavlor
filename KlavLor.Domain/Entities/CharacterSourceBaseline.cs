namespace KlavLor.Domain.Entities;

// Admin-set baseline kill count for a character at a source: kills done before we had any
// RuneLite data for them. Added to the *counted* kill total (never to a real reported KillCount)
// so newly-onboarded characters start from a realistic number. Keyed by (GameCharacterId, SourceName).
public sealed class CharacterSourceBaseline
{
    public int GameCharacterId { get; set; }
    public string SourceName { get; set; } = "";
    public int BaselineKc { get; set; }
}
