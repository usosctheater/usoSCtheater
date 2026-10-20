using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Models;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>테스트용 목록·세트·파일 준비</summary>
internal static class Fixture
{
    public static CatalogSnapshot Snapshot()
    {
        var snap = new CatalogSnapshot { FetchedAt = new DateTime(2026, 10, 12) };
        snap.Idols.Add(new IdolInfo { IdolId = 1, IdolName = "櫻木真乃", NickName = "mano" });
        snap.Idols.Add(new IdolInfo { IdolId = 21, IdolName = "樋口円香", NickName = "madoka" });
        snap.Dresses[1] = new() { Dress(1, "1040010010", "【ほわっとスマイル】櫻木真乃", "P_SSR", 1, "stand") };
        snap.Dresses[21] = new()
        {
            Dress(21, "1040210050", "【Merry】樋口円香", "P_SSR", 1, "stand"),
            Dress(21, "1030210010", "【UNCHARTE:D】樋口円香", "P_SR", 2, "cb"),
        };
        return snap;
    }

    private static DressInfo Dress(int idol, string enza, string name, string type, int order, string spineType) => new()
    {
        Exist = true, IdolId = idol, EnzaId = enza, DressName = name, DressType = type, DressTypeOrder = order, DressUuid = enza,
        Assets = new() { ["idols"] = new() { new DressAsset { Path = $"spine/idols/{spineType}/{enza}/", Type = spineType } } },
    };

    /// <summary>계획의 세트를 "받은 상태"로: 파일 생성 + 매니페스트 기록 (v1 형식이면 아이돌·의상 정보 없음)</summary>
    public static SpineManifest Downloaded(string root, SpinePlan plan, bool v1)
    {
        var m = new SpineManifest();
        foreach (var set in plan.Sets)
        {
            string folder = set.Targets[0].RelativeFolder;
            Directory.CreateDirectory(Path.Combine(root, folder));
            var e = new ManifestEntry
            {
                BaseName = set.BaseName, Group = set.Group, SpineType = set.SpineType, EnzaId = set.EnzaId,
                Folders = { folder },
                Json = new RemoteFile { SavedName = set.BaseName + ".json" },
                Atlas = new RemoteFile { SavedName = set.BaseName + ".atlas.txt" },
                Images = { new RemoteFile { SavedName = set.BaseName + ".png" } },
            };
            foreach (var f in new[] { e.Json.SavedName, e.Atlas.SavedName, e.Images[0].SavedName })
                File.WriteAllText(Path.Combine(root, folder, f), "x");
            if (!v1) e.ApplySetInfo(set);
            m.Sets[set.SourcePath] = e;
        }
        if (v1) m.Version = 1;
        return m;
    }
}

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;
    public void Post(Action action) => action();
}

internal sealed class FakeDialogs : IDialogService
{
    public bool ConfirmResult { get; set; } = true;
    public int NoticeCount { get; private set; }
    public bool Confirm(string title, string message) => ConfirmResult;
    public void ShowChangeNotice(CatalogDiff diff, DateTime previousAt) => NoticeCount++;
    public void OpenInExplorer(string path) { }

    // 추가(4-1): 막 열기·저장
    public SaveChoice SaveChoiceResult { get; set; } = SaveChoice.Cancel;
    public int AskSaveCount { get; private set; }
    public string? PickResult { get; set; }
    public List<string> Errors { get; } = new();
    public SaveChoice AskSave(string title, string message) { AskSaveCount++; return SaveChoiceResult; }
    public string? PickFolder(string title, string? initialFolder) => PickResult;
    public string? PickFile(string title, string filter, string? initialFolder) => PickResult;
    public void ShowError(string title, string message) => Errors.Add(message);

    // 추가: 메모리 클립보드 (ClipboardWritable = false면 쓰기 실패 흉내)
    public string? ClipboardText { get; set; }
    public bool ClipboardWritable { get; set; } = true;
    public string? GetClipboardText() => ClipboardText;
    public bool SetClipboardText(string text)
    {
        if (!ClipboardWritable) return false;
        ClipboardText = text;
        return true;
    }
}

