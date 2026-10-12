using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace SCSpineManager.Services;

/// <summary>
/// 립 애니메이션 결정 규칙 — CGManager.LipSyncCoroutine / GetDefaultLipEntry / ExtractLipKey와 동일.
/// - Animation에 lip_xxx가 있으면: 말하는 중 = lip_xxx, 멈춤 = lip_xxx_s (없으면 아이돌별 기본값의 Still)
/// - 없으면: 기본 트랙 애니메이션에서 키워드 추출(끝 숫자 제거, anger1 → anger, 없으면 wait)
///           → lip_{키워드} / lip_{키워드}_s, 없는 쪽은 아이돌별 기본값(DefaultLipData.xml)
/// - 아이돌별 기본값: {아이돌}/{키워드 카테고리} → 없으면 {아이돌}/Default → 아이돌 자체가 없으면 lip_wait / lip_wait_s
/// - 아이돌 키는 게임의 cgKey 앞부분(mei_idol_3 → mei)과 같은 형식 = idollist의 nickName
/// 데이터: 도구 안의 복사본 Resources\DefaultLipData.xml (exe에 포함). 원본은 게임 프로젝트
///         Assets/Resources/Data/DefaultLipData.xml — 게임 쪽을 고치면 복사본도 갱신 후 다시 빌드 (도구 독립 실행 규칙)
/// </summary>
public sealed class LipRules
{
    private readonly Dictionary<string, Dictionary<string, (string? moving, string? still)>> _defaults =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>변경: exe에 포함된 DefaultLipData.xml 읽기 (없거나 읽기 실패면 하드코딩 안전망만 사용)</summary>
    public static LipRules LoadEmbedded()
    {
        var rules = new LipRules();
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefaultLipData.xml");
            if (stream != null) rules.Read(XDocument.Load(stream));
        }
        catch { /* 안전망만 사용 */ }
        return rules;
    }

    private void Read(XDocument doc)
    {
        var root = doc.Root;
        if (root == null) return;
        foreach (var idol in root.Elements())
        {
            var categories = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in idol.Elements())
                categories[cat.Name.LocalName] = ((string?)cat.Attribute("Moving"), (string?)cat.Attribute("Still"));
            _defaults[idol.Name.LocalName] = categories;
        }
    }

    /// <summary>
    /// 말하는 중 / 멈춤 립 애니메이션 결정
    /// </summary>
    /// <param name="animations">Animation 값에 들어간 애니메이션들</param>
    /// <param name="idolKey">아이돌 키 (nickName)</param>
    /// <param name="exists">데이터에 그 애니메이션이 있는지</param>
    public (string? moving, string? still) Resolve(IReadOnlyList<string> animations, string idolKey, Func<string, bool> exists)
    {
        string? explicitLip = animations.FirstOrDefault(a => a.StartsWith("lip_"));
        if (explicitLip != null)
        {
            string moving = explicitLip;
            string candidate = moving + "_s";
            if (exists(candidate)) return (moving, candidate);
            string keyword = ExtractLipKey(moving.Substring(4));
            return (moving, GetDefault(idolKey, keyword, exists).still);
        }

        string? track0 = animations.FirstOrDefault(a => AnimationScript.TrackIndex(a) == 0);
        string lipKey = ExtractLipKey(track0);
        string candMoving = "lip_" + lipKey, candStill = "lip_" + lipKey + "_s";
        bool needDefault = !exists(candMoving) || !exists(candStill);
        var def = needDefault ? GetDefault(idolKey, lipKey, exists) : default;
        return (exists(candMoving) ? candMoving : def.moving,
                exists(candStill) ? candStill : def.still);
    }

    /// <summary>anger1 → anger, smile3 → smile, 비어 있으면 wait</summary>
    public static string ExtractLipKey(string? animName) =>
        string.IsNullOrEmpty(animName) ? "wait" : animName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

    private (string? moving, string? still) GetDefault(string idolKey, string keyword, Func<string, bool> exists)
    {
        string category = string.IsNullOrEmpty(keyword) ? "Default" : keyword;
        if (_defaults.TryGetValue(idolKey, out var cats))
        {
            if (cats.TryGetValue(category, out var e)) return e;
            if (cats.TryGetValue("Default", out var d)) return d;
        }
        // 아이돌 데이터가 없을 때의 안전망 (CGManager.DEFAULT_LIP_ANIM / DEFAULT_LIP_S_ANIM)
        return (exists("lip_wait") ? "lip_wait" : null, exists("lip_wait_s") ? "lip_wait_s" : null);
    }
}
