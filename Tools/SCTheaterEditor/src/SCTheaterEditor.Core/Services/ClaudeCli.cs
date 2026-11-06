using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SCTheaterEditor.Services;

/// <summary>추가(번역 모드 4회차): Claude Code 한 번 실행 결과. Text = 답변 글(result), CostUsd = Claude Code의 정가 추정값(구독 로그인이면 청구 없음)</summary>
public sealed record ClaudeRunResult(bool Ok, string? Text, string? Error, double? CostUsd, TimeSpan Elapsed);

/// <summary>Claude Code 실행 (테스트에서는 가짜로 바꿈)</summary>
public interface IClaudeRunner
{
    Task<ClaudeRunResult> RunAsync(string systemPrompt, string input, TimeSpan timeout, CancellationToken cancel);
}

/// <summary>
/// 추가(번역 모드 4회차): PC에 설치된 Claude Code를 비대화 모드로 한 번 실행 (가번역).
/// - 명령: claude -p --output-format json --tools "" --no-session-persistence --safe-mode --system-prompt-file {임시 파일}, 요청은 표준 입력
///   (도구 끔 · 세션 기록 안 남김 · 사용자 설정·CLAUDE.md·MCP 영향 없음 — 2026-11-05 이 PC의 2.1.318에서 확인)
/// - ANTHROPIC_API_KEY·ANTHROPIC_AUTH_TOKEN을 지우고 실행 → 구독 로그인으로만 (API 요금 방지)
/// - 임시 폴더(%TEMP%\SCTheaterEditor\claude-{guid})에서 실행하고 끝나면 지움
/// - 제한 시간·취소 때는 Claude Code 프로세스를 끝냄 (TE가 띄운 자식 프로세스만)
/// </summary>
public sealed class ClaudeCli(Func<string?> configuredPath, AppLog log) : IClaudeRunner
{
    /// <summary>기본 설치 위치 (네이티브 설치 — PATH에 등록되지 않은 경우가 있음)</summary>
    public static string DefaultInstallPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");

    /// <summary>실행할 claude 경로: 설정 → 기본 설치 위치 → PATH (claude.exe · claude.cmd). 못 찾으면 null</summary>
    public static string? Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        if (File.Exists(DefaultInstallPath)) return DefaultInstallPath;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
            {
                try
                {
                    string p = Path.Combine(dir.Trim(), name);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }   // PATH에 잘못된 글자
            }
        }
        return null;
    }

    public async Task<ClaudeRunResult> RunAsync(string systemPrompt, string input, TimeSpan timeout, CancellationToken cancel)
    {
        var sw = Stopwatch.StartNew();
        string? exe = Resolve(configuredPath());
        if (exe == null)
            return new(false, null, "Claude Code(claude.exe)를 찾지 못했습니다 — 설정 탭 > 가번역에서 경로를 지정하세요", null, sw.Elapsed);

        string work = Path.Combine(Path.GetTempPath(), AppSettings.AppName, "claude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string sysFile = Path.Combine(work, "system.txt");
            await File.WriteAllTextAsync(sysFile, systemPrompt, new UTF8Encoding(false), cancel);

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = work,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            string[] args = ["-p", "--output-format", "json", "--tools", "", "--no-session-persistence", "--safe-mode", "--system-prompt-file", sysFile];
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(exe);
            }
            else psi.FileName = exe;
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment.Remove("ANTHROPIC_API_KEY");
            psi.Environment.Remove("ANTHROPIC_AUTH_TOKEN");

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Claude Code를 시작하지 못했습니다");
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            await proc.StandardInput.WriteAsync(input);
            proc.StandardInput.Close();

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(timeout);
            try { await proc.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch (Exception ex) { log.Warn($"Claude Code 종료 실패: {ex.Message}"); }
                return new(false, null, cancel.IsCancellationRequested ? "취소했습니다" : $"제한 시간({timeout.TotalMinutes:0.#}분)을 넘겨 중지했습니다", null, sw.Elapsed);
            }
            string outText = await stdout, errText = await stderr;
            return Parse(proc.ExitCode, outText, errText, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return new(false, null, "취소했습니다", null, sw.Elapsed);   // 시작 전(임시 파일 쓰는 중) 취소
        }
        catch (Exception ex)
        {
            log.Error("Claude Code 실행 실패", ex);
            return new(false, null, ex.Message, null, sw.Elapsed);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception) { }   // 임시 폴더 — 지우지 못해도 다음 정리 때
        }
    }

    /// <summary>--output-format json 출력 → 결과 (result · is_error · total_cost_usd)</summary>
    internal static ClaudeRunResult Parse(int exitCode, string stdout, string stderr, TimeSpan elapsed)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            string? text = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            bool isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            double? cost = root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;
            if (isError || exitCode != 0 || text == null)
                return new(false, text, $"Claude Code 오류 (종료 코드 {exitCode}): {First(text ?? stderr)}", cost, elapsed);
            return new(true, text, null, cost, elapsed);
        }
        catch (JsonException)
        {
            return new(false, null, $"Claude Code 출력을 읽지 못했습니다 (종료 코드 {exitCode}): {First(stderr.Length > 0 ? stderr : stdout)}", null, elapsed);
        }
    }

    private static string First(string s)
    {
        s = s.Trim();
        int nl = s.IndexOf('\n');
        if (nl > 0) s = s[..nl].Trim();
        return s.Length > 200 ? s[..200] + "…" : s;
    }
}