internal sealed class FakeCatalog(CatalogSnapshot snap) : ICatalogSource
{
    public Task<CatalogSnapshot> FetchAsync(IProgress<string> log, CancellationToken ct) => Task.FromResult(snap);
    public void Dispose() { }
}

/// <summary>받기 흉내: 파일을 만들고 매니페스트에 기록 (실제 다운로더와 같은 결과)</summary>
internal sealed class FakeDownloader(string root, SpineManifest manifest) : ISetDownloader
{
    public int RequestCount { get; private set; }

    public Task<SetResult> ProcessAsync(SpineSet set, CancellationToken ct)
    {
        RequestCount += 3;
        var plan = new SpinePlan();
        plan.Sets.Add(set);
        var m = Fixture.Downloaded(root, plan, v1: false);
        manifest.Sets[set.SourcePath] = m.Sets[set.SourcePath];
        return Task.FromResult(SetResult.Downloaded);
    }

    public void Dispose() { }
}

public class ManifestV2Tests
{
    [Fact]
    public void V1_IsFilledFromCatalog()
    {
        using var t = new TempFolder();
        var snap = Fixture.Snapshot();
        var plan = SpinePlanner.Build(snap);
        var m = Fixture.Downloaded(t.Path, plan, v1: true);
        Assert.True(m.NeedsCatalogInfo);

        int filled = m.FillFromCatalog(snap, plan);

        Assert.Equal(3, filled);
        Assert.False(m.NeedsCatalogInfo);
        Assert.Equal(SpineManifest.CurrentVersion, m.Version);
        Assert.Equal("madoka", m.Idols[21].NickName);
        var e = m.Sets["spine/idols/cb/1030210010/"];
        Assert.Equal(21, e.IdolId);
        Assert.Equal("【UNCHARTE:D】樋口円香", e.DressName);   // 서버 원문 (폴더명은 ':'가 빠짐)
    }

    [Fact]
    public void SaveLoad_KeepsV2Fields()
    {
        using var t = new TempFolder();
        var snap = Fixture.Snapshot();
        var plan = SpinePlanner.Build(snap);
        var m = Fixture.Downloaded(t.Path, plan, v1: false);
        m.UpdateIdols(snap.Idols);
        m.Save(t.Path);

        var loaded = SpineManifest.Load(t.Path);
        Assert.Equal(2, loaded.Idols.Count);
        Assert.Equal("P_SR", loaded.Sets["spine/idols/cb/1030210010/"].DressType);
        Assert.False(loaded.NeedsCatalogInfo);
    }
}

public class SpineLibraryTests
{
    private static SpineLibrary Build(TempFolder t)
    {
        var snap = Fixture.Snapshot();
        var plan = SpinePlanner.Build(snap);
        var m = Fixture.Downloaded(t.Path, plan, v1: false);
        m.UpdateIdols(snap.Idols);
        var lib = new SpineLibrary(t.Path, m);
        lib.Rebuild();
        return lib;
    }

    [Fact]
    public void CgKey_IsNickNamePlusBaseName()
    {
        using var t = new TempFolder();
        var lib = Build(t);
        var a = lib.Find("madoka_stand_1040210050");
        Assert.NotNull(a);
        Assert.Equal("【Merry】樋口円香", a!.DressName);
        Assert.Equal("樋口円香", a.IdolName);
        Assert.Same(a, lib.Find("MADOKA_stand_1040210050"));   // 대소문자 무시
    }

    [Fact]
    public void Assets_SortedByDressTypeThenIdol()
    {
        using var t = new TempFolder();
        var lib = Build(t);
        Assert.Equal(new[] { "mano_stand_1040010010", "madoka_stand_1040210050", "madoka_cb_1030210010" },
                     lib.Assets.Select(a => a.CgKey));
    }

