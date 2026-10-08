using System.IO;
using SCSpineManager.Services;

namespace SCSpineManager.Models;

public enum ResourceNodeKind { Root, DressType, Dress, Set, File }

/// <summary>
/// 리소스 탭 트리의 항목 1개. 실제 폴더 구조와 같게 표시한다:
/// SpineData / {DressType} / {DressName} / {세트} / {파일}
/// (세트 = 같은 이름의 json·atlas·png 묶음. 실제 폴더에는 세트 단계 없이 파일이 바로 있음)
/// </summary>
public sealed class ResourceNode
{
    public required ResourceNodeKind Kind { get; init; }
    public required string Name { get; init; }
    /// <summary>실제 경로 (폴더 또는 파일)</summary>
    public required string FullPath { get; init; }
    public List<ResourceNode> Children { get; } = new();

    /// <summary>트리에 표시할 글자 (이름 + 개수)</summary>
    public string Display { get; set; } = "";
    public bool IsExpanded { get; set; }

    // 세트·파일 항목에만 있음
    public SpineSet? Set { get; init; }
    public ManifestEntry? Entry { get; init; }
}

/// <summary>받은 세트 → 리소스 트리</summary>
public static class ResourceTreeBuilder
{
    // 세트 정렬: 그룹 → 타입 → 이름
    private static readonly string[] GroupOrder = { "idols", "awake_idols", "support_idols", "idol_evolution_skins" };
    private static readonly string[] TypeOrder = { "cb", "cb_costume", "stand", "stand_costume", "picture_motion" };

    private static int IndexOr(string[] arr, string v) { int i = Array.IndexOf(arr, v); return i < 0 ? arr.Length : i; }

    /// <param name="sets">받은 세트 (완료된 것만)</param>
    /// <param name="idolOrder">idolId → 아이돌 목록 순서</param>
    public static ResourceNode Build(string root, IEnumerable<SpineSet> sets, SpineManifest manifest, IReadOnlyDictionary<int, int> idolOrder)
    {
        var rootNode = new ResourceNode { Kind = ResourceNodeKind.Root, Name = Path.GetFileName(root.TrimEnd('\\', '/')), FullPath = root, IsExpanded = true };

        // (DressType) → (상대 폴더) → 세트들
        var types = new Dictionary<string, (int order, Dictionary<string, (SpineTarget target, int idolIdx, string enzaId, List<SpineSet> sets)> dresses)>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in sets)
        {
            if (!manifest.Sets.ContainsKey(set.SourcePath)) continue;
            int idolIdx = idolOrder.TryGetValue(set.IdolId, out var ix) ? ix : int.MaxValue;
            foreach (var t in set.Targets)
            {
                if (!types.TryGetValue(t.DressType, out var type))
                    types[t.DressType] = type = (t.DressTypeOrder, new(StringComparer.OrdinalIgnoreCase));
                if (!type.dresses.TryGetValue(t.RelativeFolder, out var dress))
                    type.dresses[t.RelativeFolder] = dress = (t, idolIdx, set.EnzaId, new List<SpineSet>());
                dress.sets.Add(set);
            }
        }

        int totalSets = 0;
        foreach (var (typeName, type) in types.OrderBy(kv => kv.Value.order).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string typeFolder = SpineNaming.CleanName(typeName);
            var typeNode = new ResourceNode { Kind = ResourceNodeKind.DressType, Name = typeFolder, FullPath = Path.Combine(root, typeFolder) };
            int typeSets = 0;

            // 의상 정렬: 아이돌 목록 순서 → enzaId (같은 아이돌 안에서는 발매 순에 가까움)
            foreach (var (folder, dress) in type.dresses.OrderBy(kv => kv.Value.idolIdx).ThenBy(kv => kv.Value.enzaId, StringComparer.Ordinal))
            {
                var dressNode = new ResourceNode
                {
                    Kind = ResourceNodeKind.Dress,
                    Name = Path.GetFileName(folder),
                    FullPath = Path.Combine(root, folder),
                };
                foreach (var set in dress.sets.OrderBy(s => IndexOr(GroupOrder, s.Group)).ThenBy(s => IndexOr(TypeOrder, s.SpineType)).ThenBy(s => s.BaseName, StringComparer.Ordinal))
                {
                    var entry = manifest.Sets[set.SourcePath];
                    var setNode = new ResourceNode
                    {
                        Kind = ResourceNodeKind.Set, Name = set.BaseName, Display = set.BaseName,
                        FullPath = dressNode.FullPath, Set = set, Entry = entry,
                    };
                    foreach (var name in FileNames(entry))
                        setNode.Children.Add(new ResourceNode
                        {
                            Kind = ResourceNodeKind.File, Name = name, Display = name,
                            FullPath = Path.Combine(dressNode.FullPath, name), Set = set, Entry = entry,
                        });
                    dressNode.Children.Add(setNode);
                }
                dressNode.Display = $"{dressNode.Name}  ({dressNode.Children.Count})";
                typeNode.Children.Add(dressNode);
                typeSets += dressNode.Children.Count;
            }
            typeNode.Display = $"{typeNode.Name}  (의상 {typeNode.Children.Count} · 세트 {typeSets})";
            rootNode.Children.Add(typeNode);
            totalSets += typeSets;
        }
        rootNode.Display = $"{rootNode.Name}  (세트 {totalSets})";
        return rootNode;
    }

    public static IEnumerable<string> FileNames(ManifestEntry e)
    {
        if (e.Json != null) yield return e.Json.SavedName;
        if (e.Atlas != null) yield return e.Atlas.SavedName;
        foreach (var i in e.Images) yield return i.SavedName;
    }
}
