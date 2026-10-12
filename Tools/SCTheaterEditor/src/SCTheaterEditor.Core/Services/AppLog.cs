using System.IO;
using System.Text;

namespace SCTheaterEditor.Services;

public enum LogLevel { Info, Warn, Error }

/// <summary>로그 1줄</summary>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Message);

/// <summary>
/// 프로그램 전체의 로그 통로 (화면 로그 상자·파일·상태 표시줄이 같은 기록을 받는다).
/// - Written 이벤트는 로그를 남긴 스레드에서 바로 호출됨 → 화면 쪽 구독자가 UI 스레드로 넘겨서 처리
/// - 파일: {폴더}\log.txt. MaxFileBytes를 넘으면 log_{시각}.txt로 넘기고 새로 시작, 오래된 것은 KeepFiles개만 남김
/// - 파일 쓰기 실패는 프로그램을 멈추지 않고 화면에 한 번만 알림
/// </summary>
public sealed class AppLog
{
    /// <summary>프로그램 전체에서 쓰는 로그 (서비스가 따로 로그 통로를 받지 않아도 되도록)</summary>
    public static AppLog Shared { get; } = new();

    public const string FileName = "log.txt";

    /// <summary>파일 하나의 최대 크기 (넘으면 다음 파일로)</summary>
    public long MaxFileBytes { get; set; } = 1_000_000;

    /// <summary>넘긴 로그 파일을 몇 개까지 남길지</summary>
    public int KeepFiles { get; set; } = 5;

    private readonly object _lock = new();
    private string? _folder;
    private bool _fileFailureReported;

    public event Action<LogEntry>? Written;

    /// <summary>로그 파일 폴더 지정 (null이면 파일에 쓰지 않음)</summary>
    public void SetFileFolder(string? folder)
    {
        lock (_lock)
        {
            _folder = folder;
            _fileFailureReported = false;
        }
    }

    public void Info(string message) => Write(LogLevel.Info, message, null);
    public void Warn(string message) => Write(LogLevel.Warn, message, null);
    public void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex == null ? message : $"{message}: {ex.Message}", ex);

    /// <summary>화면 표시용 한 줄 ([시:분:초] [주의] 내용)</summary>
    public static string Format(LogEntry e) => e.Level switch
    {
        LogLevel.Warn => $"[{e.Time:HH:mm:ss}] [주의] {e.Message}",
        LogLevel.Error => $"[{e.Time:HH:mm:ss}] [오류] {e.Message}",
        _ => $"[{e.Time:HH:mm:ss}] {e.Message}",
    };

    private void Write(LogLevel level, string message, Exception? ex)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        string? fileError = null;

        lock (_lock)
        {
            if (_folder != null)
            {
                try
                {
                    AppendToFile(entry, ex);
                }
                catch (Exception e)
                {
                    // 파일 쓰기 실패: 같은 실패를 매번 알리지 않도록 한 번만
                    if (!_fileFailureReported)
                    {
                        _fileFailureReported = true;
                        fileError = e.Message;
                    }
                }
            }
        }

        Written?.Invoke(entry);
        if (fileError != null)
            Written?.Invoke(new LogEntry(DateTime.Now, LogLevel.Warn, $"로그 파일 쓰기 실패 (이후 같은 실패는 표시하지 않음): {fileError}"));
    }

    /// <summary>lock 안에서 호출</summary>
    private void AppendToFile(LogEntry entry, Exception? ex)
    {
        Directory.CreateDirectory(_folder!);
        string path = Path.Combine(_folder!, FileName);

        var info = new FileInfo(path);
        if (info.Exists && info.Length >= MaxFileBytes) Rotate(path);

        var sb = new StringBuilder();
        sb.Append($"{entry.Time:yyyy-MM-dd HH:mm:ss} [{entry.Level.ToString().ToUpperInvariant(),-5}] {entry.Message}");
        sb.AppendLine();
        if (ex != null) sb.AppendLine(ex.ToString());   // 오류는 원인 추적용으로 전체 내용까지
        File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private void Rotate(string path)
    {
        string folder = Path.GetDirectoryName(path)!;
        File.Move(path, Path.Combine(folder, $"log_{DateTime.Now:yyyyMMdd-HHmmss}.txt"), overwrite: true);

        // 이름에 시각이 들어 있어 이름순 = 오래된 순
        var old = Directory.GetFiles(folder, "log_*.txt").OrderBy(f => f, StringComparer.Ordinal).ToList();
        foreach (var f in old.Take(Math.Max(0, old.Count - KeepFiles)))
            File.Delete(f);
    }
}
