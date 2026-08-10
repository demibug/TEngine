using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 将 Assets/AssetRaw/Battle/Sprites/LevelBadge 下的等级数字图片统一为 Single 单图导入。
/// 运行时 UnityBattleViewPort 以 LoadAssetAsyncHandle&lt;Sprite&gt; 按地址加载整张单图。
/// </summary>
public static class LevelBadgeSpriteImporter
{
    private const string LevelBadgeDir = "Assets/AssetRaw/Battle/Sprites/LevelBadge";

    [MenuItem("Tools/GameBattle/设置等级数字 Sprite 为单图导入")]
    public static void SetSingleSpriteImport()
    {
        if (!Directory.Exists(LevelBadgeDir))
        {
            Debug.LogError($"[LevelBadge] 目录不存在: {LevelBadgeDir}");
            return;
        }

        int changed = 0;
        foreach (string png in Directory.GetFiles(LevelBadgeDir, "level_number_*.png"))
        {
            TextureImporter importer = AssetImporter.GetAtPath(png) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[LevelBadge] 无法读取导入器: {png}");
                continue;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(png);
            AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceUpdate);

            // 导入后实测验证：确认能按 Single 主 Sprite 加载，并打印实际 Sprite 信息。
            Sprite loaded = AssetDatabase.LoadAssetAtPath<Sprite>(png);
            if (loaded == null)
            {
                Debug.LogError($"[LevelBadge] 导入后无法按 Sprite 加载: {png}，请检查 spriteImportMode");
            }
            else
            {
                Debug.Log(
                    $"[LevelBadge] OK {Path.GetFileName(png)} sprite={loaded.name} " +
                    $"rect={loaded.rect} ppu={loaded.pixelsPerUnit} " +
                    $"mode={importer.spriteImportMode}");
            }

            changed++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[LevelBadge] 已将 {changed} 张等级数字图片设置为 Single 单图导入");
    }
}
