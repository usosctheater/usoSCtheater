#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace UsoSCTheater.Shared
{
    /// <summary>Spine 분류. LD = 커뮤니케이션 씬 Spine, SD = 엔딩 씬 Spine, Other = 목록에 없는 타입 (picture_motion 등)</summary>
    public enum SpineKind { LD, SD, Other }

    /// <summary>
    /// Spine 키 1개의 해석 결과. 키 = {캐릭터 ID}_{타입}_{enzaId} (예: fuyuko_cb_1050180010)
    /// = TE 내보내기 파일 이름 = 인덱스 Key = 막 XML CG 값 (핸드오프 4장 키 계약). 키는 대소문자 무시
    /// 타입에는 _가 들어갈 수 있음 (stand_costume, support_stand, awake_cb_costume ...)
    /// </summary>
    public sealed class SpineKeyInfo
    {
        public string Key { get; }
        public string CharacterId { get; }
        public string Type { get; }        // awake_ 접두어 포함 원본 타입
        public string EnzaId { get; }
        public SpineKind Kind { get; }
        public bool IsCostume { get; }     // 타입이 _costume으로 끝남 (엔딩 랜덤 후보에서 끌 수 있음)

        internal SpineKeyInfo(string key, string characterId, string type, string enzaId)
        {
            Key = key;
            CharacterId = characterId;
            Type = type;
            EnzaId = enzaId;
            Kind = SpineKeys.GetKind(type);
            IsCostume = type.EndsWith("_costume", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Spine 키 규칙 — 게임·TE 공용, 분류 기준은 여기 한 곳에만 둠.
    /// 원본은 게임 패키지 com.usosctheater.shared. TE src/Shared는 사본이라 직접 고치지 않음 (고친 뒤 sync-shared.ps1)
    /// </summary>
    public static class SpineKeys
    {
        public const string AwakePrefix = "awake_";                  // TE: awake_idols 그룹 접두어

        private static readonly HashSet<string> LdTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "stand", "stand_costume", "support_stand" };
        private static readonly HashSet<string> SdTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "cb", "cb_costume" };

        //형식이 맞지 않으면 false (Id = 첫 _ 앞, EnzaId = 마지막 _ 뒤 숫자, Type = 그 사이). 앞뒤 공백은 지우고 해석
        public static bool TryParse(string? key, [NotNullWhen(true)] out SpineKeyInfo? info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(key)) return false;
            string k = key!.Trim();

            int first = k.IndexOf('_');
            int last = k.LastIndexOf('_');
            if (first <= 0 || last <= first + 1 || last >= k.Length - 1) return false;

            for (int i = last + 1; i < k.Length; i++)
                if (!char.IsDigit(k[i])) return false;

            info = new SpineKeyInfo(k, k.Substring(0, first), k.Substring(first + 1, last - first - 1), k.Substring(last + 1));
            return true;
        }

        //awake_ 접두어는 떼고 판단. 목록에 없는 타입은 Other (예외는 발견 시 목록에 추가)
        public static SpineKind GetKind(string? type)
        {
            if (string.IsNullOrEmpty(type)) return SpineKind.Other;
            string t = type!;
            string baseType = t.StartsWith(AwakePrefix, StringComparison.OrdinalIgnoreCase) ? t.Substring(AwakePrefix.Length) : t;
            if (LdTypes.Contains(baseType)) return SpineKind.LD;
            if (SdTypes.Contains(baseType)) return SpineKind.SD;
            return SpineKind.Other;
        }

        //키 → 분류. 형식이 맞지 않으면 Other
        public static SpineKind KindOf(string? key) =>
            TryParse(key, out SpineKeyInfo? info) ? info.Kind : SpineKind.Other;

        //[엔딩 SD] LD 타입 → 같은 카드의 SD 타입 (stand → cb, stand_costume → cb_costume, awake_ 접두어 유지). 짝이 없으면 null (support_stand 등)
        public static string? GetPairedSDType(string? ldType)
        {
            if (string.IsNullOrEmpty(ldType)) return null;
            string t = ldType!;
            bool awake = t.StartsWith(AwakePrefix, StringComparison.OrdinalIgnoreCase);
            string baseType = awake ? t.Substring(AwakePrefix.Length) : t;

            string sdType;
            if (string.Equals(baseType, "stand", StringComparison.OrdinalIgnoreCase)) sdType = "cb";
            else if (string.Equals(baseType, "stand_costume", StringComparison.OrdinalIgnoreCase)) sdType = "cb_costume";
            else return null;
            return awake ? AwakePrefix + sdType : sdType;
        }
    }
}
