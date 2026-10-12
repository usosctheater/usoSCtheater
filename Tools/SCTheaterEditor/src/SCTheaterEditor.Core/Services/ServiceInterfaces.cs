using SCTheaterEditor.Models;

namespace SCTheaterEditor.Services;

/// <summary>목록 받기 (실제: SpineCatalogClient, 테스트: 가짜)</summary>
public interface ICatalogSource : IDisposable
{
    Task<CatalogSnapshot> FetchAsync(IProgress<string> log, CancellationToken ct);
}

/// <summary>세트 받기 (실제: SpineDownloader, 테스트: 가짜)</summary>
public interface ISetDownloader : IDisposable
{
    Task<SetResult> ProcessAsync(SpineSet set, CancellationToken ct);

    /// <summary>지금까지 보낸 요청 수</summary>
    int RequestCount { get; }
}
