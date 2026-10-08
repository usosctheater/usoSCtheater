using System.Text.Json.Serialization;

namespace SCSpineManager.Models;

/// <summary>idollist API 항목 (https://api.shinycolors.moe/spine/idollist)</summary>
public sealed class IdolInfo
{
    [JsonPropertyName("idolId")]   public int IdolId { get; set; }
    [JsonPropertyName("idolName")] public string IdolName { get; set; } = "";
    [JsonPropertyName("nickName")] public string NickName { get; set; } = "";
}

/// <summary>dresslist API 항목 (https://api.shinycolors.moe/spine/dresslist?idolId=N) — 의상 1벌</summary>
public sealed class DressInfo
{
    [JsonPropertyName("exist")]            public bool Exist { get; set; }
    [JsonPropertyName("idolId")]           public int IdolId { get; set; }
    [JsonPropertyName("dressName")]        public string DressName { get; set; } = "";
    [JsonPropertyName("dressUuid")]        public string DressUuid { get; set; } = "";
    [JsonPropertyName("dressType")]        public string DressType { get; set; } = "";
    [JsonPropertyName("enzaId")]           public string EnzaId { get; set; } = "";
    [JsonPropertyName("dress_type_order")] public int DressTypeOrder { get; set; }

    /// <summary>그룹명(idols / awake_idols / support_idols / idol_evolution_skins …) → Spine 목록</summary>
    [JsonPropertyName("assets")]
    public Dictionary<string, List<DressAsset>>? Assets { get; set; }
}

/// <summary>의상 안의 Spine 1개. path가 없는 항목(旧 의상 등)이 있음</summary>
public sealed class DressAsset
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}
