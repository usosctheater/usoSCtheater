using SCTheaterEditor.Services;

namespace SCTheaterEditor.Tests;

/// <summary>추가(번역 모드 2회차): 게임 데이터 복사본 (exe 옆 Data\) — 복사·비교·다시 복사·없음 처리</summary>
public class GameDataTests
{
    /// <summary>임시 폴더에 게임 Resources\Data(시험용 복사본 3개)와 빈 TE Data 폴더</summary>
    private sealed class Env : IDisposable
    {
        public TempFolder Temp { get; } = new();
        public string Resources => System.IO.Path.Combine(Temp.Path, "Resources");
        public string GameFile(string name) => System.IO.Path.Combine(Resources, "Data", name);
        public GameData Data { get; }
        public int ChangedCount;

        public Env(bool withGame = true)
        {
            if (withGame)
            {
                Directory.CreateDirectory(System.IO.Path.Combine(Resources, "Data"));
                foreach (var f in GameData.Files) File.Copy(TestData.Path(f), GameFile(f));
            }
            Data = new GameData(System.IO.Path.Combine(Temp.Path, "TE", "Data"), () => withGame ? Resources : null, new AppLog());
            Data.Changed += () => ChangedCount++;
        }

        public void Dispose() => Temp.Dispose();
    }

    [Fact]
    public void Refresh_CopiesMissingAndReads()
    {
        using var env = new Env();
        env.Data.Refresh();
        Assert.All(GameData.Files, f => Assert.True(File.Exists(env.Data.CopyPath(f))));
        Assert.All(env.Data.States, s => Assert.Equal(GameDataState.Same, s.State));
        Assert.False(env.Data.HasProblem);
        Assert.Equal("아사히", env.Data.Characters.KoreanName("asahi"));
        Assert.Equal(new[] { "ko", "ja" }, env.Data.Languages.Select(l => l.Code));
        Assert.Equal(1, env.ChangedCount);
    }

    [Fact]
    public void GameChanged_Different_ThenCopyAll_ReplacesInPlace()
    {
        using var env = new Env();
        env.Data.Refresh();
        var held = env.Data.Characters;   // 받아 둔 객체 (ViewModel이 가진 것)
        File.WriteAllText(env.GameFile(GameData.CharacterFile),
            "<CharacterData><Character Id=\"asahi\" Name=\"새이름\" Name_Ja=\"新\"/></CharacterData>");

        env.Data.Refresh();   // 복사본이 있으므로 덮어쓰지 않고 비교만
        Assert.Equal(GameDataState.Different, env.Data.States.Single(s => s.Name == GameData.CharacterFile).State);
        Assert.True(env.Data.HasProblem);
        Assert.Equal("아사히", held.KoreanName("asahi"));

        Assert.Equal(3, env.Data.CopyAllFromGame());
        Assert.False(env.Data.HasProblem);
        Assert.Equal("새이름", held.KoreanName("asahi"));   // 같은 객체 안이 바뀜
        Assert.Equal(1, held.Count);
    }

    [Fact]
    public void NoCopyNoGame_RunsWithoutData()
    {
        using var env = new Env(withGame: false);
        env.Data.Refresh();
        Assert.All(env.Data.States, s => Assert.Equal(GameDataState.Missing, s.State));
        Assert.Equal(0, env.Data.Characters.Count);
        Assert.Equal(new[] { "ko", "ja" }, env.Data.Languages.Select(l => l.Code));   // 기본 목록
        Assert.True(env.Data.Languages[0].IsDefault);
        Assert.Equal(0, env.Data.CopyAllFromGame());
    }

    [Fact]
    public void BrokenCopy_LogsAndRunsEmpty()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.Data.Folder);
        File.WriteAllText(env.Data.CopyPath(GameData.CharacterFile), "<깨진 XML");
        env.Data.Refresh();
        Assert.Equal(0, env.Data.Characters.Count);
        Assert.Equal(GameDataState.Different, env.Data.States.Single(s => s.Name == GameData.CharacterFile).State);
    }

    [Fact]
    public void ParseLanguages_DefaultAndSuffix()
    {
        var list = GameData.ParseLanguages(
            "<LanguageData><Language Code=\"ko\" NativeName=\"한국어\"/><Language Code=\"JA\" NativeName=\"日本語\" Default=\"true\"/></LanguageData>");
        Assert.Equal(new[] { "ko", "ja" }, list.Select(l => l.Code));
        Assert.False(list[0].IsDefault);
        Assert.True(list[1].IsDefault);
        Assert.Equal("Ja", list[1].Suffix);

        var noDefault = GameData.ParseLanguages("<LanguageData><Language Code=\"ko\"/><Language Code=\"ja\"/></LanguageData>");
        Assert.True(noDefault[0].IsDefault);
    }
}
