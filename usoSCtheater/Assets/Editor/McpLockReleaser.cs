// [신규] Claude(desktop-commander MCP)의 node.exe가 프로젝트 파일을 점유해
// Unity가 Temp 파일을 저장하지 못할 때, 저장 전에 수동으로 눌러 점유를 해제하는 메뉴.
// 동작: CommandLine에 'desktop-commander'가 포함된 node.exe를 전부 종료.
// 주의: 실행 중인 Claude 대화의 파일 도구 연결이 끊김 → Claude 앱에서 다시 연결하면 새로 뜸.
//       Claude가 파일을 쓰는 중에는 누르지 말 것(파일이 덜 써진 상태로 남을 수 있음).
#if UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

public static class McpLockReleaser
{
    private const string PsScript =
        "$p = @(Get-CimInstance Win32_Process -Filter \"Name='node.exe'\" | " +
        "Where-Object { $_.CommandLine -match 'desktop-commander' }); " +
        "foreach ($x in $p) { Stop-Process -Id $x.ProcessId -Force -ErrorAction SilentlyContinue }; " +
        "Write-Output $p.Count";

    [MenuItem("Tools/Release MCP Lock")]
    private static void ReleaseMcpLock()
    {
        if (!EditorUtility.DisplayDialog(
                "Release MCP Lock",
                "desktop-commander(node.exe)를 모두 종료합니다.\nClaude의 파일 도구 연결이 끊깁니다. 계속할까요?",
                "종료", "취소"))
            return;

        try
        {
            // 따옴표 이스케이프 문제를 피하기 위해 -EncodedCommand(UTF-16LE Base64) 사용
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(PsScript));
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var proc = Process.Start(psi))
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                string error = proc.StandardError.ReadToEnd().Trim();
                proc.WaitForExit(15000);

                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning("[McpLockReleaser] " + error);

                Debug.Log($"[McpLockReleaser] desktop-commander node.exe {output}개 종료. 이제 저장하세요.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[McpLockReleaser] 실행 실패: " + e.Message);
        }
    }
}
#endif
