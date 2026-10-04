using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hok.Catalog;

string root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".");
string seedPath = Path.Combine(root, "assets/catalog/remote-index.seed.json");
string rulesPath = Path.Combine(root, "assets/catalog/corrections.default.json");
var seed = CatalogIndex.Read(File.ReadAllText(seedPath));
string rules = File.ReadAllText(rulesPath);
int passed = 0;
void Assert(bool success, string message) { if (!success) throw new Exception(message); Console.WriteLine("PASS " + message); passed++; }
void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException) { Assert(true, message); return; } throw new Exception("Accepted: " + message); }
async Task RejectAsync(Func<Task> action, string message) { try { await action(); } catch (InvalidOperationException) { Assert(true, message); return; } throw new Exception("Accepted: " + message); }
var canonical = JsonSerializer.Serialize(seed, CatalogIndex.Json);
Assert(seed.Records.Count == 962, "bundled URL seed has 133 heroes and 829 skins");
Assert(seed.Records.All(e => CatalogIndex.SafeImage(e.ImageUrl)), "all seed images use approved HTTPS hosts");
Assert(new CatalogIndex(1, seed.Records.AsEnumerable().Reverse().ToList()).Revision == seed.Revision, "record order does not trigger sync");
Assert(CatalogIndex.Read(canonical.Replace("\"schema_version\": 1", "\"schema_version\": 1, \"generated_at\": \"2099-01-01\"")).Revision == seed.Revision, "metadata does not trigger sync");
Reject(() => new CatalogIndex(2, seed.Records).Validate(), "reject unknown schema");
Reject(() => new CatalogIndex(1, []).Validate(), "reject empty seed");
Reject(() => new CatalogIndex(1, [.. seed.Records, seed.Records[0]]).Validate(), "reject duplicate identity");
Reject(() => new CatalogIndex(1, [seed.Records[0] with { ImageUrl = "http://image.smoba.qq.com/a.png" }]).Validate(), "reject HTTP artwork");
Assert(!CatalogIndex.SafeImage("https://image.smoba.qq.com.evil.test/a") && !CatalogIndex.SafeImage("https://user@image.smoba.qq.com/a") && !CatalogIndex.SafeImage("https://image.smoba.qq.com:999/a"), "reject image host spoofing, credentials and custom port");
Reject(() => new CatalogIndex(1, [seed.Records.First(e => e.Kind == "hero"), seed.Records.First(e => e.Kind == "skin") with { HeroId = "999" }]).Validate(), "reject mismatched skin/hero ID");

