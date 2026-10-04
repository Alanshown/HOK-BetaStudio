using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hok.Catalog;

public sealed record CatalogEntry(string Kind, string Id, string HeroId, string Hero, string Name, string ImageUrl, string Status = "current");
public sealed record CatalogIndex(int SchemaVersion, List<CatalogEntry> Records)
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    public string Revision => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(Records.OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal), Json))));

    public static CatalogIndex Read(string text)
    {
        var value = JsonSerializer.Deserialize<CatalogIndex>(text, Json) ?? throw new InvalidDataException("Empty resource index.");
        value.Validate();
        return value;
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || Records is null || Records.Count is < 1 or > 50000 || !Records.Any(e => e?.Kind == "hero"))
            throw new InvalidDataException("Unsupported or empty resource index.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in Records)
        {
            if (e is null || !ValidEntry(e) || !keys.Add(e.Kind + ":" + e.Id))
                throw new InvalidDataException("Invalid or duplicate resource identity.");
        }
        var heroes = Records.Where(e => e.Kind == "hero").Select(e => e.Id).ToHashSet();
        if (Records.Any(e => !heroes.Contains(e.HeroId))) throw new InvalidDataException("Skin refers to an unknown hero.");
    }

    public static bool SafeImage(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host is "game-1255653016.file.myqcloud.com" or "image.smoba.qq.com";

    static bool ValidEntry(CatalogEntry e) => e.Kind is "hero" or "skin"
        && e.HeroId is not null && e.Id is not null && Regex.IsMatch(e.HeroId, "^[0-9]{3}$")
        && (e.Kind == "hero" ? e.Id == e.HeroId : Regex.IsMatch(e.Id, "^[0-9]{5}$") && e.Id.StartsWith(e.HeroId, StringComparison.Ordinal))
        && !string.IsNullOrWhiteSpace(e.Name) && e.Name.Length <= 200
        && !string.IsNullOrWhiteSpace(e.Hero) && e.Hero.Length <= 200
        && e.Status is "current" or "missing_from_source" or "quarantined" && SafeImage(e.ImageUrl);

    public static CatalogIndex Normalize(string catalog, string heroList, string rules, CatalogIndex previous, List<string> issues)
    {
        var raw = JsonNode.Parse(catalog) ?? throw new InvalidDataException("Empty official catalog.");
        var heroes = raw["yxlb20_2489"] as JsonArray;
        var skins = raw["pflb20_3469"] as JsonArray;
        var names = JsonNode.Parse(heroList) as JsonArray;
        var corrections = JsonNode.Parse(rules) ?? throw new InvalidDataException("Missing correction rules.");
        if (heroes is not { Count: > 0 } || skins is not { Count: > 0 } || names is not { Count: > 0 }
            || corrections["schema_version"]?.GetValue<int>() != 1)
            throw new InvalidDataException("Official source schema changed; the applied index was preserved.");
        static string S(JsonNode? n, string key) => n?[key]?.ToString().Trim() ?? "";
        var nameIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in names)
        {
            string name = S(row, "cname"), id = S(row, "ename");
            if (name.Length == 0 || !Regex.IsMatch(id, "^[0-9]{3}$")) throw new InvalidDataException("Invalid official hero list.");
            if (nameIds.TryGetValue(name, out var existing) && existing != id) throw new InvalidDataException("Ambiguous official hero name.");
            nameIds[name] = id;
        }
        var proposed = new List<CatalogEntry>();
        foreach (var row in heroes)
        {
            string id = S(row, "yxid_a7"), name = S(row, "yxmclb_9965");
            if (!nameIds.TryGetValue(name, out var expected) || id != expected) { issues.Add("Hero identity mismatch: " + id); continue; }
            proposed.Add(new("hero", id, id, name, name, S(row, "yxtxlb_8443")));
        }
        foreach (var row in skins)
        {
            string id = S(row, "pfidlb_3934"), name = S(row, "pfmclb_7523"), hero = S(row, "yxmclb_9965");
            // Rules must match the complete original tuple; never guess an ID from artwork.
            foreach (var rule in corrections["corrections"]?.AsArray() ?? [])
            {
                if (rule?["enabled"]?.GetValue<bool>() != true) continue;
                var match = rule["match"];
                if (S(match, "id") != id || S(match, "name") != name || S(match, "hero") != hero) continue;
                var set = rule["set"];
                if (set?["id"] is not null) id = S(set, "id");
                if (set?["name"] is not null) name = S(set, "name");
                if (set?["hero"] is not null) hero = S(set, "hero");
                break;
            }
            if (!nameIds.TryGetValue(hero, out var heroId)) { issues.Add("Unmapped skin: " + id); continue; }
            proposed.Add(new("skin", id, heroId, hero, name, S(row, "yxtxlb_8443")));
        }
        foreach (var row in corrections["supplements"]?.AsArray() ?? [])
        {
            string id = S(row, "id"), hero = S(row, "hero"), name = S(row, "name");
            if (row?["enabled"]?.GetValue<bool>() != true || !nameIds.TryGetValue(hero, out var heroId)
                || proposed.Any(e => e.Kind == "skin" && (e.Id == id || e.HeroId == heroId && e.Name == name))) continue;
            proposed.Add(new("skin", id, heroId, hero, name, S(row, "image_url")));
        }
        var confirmed = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        var conflicts = new HashSet<string>();
        foreach (var e in proposed)
        {
            string key = e.Kind + ":" + e.Id;
            if (!ValidEntry(e)) { issues.Add("Rejected invalid resource: " + key); continue; }
            if (conflicts.Contains(key)) continue;
            if (confirmed.TryGetValue(key, out var existing) && existing != e)
            { conflicts.Add(key); confirmed.Remove(key); issues.Add("Quarantined conflicting resource: " + key); }
            else confirmed[key] = e;
        }
        var verifiedHeroes = confirmed.Values.Where(e => e.Kind == "hero").Select(e => e.Id).ToHashSet();
        foreach (var key in confirmed.Where(p => !verifiedHeroes.Contains(p.Value.HeroId)).Select(p => p.Key).ToArray()) confirmed.Remove(key);
        // A truncated but syntactically valid response must not replace most of the collection.
        foreach (var kind in new[] { "hero", "skin" })
        {
            int count = confirmed.Values.Count(e => e.Kind == kind);
            if (count == 0 || count < previous.Records.Count(e => e.Kind == kind && e.Status == "current") * .75)
                throw new InvalidDataException("Incomplete official " + kind + " collection; update rejected.");
        }
        foreach (var old in previous.Records)
        {
            string key = old.Kind + ":" + old.Id;
            if (!confirmed.ContainsKey(key)) confirmed[key] = old with { Status = conflicts.Contains(key) ? "quarantined" : "missing_from_source" };
        }
        var result = new CatalogIndex(1, confirmed.Values.OrderBy(e => e.Kind).ThenBy(e => e.Id, StringComparer.Ordinal).ToList());
        result.Validate();
        return result;
    }
}
