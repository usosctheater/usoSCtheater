using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [ResourcePostprocessor] 구 BGTexturePostprocessor — 2026-10-28 이름 변경·개편 (이미지만이 아니라 리소스 임포트 규칙 전체를 담당)
/// 공통 원칙 [E-006]: 규칙은 "처음 들어올 때"(.meta 없음 = importSettingsMissing)만 적용 → 이후 Inspector에서 바꾼 설정은 다시 가져와도 유지
///   (규칙을 바꿔도 이미 가져온 파일에는 적용되지 않음 → 기존 파일은 Inspector에서 직접 바꿈)
/// 규칙 표:
///   텍스처: Resources 아래 이미지 → Sprite(2D and UI)·Single (Spine 아틀라스 텍스처는 제외 — Default 유지)
///   오디오: 폴더별 Load Type (압축 형식·품질·Preload는 Unity 기본값 그대로)
/// </summary>
public class ResourcePostprocessor : AssetPostprocessor   //[이름 변경] BGTexturePostprocessor → ResourcePostprocessor
{
    // ── 규칙 표 (경로는 3회차에서 ResourcePaths로 통합 예정) ─────────────
    private const string RESOURCES_ROOT = "Assets/Resources/";

    //[텍스처] 이 폴더 아래 이미지는 Sprite·Single (구: BG·Image·CharacterIcon·IrisIcon 4개 폴더 → Resources 전체로 확대)
    private static readonly string[] SPRITE_ROOTS = { RESOURCES_ROOT };
    //[텍스처] 제외 (Spine 아틀라스 텍스처는 Default 텍스처로 사용)
    private static readonly string[] SPRITE_EXCLUDES = { RESOURCES_ROOT + "Spine/" };

    //[오디오] 폴더 → Load Type (하위 폴더 포함 — Voice/{시나리오}/ 등)
    private static readonly (string folder, AudioClipLoadType loadType)[] AUDIO_RULES =
    {
        (RESOURCES_ROOT + "Audio/BGM/",   AudioClipLoadType.Streaming),           //곡 전체를 PCM으로 풀지 않음
        (RESOURCES_ROOT + "Audio/Voice/", AudioClipLoadType.CompressedInMemory),  //대사마다 불러와 씬 끝까지 남으므로 압축 상태 유지
        (RESOURCES_ROOT + "Audio/SE/",    AudioClipLoadType.DecompressOnLoad),    //짧고 즉시 재생
    };

    // ── 텍스처 ───────────────────────────────────────────────────────────
    private void OnPreprocessTexture()
    {
        if (!assetImporter.importSettingsMissing) return;   //[E-006] 처음 들어올 때만
        if (!IsUnder(assetPath, SPRITE_ROOTS) || IsUnder(assetPath, SPRITE_EXCLUDES)) return;

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        // [삭제] '이미 Sprite·Single이면 건너뜀' 검사 — 처음 들어올 때만 오므로 불필요
    }

    // ── 오디오 (2026-10-28 추가) ─────────────────────────────────────────
    private void OnPreprocessAudio()
    {
        if (!assetImporter.importSettingsMissing) return;   //[E-006] 처음 들어올 때만

        foreach (var rule in AUDIO_RULES)
        {
            if (!assetPath.StartsWith(rule.folder, StringComparison.OrdinalIgnoreCase)) continue;

            AudioImporter importer = (AudioImporter)assetImporter;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = rule.loadType;
            importer.defaultSampleSettings = settings;
            return;
        }
    }

    private static bool IsUnder(string path, string[] roots)
    {
        foreach (string root in roots)
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
