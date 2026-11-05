using SCTheaterEditor.Services;

namespace SCTheaterEditor.Tests;

/// <summary>추가(번역 모드 2회차): 시험용 게임 데이터 복사본 (테스트 폴더 Data\ → 빌드 출력 Data\)</summary>
internal static class TestData
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Data", name);
    public static string Text(string name) => File.ReadAllText(Path(name));
    public static CharacterData Characters() => CharacterData.Parse(Text(GameData.CharacterFile));
    public static LipRules Lips() => LipRules.Parse(Text(GameData.LipFile));
}
