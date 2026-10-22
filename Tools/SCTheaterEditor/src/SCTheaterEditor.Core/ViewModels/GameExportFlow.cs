using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>'게임으로 내보내기' 공용 흐름 (리소스 탭·스크립트 표·설정 탭)</summary>
internal static class GameExportFlow
{
    /// <summary>변경(5회차): 게임 Spine 폴더 = Resources\Spine. 없으면 설정 탭 안내 후 null (이전: 폴더 고르기)</summary>
    public static string? EnsureFolder(AppSettings settings, IDialogService dialogs)
    {
        string? folder = settings.Editor.GameSpineFolder;
        if (folder != null && Directory.Exists(folder)) return folder;
        dialogs.ShowError("게임 Spine 폴더 없음", folder == null
            ? "설정 탭 > 경로에서 게임 Resources 폴더를 지정하세요."
            : $"{folder}\n\nResources 폴더 아래에 Spine 폴더가 없습니다. 설정 탭에서 Resources 폴더를 확인하세요.");
        return null;
    }

    /// <summary>세트마다 내보내기 (다르면 물어봄) + 로그</summary>
    public static List<GameExportResult> Run(GameSpineExporter exporter, string folder, IEnumerable<SpineAsset> assets,
                                             IDialogService dialogs, AppLog log)
    {
        var results = new List<GameExportResult>();
        foreach (var a in assets)
        {
            var r = exporter.Export(folder, a, (asset, files) => dialogs.Confirm("게임에 다른 파일이 있음",
                $"{asset.DressName} ({asset.CgKey})\n\n게임 폴더의 파일이 TE 세트와 다릅니다:\n  {string.Join("\n  ", files)}\n\n덮어쓸까요? (취소 = 이 세트 건너뜀)"));
            results.Add(r);
            string line = $"게임 내보내기 [{r.Status}] {a.CgKey}{(r.Message != null ? $" — {r.Message}" : "")}";
            if (r.Status == GameExportStatus.Failed) log.Warn(line); else log.Info(line);
        }
        return results;
    }

    /// <summary>요약 문장 (예: "복사 3 · 같음 2"). 새로 쓴 게 있으면 Unity 등록 안내를 붙임</summary>
    public static string Summary(IReadOnlyCollection<GameExportResult> results)
    {
        var labels = new (GameExportStatus Status, string Label)[]
        {
            (GameExportStatus.Copied, "복사"), (GameExportStatus.Overwritten, "덮어씀"),
            (GameExportStatus.Same, "이미 리소스가 존재하여 복사하지 않음"),   // 변경 (기존 "같음")
            (GameExportStatus.Skipped, "건너뜀"), (GameExportStatus.Failed, "실패"),
        };
        // 변경: Same만 "…, N개의 스파인", 나머지는 "복사 N"
        var parts = labels.Select(l => (l.Status, l.Label, Count: results.Count(r => r.Status == l.Status)))
                          .Where(p => p.Count > 0)
                          .Select(p => p.Status == GameExportStatus.Same ? $"{p.Label}, {p.Count}개의 스파인" : $"{p.Label} {p.Count}");
        string text = results.Count == 0 ? "내보낼 세트 없음" : string.Join(" · ", parts);
        if (results.Any(r => r.Status is GameExportStatus.Copied or GameExportStatus.Overwritten))
            text += " → Unity에서 Tools > Spine Auto Setup으로 등록";
        return text;
    }
}
