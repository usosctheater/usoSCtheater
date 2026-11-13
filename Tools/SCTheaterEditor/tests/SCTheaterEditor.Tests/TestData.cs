using SCTheaterEditor.Services;

namespace SCTheaterEditor.Tests;

/// <summary>추가(번역 모드 2회차): 시험용 게임 데이터 복사본 (테스트 폴더 Data\ → 빌드 출력 Data\)</summary>
internal static class TestData
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Data", name);
    public static string Text(string name) => File.ReadAllText(Path(name));
    public static CharacterData Characters() =>   //[공유 코드 통합] 수정: 기본 언어·경고 목록 인자
        CharacterData.Parse(Text(GameData.CharacterFile), new UsoSCTheater.Shared.LanguageInfo("ko", "한국어", true), new List<string>());
    public static UsoSCTheater.Shared.LipData Lips() => UsoSCTheater.Shared.LipData.Parse(Text(GameData.LipFile));   //[공유 코드 통합] 수정(5회차): 공유 LipData

    /// <summary>[스테이지 레이아웃] 추가: 무대 배치 표 (게임 StageData.xml 복사본, 1~6분할)</summary>
    public static UsoSCTheater.Shared.StageTable Stage() => UsoSCTheater.Shared.StageTable.Parse(Text(GameData.StageFile), new List<string>());
}
