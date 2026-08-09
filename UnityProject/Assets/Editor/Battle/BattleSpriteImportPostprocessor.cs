using UnityEditor;
using UnityEngine;

/// <summary>
/// 统一 Battle 派生图片的 Sprite 导入设置。
/// </summary>
public sealed class BattleSpriteImportPostprocessor : AssetPostprocessor
{
    private const string SPRITE_ROOT = "Assets/AssetRaw/Battle/Sprites/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(SPRITE_ROOT, System.StringComparison.Ordinal))
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
    }
}
