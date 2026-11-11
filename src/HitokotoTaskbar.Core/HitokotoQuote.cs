using System.Text.Json.Serialization;

namespace HitokotoTaskbar.Core;

/// <summary>A last-known-good sentence, retained across network failures and restarts.</summary>
public sealed record HitokotoQuote
{
    public string Text { get; init; } = "";
    public string? Source { get; init; }
    public string? Author { get; init; }
    public string? Uuid { get; init; }
    public DateTimeOffset FetchedAt { get; init; }

    [JsonIgnore]
    public string TooltipText
    {
        get
        {
            var credit = string.Join(" · ", new[] { Author, Source }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return credit.Length == 0 ? Text : $"{Text}\n—— {credit}";
        }
    }
}
