namespace UsoSCTheater
{
    /// <summary>
    /// [리소스 정리] 리소스 폴더 경로의 유일한 원본 (2026-10-29). 불러오는 코드(Resources.Load 경로)와 에디터 임포트 규칙·검사가 모두 이 상수를 씀
    /// 폴더를 옮기거나 이름을 바꾸면 여기만 고침 → ResourcePostprocessor 규칙 표도 함께 따라감
    /// TE는 BG·Spine·Scenario·Audio 폴더 이름을 따로 가지고 있음(도구 독립 규칙) → 이 넷을 바꾸면 TE AppSettings·AudioLibrary도 함께 고칠 것
    /// </summary>
    public static class ResourcePaths
    {
        // ── Resources.Load 경로 (Resources 기준) ──
        public const string BG = "BG";
        public const string ScenarioImage = "ScenarioImage";        // 막 XML IMAGE 키 (구 Image — UI 이미지는 Assets/Images/UI로 분리)
        public const string CharacterIcon = "CharacterIcon";
        public const string IrisIcon = "IrisIcon";
        public const string ScenarioThumbnail = "ScenarioThumbnail";
        public const string Spine = "Spine";
        public const string Scenario = "Scenario";
        public const string Data = "Data";
        public const string AudioBGM = "Audio/BGM";
        public const string AudioSE = "Audio/SE";
        public const string AudioVoice = "Audio/Voice";

        // ── 에디터용 에셋 경로 (Assets 기준) ──
        public const string AssetsRoot = "Assets/Resources";          // 키로 불러오는 리소스
        public const string AssetsImages = "Assets/Images";           // 씬·프리팹이 직접 참조하는 이미지 (Resources 밖, 하위 UI/)
    }
}
