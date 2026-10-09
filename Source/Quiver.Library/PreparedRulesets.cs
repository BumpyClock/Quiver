using Quiver.Library.Models;

namespace Quiver.Library;

public sealed record RulesetMatch(Guid Id, string RulesetName, Guid BrowserId, Guid? AlternateLaunchId);

/// <summary>An immutable copy of ordered rules, with regex preparation deferred until matching.</summary>
public sealed class PreparedRulesets
{
    private sealed record Entry(RulesetMatch Match, Lazy<RuleMatch.CachedRule>[] Rules);
    private readonly Entry[] entries;

    internal PreparedRulesets(IEnumerable<Ruleset> rulesets)
    {
        entries = rulesets.Select(ruleset => new Entry(
            new RulesetMatch(ruleset.Id, ruleset.RulesetName, ruleset.BrowserId, ruleset.AlternateLaunchId),
            (ruleset.Rules ?? []).Where(rule => rule is not null)
                .Select(rule => new Lazy<RuleMatch.CachedRule>(() => RuleMatch.PrepareRule(rule)))
                .ToArray())).ToArray();
    }

    public RulesetMatch? Check(string link, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Uri.TryCreate(link, UriKind.Absolute, out var uri);
        foreach (var entry in entries)
        {
            foreach (var rule in entry.Rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches = RuleMatch.CheckRule(link, uri, rule.Value);
                cancellationToken.ThrowIfCancellationRequested();
                if (matches)
                {
                    return entry.Match;
                }
            }
        }
        return null;
    }
}