    [Fact]
    public void MissingFile_IsExcluded_AndRefreshRestores()
    {
        using var t = new TempFolder();
        var lib = Build(t);
        var a = lib.Find("mano_stand_1040010010")!;
        string png = Path.Combine(lib.FolderPath(a), a.ImageFiles[0]);
        File.Delete(png);
        lib.Refresh(a.SourcePath);
        Assert.Null(lib.Find(a.CgKey));

        File.WriteAllText(png, "x");
        int changed = 0;
        lib.Changed += () => changed++;
        lib.Refresh(a.SourcePath);
        Assert.NotNull(lib.Find(a.CgKey));
        Assert.Equal(1, changed);
    }
}

public class IdolFilterViewModelTests
{
    private static IdolFilterViewModel Make(IEnumerable<int>? on = null, string? unit = null)
    {
        var f = new IdolFilterViewModel();
        var idols = new List<IdolInfo>
        {
            new() { IdolId = 1, IdolName = "A" }, new() { IdolId = 2, IdolName = "B" }, new() { IdolId = 3, IdolName = "C" },
        };
        var units = new List<IdolUnit> { new("U1", new() { 1, 2 }), new("U2", new() { 3 }) };
        f.Rebuild(idols, units, on, unit);
        return f;
    }

    [Fact]
    public void UnitSwitch_KeepsCheckState()
    {
        var f = Make(on: new[] { 1, 3 }, unit: "U1");
        Assert.Equal(new[] { 1, 2 }, f.VisibleMembers.Select(m => m.Id));
        f.VisibleMembers[1].IsChecked = true;   // B 켬
        f.SelectedUnit = f.Units.Single(u => u.Name == "U2");
        Assert.Equal(new[] { 3 }, f.VisibleMembers.Select(m => m.Id));
        Assert.Equal(new[] { 1, 2, 3 }, f.CheckedIds);
    }

    [Fact]
    public void ToggleUnit_OnlyAffectsSelectedUnit_AndNotifiesOnce()
    {
        var f = Make(unit: "U1");   // 전부 켜짐
        int changed = 0;
        f.Changed += () => changed++;
        f.ToggleUnitCommand.Execute(null);
        Assert.Equal(new[] { 3 }, f.CheckedIds);
        Assert.Equal(1, changed);
        Assert.Equal("U1 (0/2)", f.SelectedUnit!.Label);
        Assert.Equal("전체 (1/3)", f.Units[0].Label);
    }

    [Fact]
    public void ToggleAll_IgnoresUnit_AndNotifiesOnce()
    {
        var f = Make(on: new[] { 1 }, unit: "U2");
        int changed = 0;
        f.Changed += () => changed++;
        f.ToggleAllCommand.Execute(null);           // 일부만 켜짐 → 모두 켬
        Assert.Equal(new[] { 1, 2, 3 }, f.CheckedIds);
        f.ToggleAllCommand.Execute(null);           // 모두 켜짐 → 모두 끔
        Assert.Empty(f.CheckedIds);
        Assert.Equal(2, changed);
        Assert.Equal("전체 (0/3)", f.Units[0].Label);
    }

    [Fact]
    public void Rebuild_DoesNotRaiseChanged()
    {
        var f = new IdolFilterViewModel();
        int changed = 0;
        f.Changed += () => changed++;
        f.Rebuild(new List<IdolInfo> { new() { IdolId = 1, IdolName = "A" } }, new List<IdolUnit>(), null, null);
        Assert.Equal(0, changed);
        Assert.Equal(IdolFilterViewModel.AllUnitsName, f.SelectedUnit!.Name);
    }
}

