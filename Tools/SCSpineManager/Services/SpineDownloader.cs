using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using SCSpineManager.Models;

namespace SCSpineManager.Services;

/// <summary>서버가 혼잡 응답(429/5xx)을 재시도 후에도 계속 보낼 때 → 큐 전체 중지</summary>
public sealed class ServerBusyException(string message) : Exception(message);

/// <summary>재시도해도 의미 없는 실패(404 등) → 이 세트만 실패 처리하고 다음 세트로</summary>
public sealed class RemoteFileException(string message) : Exception(message);

public enum SetResult { Downloaded, CopiedOnly, AlreadyDone }

/// <summary>
/// 세트 1개(json + atlas + 텍스처) 받기.
/// - 요청은 하나씩, 요청 시작 사이에 설정 간격(기본 1초)을 둔다
/// - 세 파일을 모두 메모리로 받은 뒤 저장 (중간에 실패·취소하면 아무것도 쓰지 않음)
/// - 텍스처는 PNG로 변환, atlas 페이지명을 새 파일명으로 바꿔 .atlas.txt로 저장
/// - 저장은 .part에 쓴 뒤 이름 변경, 같은 경로를 공유하는 다른 의상 폴더에는 로컬 복사
/// - 실패 재시도: 네트워크 오류·429·5xx는 30초 → 2분 → 10분 뒤 다시 시도
///   그래도 429·5xx면 ServerBusyException(큐 중지), 네트워크 오류면 이 세트만 실패
/// </summary>
public sealed class SpineDownloader : IDisposable
{
    private static readonly TimeSpan[] RetryDelays =
        { TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10) };

    /// <summary>매니페스트 저장 주기 (세트마다 쓰면 수천 번 전체를 다시 쓰게 되므로 모아서 저장)</summary>
    private const int SaveEverySets = 20;

    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly SpineManifest _manifest;
    private readonly Action<string> _log;
    private DateTime _lastRequestAt = DateTime.MinValue;
    private int _unsavedSets;

    /// <summary>지금까지 보낸 요청 수 (재시도 포함)</summary>
    public int RequestCount { get; private set; }

    public SpineDownloader(AppSettings settings, SpineManifest manifest, Action<string> log)
    {
        _settings = settings;
        _manifest = manifest;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SCSpineManager/0.1");
    }

    // ------------------------------------------------------------------ 완료 판정

    /// <summary>매니페스트에 기록이 있고, 원본 폴더에 파일이 모두 있으면 완료</summary>
    public static bool IsComplete(SpineManifest manifest, string root, SpineSet set)
    {
        if (!manifest.Sets.TryGetValue(set.SourcePath, out var e) || e.Json == null || e.Atlas == null || e.Images.Count == 0)
            return false;
        // 매니페스트에 기록된 이름과 지금 규칙의 이름이 다르면 다시 정리가 필요하므로 미완료로 봄
        if (e.BaseName != set.BaseName) return false;
        return set.Targets.All(t => FilesExist(Path.Combine(root, t.RelativeFolder), e));
    }

    private static bool FilesExist(string folder, ManifestEntry e) =>
        File.Exists(Path.Combine(folder, e.Json!.SavedName)) &&
        File.Exists(Path.Combine(folder, e.Atlas!.SavedName)) &&
        e.Images.All(i => File.Exists(Path.Combine(folder, i.SavedName)));

    // ------------------------------------------------------------------ 세트 처리

    public async Task<SetResult> ProcessAsync(SpineSet set, CancellationToken ct)
    {
        string root = _settings.SaveFolder;

        // 이미 받은 세트: 빠진 복사본만 로컬에서 채움 (서버 요청 없음)
        if (_manifest.Sets.TryGetValue(set.SourcePath, out var existing) && existing.BaseName == set.BaseName
            && existing.Json != null && existing.Atlas != null && existing.Images.Count > 0)
        {
            string? source = set.Targets.Select(t => Path.Combine(root, t.RelativeFolder))
                                        .FirstOrDefault(f => FilesExist(f, existing));
            if (source != null)
            {
                bool copied = CopyToMissingTargets(set, existing, source);
                if (copied) MarkDirty();
                return copied ? SetResult.CopiedOnly : SetResult.AlreadyDone;
            }
        }

        string baseUrl = _settings.FileBase + set.SourcePath;

        // 1) json
        var json = await GetAsync(baseUrl + "data.json", ct);

        // 2) atlas → 페이지(텍스처) 목록
        var atlas = await GetAsync(baseUrl + "data.atlas", ct);
        string atlasText = DecodeText(atlas.Bytes);
        var (lines, pageLines) = AtlasPages.Parse(atlasText);
        if (pageLines.Count == 0) throw new RemoteFileException("atlas에서 텍스처 페이지를 찾지 못함");
        if (pageLines.Count > 1) _log($"  참고: 텍스처 {pageLines.Count}장 — {set.BaseName}");

        // 3) 텍스처 (페이지 순서대로) → PNG 변환, atlas의 페이지명 교체
        var images = new List<(RemoteFile info, byte[] png)>();
        for (int i = 0; i < pageLines.Count; i++)
        {
            string pageName = lines[pageLines[i]].Trim();
            var img = await GetAsync(baseUrl + Uri.EscapeDataString(pageName), ct);
            byte[] png = TextureConverter.ToPng(img.Bytes, out string format);

            string savedName = set.BaseName + (i == 0 ? "" : $"_{i + 1}") + SpineNaming.ImageExt;
            lines[pageLines[i]] = savedName;
            images.Add((img.ToRemoteFile(pageName, savedName, format), png));
        }
        byte[] newAtlas = new UTF8Encoding(false).GetBytes(string.Join("\n", lines));

        // 4) 원본 폴더에 저장
        var entry = new ManifestEntry
        {
            BaseName = set.BaseName,
            Group = set.Group,
            SpineType = set.SpineType,
            EnzaId = set.EnzaId,
            DownloadedAt = DateTime.Now,
            Json = json.ToRemoteFile("data.json", set.BaseName + SpineNaming.JsonExt),
            Atlas = atlas.ToRemoteFile("data.atlas", set.BaseName + SpineNaming.AtlasExt),
            Images = images.Select(x => x.info).ToList(),
        };

        string primary = Path.Combine(root, set.Targets[0].RelativeFolder);
        Directory.CreateDirectory(primary);
        WriteAtomic(Path.Combine(primary, entry.Json.SavedName), json.Bytes);
        WriteAtomic(Path.Combine(primary, entry.Atlas.SavedName), newAtlas);
        foreach (var (info, png) in images)
            WriteAtomic(Path.Combine(primary, info.SavedName), png);

        // 5) 다른 의상 폴더에 로컬 복사
        CopyToMissingTargets(set, entry, primary);

        _manifest.Sets[set.SourcePath] = entry;
        MarkDirty();
        return SetResult.Downloaded;
    }

    /// <summary>원본 폴더의 파일을 아직 없는 저장 위치로 복사. 복사했으면 true</summary>
    private bool CopyToMissingTargets(SpineSet set, ManifestEntry entry, string sourceFolder)
    {
        bool copied = false;
        foreach (var t in set.Targets)
        {
            string folder = Path.Combine(_settings.SaveFolder, t.RelativeFolder);
            if (!entry.Folders.Contains(t.RelativeFolder, StringComparer.OrdinalIgnoreCase))
                entry.Folders.Add(t.RelativeFolder);
            if (string.Equals(folder, sourceFolder, StringComparison.OrdinalIgnoreCase) || FilesExist(folder, entry))
                continue;

            Directory.CreateDirectory(folder);
            foreach (var name in new[] { entry.Json!.SavedName, entry.Atlas!.SavedName }.Concat(entry.Images.Select(i => i.SavedName)))
                File.Copy(Path.Combine(sourceFolder, name), Path.Combine(folder, name), overwrite: true);
            copied = true;
        }
        return copied;
    }

    // ------------------------------------------------------------------ 매니페스트

    private void MarkDirty()
    {
        if (++_unsavedSets >= SaveEverySets) Flush();
    }

    /// <summary>모아 둔 기록을 매니페스트 파일에 저장 (큐가 끝나거나 중지될 때도 호출)</summary>
    public void Flush()
    {
        if (_unsavedSets == 0) return;
        _manifest.Save(_settings.MetaFolder);
        _unsavedSets = 0;
    }

    // ------------------------------------------------------------------ 요청

    private sealed record Fetched(byte[] Bytes, string? ETag, DateTimeOffset? LastModified)
    {
        public RemoteFile ToRemoteFile(string name, string savedName, string? format = null) => new()
        {
            Name = name, SavedName = savedName, ETag = ETag, LastModified = LastModified, Size = Bytes.LongLength, Format = format,
        };
    }

    private async Task<Fetched> GetAsync(string url, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            await ThrottleAsync(ct);
            string problem;
            bool busy;
            try
            {
                RequestCount++;
                using var res = await _http.GetAsync(url, ct);
                int code = (int)res.StatusCode;
                if (res.IsSuccessStatusCode)
                {
                    byte[] bytes = await res.Content.ReadAsByteArrayAsync(ct);
                    return new Fetched(bytes, res.Headers.ETag?.Tag, res.Content.Headers.LastModified);
                }
                if (res.StatusCode != HttpStatusCode.TooManyRequests && code < 500)
                    throw new RemoteFileException($"{code} {res.ReasonPhrase} — {url}");   // 404 등: 재시도 안 함
                problem = $"서버 응답 {code}";
                busy = true;
            }
            catch (HttpRequestException ex) { problem = $"네트워크 오류: {ex.Message}"; busy = false; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { problem = "응답 시간 초과"; busy = false; }
            catch (IOException ex) { problem = $"전송 오류: {ex.Message}"; busy = false; }

            if (attempt >= RetryDelays.Length)
            {
                if (busy) throw new ServerBusyException($"{problem} — 재시도 {RetryDelays.Length}회 후에도 계속됨, 큐를 중지합니다");
                throw new RemoteFileException($"{problem} — 재시도 {RetryDelays.Length}회 실패: {url}");
            }
            var wait = RetryDelays[attempt];
            _log($"  {problem} → {wait.TotalSeconds:0}초 뒤 다시 시도 ({attempt + 1}/{RetryDelays.Length})");
            await Task.Delay(wait, ct);
        }
    }

    /// <summary>요청 시작 사이에 최소 간격 보장</summary>
    private async Task ThrottleAsync(CancellationToken ct)
    {
        var wait = _lastRequestAt + TimeSpan.FromMilliseconds(_settings.RequestIntervalMs) - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        _lastRequestAt = DateTime.UtcNow;
    }

    // ------------------------------------------------------------------ 기타

    private static string DecodeText(byte[] bytes)
    {
        string s = Encoding.UTF8.GetString(bytes);
        return s.Length > 0 && s[0] == '﻿' ? s[1..] : s;
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        string part = path + ".part";
        File.WriteAllBytes(part, bytes);
        File.Move(part, path, overwrite: true);
    }

    public void Dispose()
    {
        Flush();
        _http.Dispose();
    }
}

/// <summary>Spine 3.x atlas의 페이지(텍스처) 줄 찾기: 빈 줄 다음(또는 파일 첫 줄)의 첫 비어 있지 않은 줄</summary>
public static class AtlasPages
{
    public static (string[] lines, List<int> pageLines) Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var pages = new List<int>();
        bool expectPage = true;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) { expectPage = true; continue; }
            if (expectPage)
            {
                if (!lines[i].Contains('.') || lines[i].Contains(':'))
                    throw new RemoteFileException($"atlas 형식 이상 ({i + 1}번째 줄: {lines[i].Trim()})");
                pages.Add(i);
                expectPage = false;
            }
        }
        return (lines, pages);
    }
}
