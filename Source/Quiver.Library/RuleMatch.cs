using Quiver.Library.Models;
using System.Text.RegularExpressions;

namespace Quiver.Library;

public class RuleMatch
{
    private const int CacheLimit = 256;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);
    private static readonly Dictionary<string, CachedRule> RuleCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> CacheOrder = new();
    private static readonly object CacheLock = new();

    internal sealed record CachedRule(Rule Rule, Regex? Regex);

    public static PreparedRulesets PrepareRulesets(IEnumerable<Ruleset> rulesets) => new(rulesets);

    public static Ruleset? CheckRulesets(string link, List<Ruleset> rulesets)
    {
        Uri.TryCreate(link, UriKind.Absolute, out var uri);
        return rulesets.FirstOrDefault(ruleset => ruleset.Rules is { } rules
            && CheckMultiple(link, uri, rules));
    }

    public static bool CheckMultiple(string link, List<string> rules)
    {
        Uri.TryCreate(link, UriKind.Absolute, out var uri);
        return CheckMultiple(link, uri, rules);
    }

    private static bool CheckMultiple(string link, Uri? uri, List<string> rules)
    {
        foreach (var rule in rules)
        {
            if (rule != null && CheckRule(link, uri, GetCachedRule(rule)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool CheckRule(string link, string? rule)
    {
        if (rule == null)
        {
            return false;
        }

        Uri.TryCreate(link, UriKind.Absolute, out var uri);
        return CheckRule(link, uri, GetCachedRule(rule));
    }

    public static bool CheckRule(string link, Rule rule)
    {
        Uri.TryCreate(link, UriKind.Absolute, out var uri);
        return CheckRule(link, uri, rule.Mode == RuleMode.Regex
            ? GetCachedRule("r$" + rule.RuleContent)
            : new CachedRule(rule, null));
    }

    internal static bool CheckRule(string link, Uri? uri, CachedRule cached)
    {
        try
        {
            return cached.Rule.Mode switch
            {
                RuleMode.Domain => uri != null && DomainMatches(uri.Host, cached.Rule.RuleContent),
                RuleMode.Regex => cached.Regex?.IsMatch(link) == true,
                _ => link.Equals(cached.Rule.RuleContent)
            };
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public static bool DomainCheck(string link, string rule)
    {
        return Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && DomainMatches(uri.Host, rule);
    }

    private static bool DomainMatches(string domain, string rule)
    {
        if (rule.StartsWith("*.", StringComparison.Ordinal))
        {
            var host = domain.AsSpan();
            var baseDomain = rule.AsSpan(2);
            return host.Equals(baseDomain, StringComparison.OrdinalIgnoreCase)
                || (host.Length > baseDomain.Length
                    && host[host.Length - baseDomain.Length - 1] == '.'
                    && host.EndsWith(baseDomain, StringComparison.OrdinalIgnoreCase));
        }

        return domain.Equals(rule, StringComparison.OrdinalIgnoreCase);
    }

    private static CachedRule GetCachedRule(string storedRule)
    {
        lock (CacheLock)
        {
            if (RuleCache.TryGetValue(storedRule, out var cached))
            {
                return cached;
            }

            cached = PrepareRule(storedRule);
            if (RuleCache.Count == CacheLimit)
            {
                RuleCache.Remove(CacheOrder.Dequeue());
            }
            RuleCache.Add(storedRule, cached);
            CacheOrder.Enqueue(storedRule);
            return cached;
        }
    }

    internal static CachedRule PrepareRule(string storedRule)
    {
        var rule = new Rule(storedRule);
        return new CachedRule(rule, rule.Mode == RuleMode.Regex ? CreateRegex(rule.RuleContent) : null);
    }

    private static Regex? CreateRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.None, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
