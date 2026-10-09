using Quiver.Library;
using Quiver.Library.Models;

static void Check(bool condition, string caseName)
{
    if (!condition)
    {
        throw new Exception($"Rule matching failed: {caseName}");
    }
}

const string url = "https://docs.example.com/path";
Check(RuleMatch.CheckRule(url, "s$https://docs.example.com/path"), "exact string");
Check(!RuleMatch.CheckRule(url, "s$https://DOCS.example.com/path"), "string casing");
Check(RuleMatch.CheckRule(url, "d$*.example.com"), "wildcard subdomain");
Check(RuleMatch.CheckRule("https://example.com/", "d$*.example.com"), "wildcard apex");
Check(!RuleMatch.CheckRule("https://badexample.com/", "d$*.example.com"), "domain boundary");
Check(!RuleMatch.CheckRule("not a url", "d$example.com"), "invalid absolute URL");
Check(RuleMatch.CheckRule("https://example.com/abab", @"r$(ab)\1"), "regex backreference");
Check(!RuleMatch.CheckRule(url, "r$("), "invalid regex");

var editedRule = new Rule(@"^https://docs\.example\.com/other$", "Regex");
Check(!RuleMatch.CheckRule(url, editedRule), "regex before edit");
editedRule.RuleContent = @"^https://docs\.example\.com/path$";
Check(RuleMatch.CheckRule(url, editedRule), "regex after edit");

var manyRules = Enumerable.Range(0, 300).Select(i => $"s$https://example.com/{i}").ToList();
Check(RuleMatch.CheckMultiple("https://example.com/299", manyRules), "match after cache capacity");
Check(RuleMatch.CheckRule("https://example.com/0", manyRules[0]), "evicted rule still matches");

var first = new Ruleset { Rules = ["r$(", "d$*.example.com"] };
var second = new Ruleset { Rules = ["d$docs.example.com"] };
Check(ReferenceEquals(RuleMatch.CheckRulesets(url, [first, second]), first), "first ruleset wins");
Check(RuleMatch.CheckMultiple(url, ["r$(", "d$docs.example.com"]), "later rule after invalid regex");

var hostile = "https://example.com/" + new string('a', 4096) + "!";
Check(RuleMatch.CheckMultiple(hostile, [@"r$^https://example\.com/(a+)+$", "d$example.com"]), "later rule after regex timeout");
Console.WriteLine("Rule matching smoke checks passed.");

if (args.Contains("--measure"))
{
    var rules = new List<string> { "d$*.example.com", @"r$^https://docs\.example\.com/path$" };
    for (var i = 0; i < 1_000; i++) RuleMatch.CheckMultiple(url, rules);
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 10_000; i++) RuleMatch.CheckMultiple(url, rules);
    timer.Stop();
    Console.WriteLine($"10,000 repeated checks: {timer.ElapsedMilliseconds} ms, {GC.GetAllocatedBytesForCurrentThread() - allocatedBefore} allocated bytes (local measurement).");
}