public class ResourceViewModelTests
{
    private static (ResourceViewModel vm, SpineLibrary lib, List<OpenSpineMessage> opened) Make(TempFolder t)
    {
        var snap = Fixture.Snapshot();
        var plan = SpinePlanner.Build(snap);
        var m = Fixture.Downloaded(t.Path, plan, v1: false);
        m.UpdateIdols(snap.Idols);
        var lib = new SpineLibrary(t.Path, m);
        lib.Rebuild();
        var messenger = new StrongReferenceMessenger();
        var opened = new List<OpenSpineMessage>();
        messenger.Register<OpenSpineMessage>(opened, (r, msg) => ((List<OpenSpineMessage>)r).Add(msg));
        var vm = new ResourceViewModel(new AppSettings(), lib, new List<IdolUnit>(), new FakeDialogs(), () => { }, messenger, new AppLog());
        return (vm, lib, opened);
    }

    [Fact]
    public void Items_HaveSeparatorsPerDressType()
    {
        using var t = new TempFolder();
        var (vm, _, _) = Make(t);
        Assert.Equal(new[] { "── P_SSR (2) ──", "【ほわっとスマイル】櫻木真乃", "【Merry】樋口円香", "── P_SR (1) ──", "【UNCHARTE:D】樋口円香" },
                     vm.Items.Select(i => i is ResourceSeparator s ? s.Title : ((ResourceAssetItem)i).Title));
    }

    [Fact]
    public void SearchAndFilter_NarrowItems()
    {
        using var t = new TempFolder();
        var (vm, _, _) = Make(t);
        vm.SearchText = "cb";
        Assert.Equal(2, vm.Items.Count);   // 구분선 1 + 세트 1
        vm.SearchText = "";
        vm.Filter.VisibleMembers.Single(m => m.Id == 21).IsChecked = false;
        Assert.Single(vm.Items.OfType<ResourceAssetItem>());
        Assert.Equal("◎ 0 · 세트 1 / 3개", vm.SummaryText);   // 변경: 앞에 게임 보유(◎) 개수
    }

    [Fact]
    public void Open_SendsMessage_AndInfoShowsKey()
    {
        using var t = new TempFolder();
        var (vm, _, opened) = Make(t);
        Assert.False(vm.OpenCommand.CanExecute(null));
        vm.SelectedItem = vm.Items.OfType<ResourceAssetItem>().First(i => i.Asset.CgKey == "madoka_cb_1030210010");
        Assert.Contains("CG 키: madoka_cb_1030210010", vm.InfoText);
        Assert.EndsWith("cb_1030210010.png", vm.PreviewPath);
        vm.OpenCommand.Execute(null);
        Assert.Equal("madoka_cb_1030210010", Assert.Single(opened).Asset.CgKey);
    }

    [Fact]
    public void LibraryChange_KeepsSelection()
    {
        using var t = new TempFolder();
        var (vm, lib, _) = Make(t);
        var target = vm.Items.OfType<ResourceAssetItem>().Last();
        vm.SelectedItem = target;
        lib.Rebuild();
        Assert.Equal(target.Asset.SourcePath, vm.SelectedAsset?.SourcePath);
    }
}

public class DownloadViewModelTests
{
    [Fact]
    public async Task Refresh_ThenDownloadChecked_UpdatesRowsAndLibrary()
    {
        using var t = new TempFolder();
        var settings = new AppSettings { SpineDataFolderSetting = t.Path };
        var manifest = new SpineManifest();
        var lib = new SpineLibrary(settings.SaveFolder, manifest);
        var dialogs = new FakeDialogs();
        var vm = new DownloadViewModel(settings, lib, new List<IdolUnit>(), dialogs,
            () => new FakeCatalog(Fixture.Snapshot()), () => new FakeDownloader(settings.SaveFolder, manifest), () => { }, new AppLog());

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.VisibleRows.Count);
        Assert.All(vm.VisibleRows, r => Assert.Equal(SetRowViewModel.Waiting, r.Status));
        Assert.True(File.Exists(CatalogSnapshot.IdolListPath(settings.MetaFolder)));