JsonObject Source() => new()
{
    ["yxlb20_2489"] = new JsonArray(seed.Records.Where(e => e.Kind == "hero").Select(e => (JsonNode)new JsonObject { ["yxid_a7"] = e.Id, ["yxmclb_9965"] = e.Name, ["yxtxlb_8443"] = e.ImageUrl }).ToArray()),
    ["pflb20_3469"] = new JsonArray(seed.Records.Where(e => e.Kind == "skin").Select(e => (JsonNode)new JsonObject { ["pfidlb_3934"] = e.Id, ["pfmclb_7523"] = e.Name, ["yxmclb_9965"] = e.Hero, ["yxtxlb_8443"] = e.ImageUrl }).ToArray())
};
var names = new JsonArray(seed.Records.Where(e => e.Kind == "hero").Select(e => (JsonNode)new JsonObject { ["ename"] = int.Parse(e.Id), ["cname"] = e.Name }).ToArray());
string namesJson = names.ToJsonString();
CatalogIndex Normalize(JsonObject source, CatalogIndex? previous = null) => CatalogIndex.Normalize(source.ToJsonString(), namesJson, rules, previous ?? seed, []);
Assert(Normalize(Source()).Revision == seed.Revision, "normalized seed matches canonical index exactly");
var corrected = Source();
foreach (var row in corrected["pflb20_3469"]!.AsArray())
{
    string id = row!["pfidlb_3934"]!.ToString();
    if (id == "12407") row["pfidlb_3934"] = "132";
    if (id == "11401") row["pfidlb_3934"] = "11402";
    if (id is "10609" or "16706") { string hero = row["yxmclb_9965"]!.ToString(); row["yxmclb_9965"] = row["pfmclb_7523"]!.ToString(); row["pfmclb_7523"] = hero; }
}
Assert(Normalize(corrected).Revision == seed.Revision, "four exact correction rules repair known official anomalies");
var supplemented = Source();
foreach (var row in supplemented["pflb20_3469"]!.AsArray().Where(e => e!["pfidlb_3934"]!.ToString() is "14402" or "19501").ToArray()) supplemented["pflb20_3469"]!.AsArray().Remove(row);
Assert(Normalize(supplemented).Revision == seed.Revision, "two explicit supplements fill missing entries");
var changed = Source();
changed["pflb20_3469"]![0]!["pfmclb_7523"] = "Updated skin name";
var changedIndex = Normalize(changed);
Assert(CatalogService.Difference(seed, changedIndex) is { Added: 0, Changed: 1 }, "name change detected");
var imageChange = Source(); imageChange["yxlb20_2489"]![0]!["yxtxlb_8443"] = "https://image.smoba.qq.com/changed.png";
Assert(CatalogService.Difference(seed, Normalize(imageChange)) is { Changed: 1 }, "image URL change detected");
var duplicate = Source(); duplicate["pflb20_3469"]!.AsArray().Add(duplicate["pflb20_3469"]![0]!.DeepClone());
Assert(Normalize(duplicate).Revision == seed.Revision, "identical duplicates do not trigger sync");
duplicate["pflb20_3469"]!.AsArray().Last()!["pfmclb_7523"] = "Conflicting name";
Assert(Normalize(duplicate).Records.Count(e => e.Status == "quarantined") == 1, "conflicting identity is quarantined, not overwritten");
var removed = Source(); string removedId = removed["pflb20_3469"]![0]!["pfidlb_3934"]!.ToString(); removed["pflb20_3469"]!.AsArray().RemoveAt(0);
var retained = Normalize(removed);
Assert(retained.Records.Single(e => e.Kind == "skin" && e.Id == removedId).Status == "missing_from_source", "temporarily missing skin retained");
Assert(Normalize(removed, retained).Revision == retained.Revision, "retained missing entries do not cause repeated updates");
var incomplete = Source(); incomplete["pflb20_3469"] = new JsonArray(incomplete["pflb20_3469"]![0]!.DeepClone());
Reject(() => Normalize(incomplete), "reject truncated official collection");
Reject(() => CatalogIndex.Normalize("{}", namesJson, rules, seed, []), "reject official schema change");
var badNames = names.DeepClone().AsArray(); badNames.Add(new JsonObject { ["ename"] = 999, ["cname"] = names[0]!["cname"]!.ToString() });
Reject(() => CatalogIndex.Normalize(Source().ToJsonString(), badNames.ToJsonString(), rules, seed, []), "ambiguous hero mapping rejected");
var newSource = Source(); var newNames = names.DeepClone().AsArray();
newNames.Add(new JsonObject { ["ename"] = 999, ["cname"] = "New hero" });
newSource["yxlb20_2489"]!.AsArray().Add(new JsonObject { ["yxid_a7"] = "999", ["yxmclb_9965"] = "New hero", ["yxtxlb_8443"] = "https://image.smoba.qq.com/999.png" });
newSource["pflb20_3469"]!.AsArray().Add(new JsonObject { ["pfidlb_3934"] = "99901", ["pfmclb_7523"] = "New skin", ["yxmclb_9965"] = "New hero", ["yxtxlb_8443"] = "https://image.smoba.qq.com/99901.png" });
Assert(CatalogService.Difference(seed, CatalogIndex.Normalize(newSource.ToJsonString(), newNames.ToJsonString(), rules, seed, [])) is { Added: 2, Changed: 0 }, "new hero and skin detected without guessing IDs");

