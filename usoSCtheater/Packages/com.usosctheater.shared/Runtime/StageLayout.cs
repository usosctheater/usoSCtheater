#nullable enable
using System.Globalization;

namespace UsoSCTheater.Shared
{
    /// <summary>
    /// 무대 배치 공식 — 게임 CGManager·TE 무대가 함께 씀 (원본 게임 패키지, TE는 사본 → 고친 뒤 node sync-shared.js)
    /// 좌표: 1920×1080 화면 중심 기준 px, 위가 +. Pos 표기 "N-i" = N분할의 왼쪽부터 i번째 칸
    /// </summary>
    public static class StageLayout
    {
        public const double ScreenWidth = 1920;
        public const double ScreenHeight = 1080;
        public const string ZoomPivotBone = "head";   //[공유 코드 통합] 추가(5회차): 줌 기준 본 (기본 자세 위치) — TE JS는 config로 받음

        //"N-i" → 숫자 두 개 (형식만). 칸 범위·표에 있는지는 StagePlanner
        public static bool TryParsePos(string? text, out int count, out int index)
        {
            count = 0;
            index = 0;
            string[] parts = (text ?? "").Trim().Split('-');
            return parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out count)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }

        public static string FormatPos(int count, int index) =>
            count.ToString(CultureInfo.InvariantCulture) + "-" + index.ToString(CultureInfo.InvariantCulture);

        //N분할 i번째 칸 X = ((i − 0.5) / N − 0.5) × 사용 폭
        public static double SlotX(double width, int count, int index) => ((index - 0.5) / count - 0.5) * width;

        //줌: 효과 층(자식)을 zoom배로 키울 때 기준점(head 본, 자식 기준 좌표)이 제자리에 있도록 자식을 옮길 거리 (축마다)
        //배율만으로 매번 다시 계산하므로 줌을 이어 걸어도 누적되지 않음
        public static double ZoomOffset(double pivot, double zoom) => pivot * (1 - zoom);
    }
}