        // 円香만 켜고 받기
        vm.Filter.VisibleMembers.Single(m => m.Id == 1).IsChecked = false;
        Assert.Equal(2, vm.VisibleRows.Count);
        await vm.DownloadCheckedCommand.ExecuteAsync(null);

        Assert.All(vm.VisibleRows, r => Assert.Equal(SetRowViewModel.Done, r.Status));
        Assert.Equal(2, lib.Count);
        Assert.NotNull(lib.Find("madoka_stand_1040210050"));
        Assert.False(vm.IsBusy);
        Assert.Equal(0, dialogs.NoticeCount);   // 첫 목록은 비교 안 함
    }

    [Fact]
    public async Task DownloadSelected_OnlySelectedRows()
    {
        using var t = new TempFolder();
        var settings = new AppSettings { SpineDataFolderSetting = t.Path };
        var manifest = new SpineManifest();
        var lib = new SpineLibrary(settings.SaveFolder, manifest);
        var vm = new DownloadViewModel(settings, lib, new List<IdolUnit>(), new FakeDialogs(),
            () => new FakeCatalog(Fixture.Snapshot()), () => new FakeDownloader(settings.SaveFolder, manifest), () => { }, new AppLog());

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.DownloadSelectedCommand.CanExecute(null));   // 선택 없음
        var target = vm.VisibleRows[0];
        vm.SelectedRows = [target];
        Assert.True(vm.DownloadSelectedCommand.CanExecute(null));
        await vm.DownloadSelectedCommand.ExecuteAsync(null);

        Assert.Equal(SetRowViewModel.Done, target.Status);
        Assert.Equal(1, lib.Count);
        Assert.Equal(2, vm.VisibleRows.Count(r => r.Status == SetRowViewModel.Waiting));
    }

    [Fact]
    public async Task DownloadCancelledByUser_DoesNothing()
    {
        using var t = new TempFolder();
        var settings = new AppSettings { SpineDataFolderSetting = t.Path };
        var manifest = new SpineManifest();
        var lib = new SpineLibrary(settings.SaveFolder, manifest);
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var vm = new DownloadViewModel(settings, lib, new List<IdolUnit>(), dialogs,
            () => new FakeCatalog(Fixture.Snapshot()), () => new FakeDownloader(settings.SaveFolder, manifest), () => { }, new AppLog());

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.DownloadAllCommand.ExecuteAsync(null);
        Assert.Equal(0, lib.Count);
    }

    [Fact]
    public void LoadSavedSnapshot_MigratesV1Manifest()
    {
        using var t = new TempFolder();
        var settings = new AppSettings { SpineDataFolderSetting = t.Path };
        var snap = Fixture.Snapshot();
        snap.RawIdolList = System.Text.Json.JsonSerializer.Serialize(snap.Idols);
        foreach (var (id, list) in snap.Dresses) snap.RawDressLists[id] = System.Text.Json.JsonSerializer.Serialize(list);
        snap.Save(settings.MetaFolder);

        var manifest = Fixture.Downloaded(settings.SaveFolder, SpinePlanner.Build(snap), v1: true);
        var lib = new SpineLibrary(settings.SaveFolder, manifest);
        lib.Rebuild();
        Assert.Equal("stand_1040210050", lib.Assets.First(a => a.EnzaId == "1040210050").CgKey);   // 아이돌 정보 없음

        var vm = new DownloadViewModel(settings, lib, new List<IdolUnit>(), new FakeDialogs(),
            () => new FakeCatalog(snap), () => new FakeDownloader(settings.SaveFolder, manifest), () => { }, new AppLog());
        vm.LoadSavedSnapshot();

        Assert.NotNull(lib.Find("madoka_stand_1040210050"));
        Assert.Equal(SpineManifest.CurrentVersion, SpineManifest.Load(settings.MetaFolder).Version);
        Assert.All(vm.VisibleRows, r => Assert.Equal(SetRowViewModel.Done, r.Status));
    }
}
