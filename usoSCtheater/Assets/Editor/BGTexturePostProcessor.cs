using UnityEditor;

/// <summary>
/// Assets/Resources/BG, Image 폴더에 임포트되는 이미지의
/// TextureType을 Sprite(2D and UI), SpriteMode를 Single로 자동 변환합니다.
/// </summary>
public class BGTexturePostprocessor : AssetPostprocessor
{
    private static readonly string[] TARGET_PATHS =
    {
        "Assets/Resources/BG/",
        "Assets/Resources/Image/"
    };

    private void OnPreprocessTexture()
    {
        bool isTarget = false;
        foreach (var path in TARGET_PATHS)
        {
            if (assetPath.StartsWith(path))
            {
                isTarget = true;
                break;
            }
        }
        if (!isTarget) return;

        TextureImporter importer = (TextureImporter)assetImporter;

        if (importer.textureType == TextureImporterType.Sprite
            && importer.spriteImportMode == SpriteImportMode.Single) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
    }
}