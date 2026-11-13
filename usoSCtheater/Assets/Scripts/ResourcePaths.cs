using UsoSCTheater.Shared;   //[공유 코드 통합] 추가(5회차): 폴더 이름은 공유 ResourceFolders

namespace UsoSCTheater
{
    /// <summary>
    /// [리소스 정리] 리소스 폴더 경로의 유일한 원본 (2026-10-29). 불러오는 코드(Resources.Load 경로)와 에디터 임포트 규칙·검사가 모두 이 상수를 씀
    /// 폴더를 옮기거나 이름을 바꾸면 여기만 고침 → ResourcePostprocessor 규칙 표도 함께 따라감
    /// [공유 코드 통합] 수정(5회차): 폴더 이름의 원본은 공유 ResourceFolders (TE도 같은 상수) — 여기 이름은 기존 코드가 쓰던 별칭
    /// </summary>
    public static class ResourcePaths
    {
        // ── Resources.Load 경로 (Resources 기준) ── [공유 코드 통합] 수정: 값 = 공유 상수
        public const string BG = ResourceFolders.BG;
        public const string ScenarioImage = ResourceFolders.ScenarioImage;        // 막 XML IMAGE 키 (구 Image — UI 이미지는 Assets/Images/UI로 분리)
        public const string CharacterIcon = ResourceFolders.CharacterIcon;
        public const string IrisIcon = ResourceFolders.IrisIcon;
        public const string ScenarioThumbnail = ResourceFolders.ScenarioThumbnail;
        public const string Spine = ResourceFolders.Spine;
        public const string Scenario = ResourceFolders.Scenario;
        public const string Data = ResourceFolders.Data;
        public const string AudioBGM = ResourceFolders.AudioBGM;
        public const string AudioSE = ResourceFolders.AudioSE;
        public const string AudioVoice = ResourceFolders.AudioVoice;

        // ── 에디터용 에셋 경로 (Assets 기준) ── 게임 전용
        public const string AssetsRoot = "Assets/Resources";          // 키로 불러오는 리소스
        public const string AssetsImages = "Assets/Images";           // 씬·프리팹이 직접 참조하는 이미지 (Resources 밖, 하위 UI/)
    }
}
