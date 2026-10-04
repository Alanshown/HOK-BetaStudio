using System.Net;
using System.Text;
using System.Text.Json;

namespace Hok.Catalog;

public sealed record CatalogUpdate(string Revision, int Added, int Changed);
public sealed record CatalogCandidate(string BaseRevision, CatalogIndex Index);
sealed record SourceCache(string Body, string? Etag, string? Modified);

/// <summary>Independent of DB workers and session caches. Fetching never applies an index.</summary>
public sealed class CatalogService : IDisposable
{
    public const string CatalogUrl = "https://pvp.qq.com/zlkdatasys/heroskinlist.json";
    public const string HeroesUrl = "https://pvp.qq.com/web201605/js/herolist.json";
    readonly string directory, seed, rules;
    readonly HttpClient http;
    readonly bool ownsHttp;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    CatalogIndex? active;
    public CatalogService(string directory, string seed, string rules, HttpClient? http = null)
    {
        this.directory = directory; this.seed = seed; this.rules = rules;
        ownsHttp = http is null;
        this.http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });
    }
    string PathFor(string file) => Path.Combine(directory, file);
    FileStream Lock() => new(PathFor("catalog.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    public async Task<CatalogIndex> LoadAsync()
    {
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            if (active is not null) return active;
            Directory.CreateDirectory(directory);
            using var fileLock = Lock();
            active = ReadIndex("index.json") ?? ReadIndex("index.backup.json") ?? CatalogIndex.Read(await File.ReadAllTextAsync(seed, lifetime.Token).ConfigureAwait(false));
            if (ReadIndex("index.json") is null) AtomicWrite(PathFor("index.json"), JsonSerializer.Serialize(active, CatalogIndex.Json));
            return active;
        }
        finally { gate.Release(); }
    }
    CatalogIndex? ReadIndex(string name)
    {
        try { return File.Exists(PathFor(name)) ? CatalogIndex.Read(File.ReadAllText(PathFor(name))) : null; }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or ArgumentException)
        { Log("Cannot use " + name + ": " + e.Message); return null; }
    }
    public static CatalogUpdate? Difference(CatalogIndex applied, CatalogIndex candidate)
    {
        if (applied.Revision == candidate.Revision) return null;
        var old = applied.Records.ToDictionary(e => e.Kind + ":" + e.Id);
        int added = 0, changed = 0;
        foreach (var e in candidate.Records)
        {
            if (!old.TryGetValue(e.Kind + ":" + e.Id, out var before)) added++;
            else if (before != e) changed++;
        }
        changed += old.Keys.Except(candidate.Records.Select(e => e.Kind + ":" + e.Id)).Count();
        return new(candidate.Revision, added, changed);
    }
    public async Task<CatalogUpdate?> CheckAsync()
    {
        await LoadAsync().ConfigureAwait(false);
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            CatalogIndex baseline;
            using (Lock()) baseline = ReadIndex("index.json") ?? active!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(100));
            var sources = await Task.WhenAll(Fetch(CatalogUrl, "source-catalog.json", timeout.Token), Fetch(HeroesUrl, "source-heroes.json", timeout.Token)).ConfigureAwait(false);
            var issues = new List<string>();
            var candidate = CatalogIndex.Normalize(sources[0].Body, sources[1].Body, await File.ReadAllTextAsync(rules, timeout.Token).ConfigureAwait(false), baseline, issues);
            using (Lock())
            {
                var disk = ReadIndex("index.json") ?? active!;
                if (disk.Revision != baseline.Revision) throw new IOException("Resource index changed in another app instance; check on next launch.");
                AtomicWrite(PathFor("source-catalog.json"), JsonSerializer.Serialize(sources[0]));
                AtomicWrite(PathFor("source-heroes.json"), JsonSerializer.Serialize(sources[1]));
                AtomicWrite(PathFor("candidate.json"), JsonSerializer.Serialize(new CatalogCandidate(baseline.Revision, candidate), CatalogIndex.Json));
            }
            foreach (var issue in issues) Log(issue);
            // Compare with this window, not just disk: another window might have applied an update.
            return Difference(active!, candidate);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log("Background check: " + e.Message);
            // Only a previously validated candidate may remain actionable when offline.
            try { using var fileLock = Lock(); return Pending(); } catch { return null; }
        }
        finally { gate.Release(); }
    }
    CatalogCandidate? ReadCandidate()
    {
        if (!File.Exists(PathFor("candidate.json"))) return null;
        var value = JsonSerializer.Deserialize<CatalogCandidate>(File.ReadAllText(PathFor("candidate.json")), CatalogIndex.Json);
        if (value?.Index is null) throw new InvalidDataException("Empty staged resource index.");
        value.Index.Validate();
        return value;
    }
    CatalogUpdate? Pending()
    {
        var value = ReadCandidate();
        var disk = ReadIndex("index.json") ?? active!;
        return value is not null && (value.BaseRevision == disk.Revision || value.Index.Revision == disk.Revision)
            ? Difference(active!, value.Index) : null;
    }
    public async Task<CatalogIndex> ApplyAsync(string expectedRevision)
    {
        await LoadAsync().ConfigureAwait(false);
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            using var fileLock = Lock();
            var pending = ReadCandidate() ?? throw new InvalidOperationException("No validated resource update.");
            var disk = ReadIndex("index.json") ?? throw new IOException("Applied index could not be verified.");
            if (pending.Index.Revision != expectedRevision || (pending.BaseRevision != disk.Revision && pending.Index.Revision != disk.Revision))
                throw new InvalidOperationException("The resource update is stale; restart to check again.");
            if (disk.Revision != pending.Index.Revision)
                AtomicWrite(PathFor("index.json"), JsonSerializer.Serialize(pending.Index, CatalogIndex.Json), PathFor("index.backup.json"));
            active = pending.Index;
            return active;
        }
        finally { gate.Release(); }
    }
    async Task<SourceCache> Fetch(string url, string cacheName, CancellationToken token)
    {
        SourceCache? cached = null;
        try { if (File.Exists(PathFor(cacheName))) cached = JsonSerializer.Deserialize<SourceCache>(await File.ReadAllTextAsync(PathFor(cacheName), token).ConfigureAwait(false)); } catch (JsonException) { }
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("HOK-BetaStudio/1.3");
                if (cached?.Etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", cached.Etag);
                if (cached?.Modified is not null) request.Headers.TryAddWithoutValidation("If-Modified-Since", cached.Modified);
                using var perRequest = CancellationTokenSource.CreateLinkedTokenSource(token);
                perRequest.CancelAfter(TimeSpan.FromSeconds(25));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, perRequest.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) return cached;
                response.EnsureSuccessStatusCode();
                const int max = 32 * 1024 * 1024;
                if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Official response exceeds size limit.");
                using var stream = await response.Content.ReadAsStreamAsync(perRequest.Token).ConfigureAwait(false);
                using var bytes = new MemoryStream();
                var buffer = new byte[65536]; int read;
                while ((read = await stream.ReadAsync(buffer, perRequest.Token).ConfigureAwait(false)) > 0)
                {
                    if (bytes.Length + read > max) throw new InvalidDataException("Official response exceeds size limit.");
                    bytes.Write(buffer, 0, read);
                }
                return new(Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF'), response.Headers.ETag?.ToString(), response.Content.Headers.LastModified?.ToString("R"));
            }
            catch (Exception e) when (attempt < 2 && !token.IsCancellationRequested && (e is OperationCanceledException || e is HttpRequestException h && (h.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)h.StatusCode >= 500)))
            { await Task.Delay(TimeSpan.FromSeconds(1 << attempt), token).ConfigureAwait(false); }
        }
    }
    internal static void AtomicWrite(string path, string text, string? backup = null)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text); file.Write(bytes); file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, backup);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    void Log(string message)
    {
        try
        {
            string path = PathFor("sync.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, PathFor("sync.previous.log"), true);
            File.AppendAllText(path, DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { lifetime.Cancel(); if (ownsHttp) http.Dispose(); }
}