string temp = Path.Combine(Path.GetTempPath(), "Hok-Catalog-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
Console.WriteLine("Isolated test data: " + temp);
var handler = new SourceHandler { Catalog = Source().ToJsonString(), Heroes = namesJson };
using var client = new HttpClient(handler);
using var service = new CatalogService(temp, seedPath, rulesPath, client);
Assert((await service.LoadAsync()).Revision == seed.Revision, "first run bootstraps bundled index locally");
Assert(await service.CheckAsync() is null, "identical source exposes no sync button");
Assert(handler.Requests == 2, "two official sources fetched");
Assert(await service.CheckAsync() is null && handler.Conditional == 2, "conditional 304 uses validated cached response");
handler.Catalog = changed.ToJsonString(); handler.Etag = "\"changed\"";
var pending = await service.CheckAsync();
Assert(pending is { Changed: 1 }, "background check stages changed index");
Assert(CatalogIndex.Read(File.ReadAllText(Path.Combine(temp, "index.json"))).Revision == seed.Revision && (await service.LoadAsync()).Revision == seed.Revision, "background check never changes applied index or current window");
await RejectAsync(() => service.ApplyAsync("stale-revision"), "reject stale apply token");
handler.Fail = true;
Assert(await service.CheckAsync() is { Changed: 1 }, "offline retains validated pending update");
var applied = await service.ApplyAsync(pending!.Revision);
Assert(applied.Revision == changedIndex.Revision, "explicit apply commits staged index");
Assert(CatalogIndex.Read(File.ReadAllText(Path.Combine(temp, "index.backup.json"))).Revision == seed.Revision, "atomic apply preserves previous index as backup");
Assert(await service.CheckAsync() is null, "offline after apply exposes no stale button");
using (var restarted = new CatalogService(temp, seedPath, rulesPath, client)) Assert((await restarted.LoadAsync()).Revision == applied.Revision, "restart reads the applied local index without network");
handler.Fail = false; handler.Catalog = "{}"; handler.Etag = "\"invalid\"";
Assert(await service.CheckAsync() is null, "bad source remains invisible");
Assert(CatalogIndex.Read(File.ReadAllText(Path.Combine(temp, "index.json"))).Revision == applied.Revision, "bad source cannot replace applied index");
string recovery = Path.Combine(temp, "recovery"); Directory.CreateDirectory(recovery);
File.WriteAllText(Path.Combine(recovery, "index.json"), "corrupt");
File.WriteAllText(Path.Combine(recovery, "index.backup.json"), canonical);
using (var recovered = new CatalogService(recovery, seedPath, rulesPath, client)) Assert((await recovered.LoadAsync()).Revision == seed.Revision, "corrupt local index recovers from backup");
Assert(!Directory.EnumerateFiles(temp, "*.tmp").Any(), "no partial temporary writes left behind");

if (args.Contains("--live"))
{
    string livePath = Path.Combine(temp, "live");
    using var live = new CatalogService(livePath, seedPath, rulesPath);
    await live.LoadAsync();
    var update = await live.CheckAsync();
    Console.WriteLine("LIVE " + JsonSerializer.Serialize(update));
    Assert(File.Exists(Path.Combine(livePath, "source-catalog.json")) && File.Exists(Path.Combine(livePath, "source-heroes.json")), "live official sources fetched and normalized successfully");
    Assert(CatalogIndex.Read(File.ReadAllText(Path.Combine(livePath, "index.json"))).Revision == seed.Revision, "live check did not auto-apply changes");
}
Console.WriteLine($"All {passed} catalog checks passed.");

sealed class SourceHandler : HttpMessageHandler
{
    public string Catalog = "", Heroes = "", Etag = "\"initial\"";
    public bool Fail;
    public int Requests, Conditional;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (Fail) throw new HttpRequestException("Offline test", null, HttpStatusCode.Forbidden);
        if (request.Headers.IfNoneMatch.Any(e => e.ToString() == Etag))
        { Conditional++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)); }
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsoluteUri == CatalogService.CatalogUrl ? Catalog : Heroes, Encoding.UTF8, "application/json") };
        response.Headers.ETag = new(Etag);
        return Task.FromResult(response);
    }
}
