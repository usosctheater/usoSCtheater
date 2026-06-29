using UnityEditor;

/// <summary>
/// Assets/Resources/BG 폴더에 임포트되는 이미지의
/// TextureType을 Sprite(2D and UI), SpriteMode를 Single로 자동 변환합니다.
/// </summary>
public class BGTexturePostprocessor : AssetPostprocessor
{
    private const string BG_PATH = "Assets/Resources/BG/";

    private void OnPreprocessTexture()
    {

        if (!assetPath.StartsWith(BG_PATH)) return;

        TextureImporter importer = (TextureImporter)assetImporter;

        if (importer.textureType == TextureImporterType.Sprite
            && importer.spriteImportMode == SpriteImportMode.Single) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
    }
}