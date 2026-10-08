using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SCSpineManager.Models;

namespace SCSpineManager.Services;

/// <summary>
/// 목록 스냅샷: idollist + 아이돌별 dresslist. 서버 응답 원문을 그대로 보관한다 (다음 확인 때 비교 기준).
/// 저장 위치: {SaveFolder}\_meta\idollist.json, \_meta\dresslist\{idolId}.json
/// </summary>
public sealed class CatalogSnapshot
{
    public List<IdolInfo> Idols { get; } = new();
    /// <summary>idolId → 의상 목록</summary>
    public Dictionary<int, List<DressInfo>> Dresses { get; } = new();

    // 서버 응답 원문 (저장용)
    public string RawIdolList { get; set; } = "";
    public Dictionary<int, string> RawDressLists { get; } = new();

    public DateTime FetchedAt { get; set; }

    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string IdolListPath(string metaFolder) => Path.Combine(metaFolder, "idollist.json");
    public static string DressListFolder(string metaFolder) => Path.Combine(metaFolder, "dresslist");

    /// <summary>스냅샷 저장. 이전 스냅샷을 통째로 바꾼다 (아이돌이 목록에서 빠져도 남지 않도록 dresslist 폴더를 새로 씀)</summary>
    public void Save(string metaFolder)
    {
        string dressDir = DressListFolder(metaFolder);
        string tempDir = dressDir + ".new";
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        foreach (var (idolId, raw) in RawDressLists)
            File.WriteAllText(Path.Combine(tempDir, $"{idolId}.json"), raw, Encoding.UTF8);

        // 다 쓴 뒤에 교체 → 중간에 실패해도 이전 스냅샷이 남는다
        if (Directory.Exists(dressDir)) Directory.Delete(dressDir, true);
        Directory.Move(tempDir, dressDir);
        File.WriteAllText(IdolListPath(metaFolder), RawIdolList, Encoding.UTF8);
    }

    /// <summary>저장된 스냅샷 읽기. 없으면 null</summary>
    public static CatalogSnapshot? Load(string metaFolder)
    {
        string idolPath = IdolListPath(metaFolder);
        if (!File.Exists(idolPath)) return null;

        var snap = new CatalogSnapshot
        {
            RawIdolList = File.ReadAllText(idolPath, Encoding.UTF8),
            FetchedAt = File.GetLastWriteTime(idolPath),
        };
        snap.Idols.AddRange(JsonSerializer.Deserialize<List<IdolInfo>>(snap.RawIdolList, JsonOptions) ?? new());

        foreach (var idol in snap.Idols)
        {
            string p = Path.Combine(DressListFolder(metaFolder), $"{idol.IdolId}.json");
            if (!File.Exists(p)) continue;
            string raw = File.ReadAllText(p, Encoding.UTF8);
            snap.RawDressLists[idol.IdolId] = raw;
            snap.Dresses[idol.IdolId] = JsonSerializer.Deserialize<List<DressInfo>>(raw, JsonOptions) ?? new();
        }
        return snap;
    }
}

/// <summary>
/// 목록 API 요청. 요청은 하나씩 순서대로, 요청 사이에 설정된 간격을 둔다 (서버 부담 최소화).
/// 전체 요청 수 = 1(idollist) + 아이돌 수(dresslist) ≈ 34회
/// </summary>
public sealed class SpineCatalogClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;

    public SpineCatalogClient(AppSettings settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SCSpineManager/0.1");
    }

    /// <summary>idollist와 모든 dresslist를 받는다. 하나라도 실패하면 예외 (이전 스냅샷은 그대로 둠)</summary>
    public async Task<CatalogSnapshot> FetchAsync(IProgress<string> log, CancellationToken ct)
    {
        var snap = new CatalogSnapshot { FetchedAt = DateTime.Now };

        snap.RawIdolList = await GetTextAsync(_settings.ApiBase + "idollist", ct);
        snap.Idols.AddRange(JsonSerializer.Deserialize<List<IdolInfo>>(snap.RawIdolList, CatalogSnapshot.JsonOptions) ?? new());
        log.Report($"아이돌 목록: {snap.Idols.Count}명");

        for (int i = 0; i < snap.Idols.Count; i++)
        {
            var idol = snap.Idols[i];
            await Task.Delay(_settings.RequestIntervalMs, ct);

            string raw = await GetTextAsync($"{_settings.ApiBase}dresslist?idolId={idol.IdolId}", ct);
            var dresses = JsonSerializer.Deserialize<List<DressInfo>>(raw, CatalogSnapshot.JsonOptions) ?? new();
            snap.RawDressLists[idol.IdolId] = raw;
            snap.Dresses[idol.IdolId] = dresses;
            log.Report($"[{i + 1}/{snap.Idols.Count}] {idol.IdolName}: 의상 {dresses.Count}벌");
        }
        return snap;
    }

    /// <summary>GET 1회. 실패 시 5초 뒤 한 번만 다시 시도</summary>
    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var res = await _http.GetAsync(url, ct);
                res.EnsureSuccessStatusCode();
                return await res.Content.ReadAsStringAsync(ct);
            }
            catch (Exception) when (attempt < 2 && !ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
