#nullable enable
using System;
using System.Collections.Generic;

namespace UsoSCTheater.Shared
{
    /// <summary>
    /// [공유 코드 통합] 추가(5회차): 게임 Resources 폴더 이름 (Resources 기준, / 구분) — 게임 Resources.Load·TE 게임 폴더 찾기 공용.
    /// 폴더를 옮기거나 이름을 바꾸면 여기만 고침 (게임 ResourcePostprocessor 규칙 표도 함께 확인)
    /// </summary>
    public static class ResourceFolders
    {
        public const string BG = "BG";
        public const string ScenarioImage = "ScenarioImage";        //막 XML IMAGE 키
        public const string CharacterIcon = "CharacterIcon";
        public const string IrisIcon = "IrisIcon";
        public const string ScenarioThumbnail = "ScenarioThumbnail";
        public const string Spine = "Spine";
        public const string Scenario = "Scenario";
        public const string Data = "Data";
        public const string AudioBGM = "Audio/BGM";
        public const string AudioSE = "Audio/SE";
        public const string AudioVoice = "Audio/Voice";

        //Spine 배치: Spine/{캐릭터 ID}/{키}.json·.atlas.txt·.png (캐릭터 ID = 키의 첫 _ 앞 = idollist nickName)
        public static string SpineFolder(string characterId) => Spine + "/" + characterId;
    }

    /// <summary>[공유 코드 통합] 추가(5회차): 소리 규칙 — AUDIO 슬롯, 보이스 찾는 경로</summary>
    public static class AudioRules
    {
        //AUDIO 줄 Value = 슬롯. bgm은 BGM 폴더·항상 반복, 1~3은 SE 폴더 (값이라 대소문자 무시)
        public const string SlotBgm = "bgm";
        public static readonly IReadOnlyList<string> Slots = new[] { SlotBgm, "1", "2", "3" };
        public const string EffectLoop = "loop", EffectStop = "stop";

        public static bool IsSlot(string? slot)
        {
            foreach (string s in Slots)
                if (string.Equals(s, (slot ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsBgm(string? slot) => string.Equals((slot ?? "").Trim(), SlotBgm, StringComparison.OrdinalIgnoreCase);

        //슬롯 소리가 있는 폴더
        public static string SlotFolder(string? slot) => IsBgm(slot) ? ResourceFolders.AudioBGM : ResourceFolders.AudioSE;

        //보이스 찾는 순서 (확장자 없음): Voice/{시나리오 폴더}/{키} → Voice/{키}. 키에 폴더를 붙인 "OT/OT001"도 뒤쪽에서 찾힘
        public static List<string> VoicePaths(string? scenarioFolder, string key)
        {
            var paths = new List<string>();
            string folder = (scenarioFolder ?? "").Trim().Trim('/');
            string k = key.Trim().Replace('\\', '/');
            if (folder.Length > 0) paths.Add($"{ResourceFolders.AudioVoice}/{folder}/{k}");
            paths.Add($"{ResourceFolders.AudioVoice}/{k}");
            return paths;
        }
    }
}
