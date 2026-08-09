using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 将 Battle 的 Laya 图集确定性展开为独立 PNG 图片。
/// </summary>
public static class LayaAtlasExtractor
{
    private const string LAYA_SOURCE_ROOT = "Assets/AssetRaw/Battle/Source/Laya";
    private const string DERIVED_ROOT = "Assets/AssetRaw/Battle/Sprites/Extracted";

    /// <summary>
    /// 从菜单执行全部 Battle Laya 图集提取。
    /// </summary>
    [MenuItem("Tools/Battle/提取 Laya 图集")]
    public static void ExtractAll()
    {
        string sourceRootPath = ToFullPath(LAYA_SOURCE_ROOT);
        if (!Directory.Exists(sourceRootPath))
        {
            throw new DirectoryNotFoundException($"未找到 Laya 图集源目录：{LAYA_SOURCE_ROOT}");
        }

        string[] atlasPaths = Directory.GetFiles(sourceRootPath, "*.atlas", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (atlasPaths.Length == 0)
        {
            throw new InvalidOperationException($"Laya 图集源目录中没有 .atlas 文件：{LAYA_SOURCE_ROOT}");
        }

        int extractedCount = 0;
        try
        {
            for (int i = 0; i < atlasPaths.Length; i++)
            {
                extractedCount += ExtractAtlas(atlasPaths[i], sourceRootPath);
            }
        }
        finally
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        Debug.Log($"[LayaAtlasExtractor] 已提取 {extractedCount} 张图片到 {DERIVED_ROOT}。");
    }

    private static int ExtractAtlas(string atlasPath, string sourceRootPath)
    {
        JObject atlas = JObject.Parse(File.ReadAllText(atlasPath));
        string imageName = atlas["meta"]?["image"]?.Value<string>();
        JObject frames = atlas["frames"] as JObject;
        if (string.IsNullOrWhiteSpace(imageName) || frames == null)
        {
            throw new InvalidDataException($"图集缺少 meta.image 或 frames：{atlasPath}");
        }

        string sourceDirectory = Path.GetDirectoryName(atlasPath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new InvalidDataException($"图集路径无效：{atlasPath}");
        }

        string imagePath = Path.Combine(sourceDirectory, imageName);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"图集图片不存在：{imagePath}", imagePath);
        }

        string relativeDirectory = Path.GetRelativePath(sourceRootPath, sourceDirectory);
        string destinationDirectory = Path.Combine(ToFullPath(DERIVED_ROOT), relativeDirectory);
        Directory.CreateDirectory(destinationDirectory);

        Texture2D atlasTexture = LoadTexture(imagePath);
        try
        {
            int extractedCount = 0;
            foreach (JProperty frameProperty in frames.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                string relativeOutputPath = GetSafeRelativePath(frameProperty.Name);
                JObject frameDefinition = frameProperty.Value as JObject;
                if (frameDefinition == null)
                {
                    throw new InvalidDataException($"帧定义无效：atlas={atlasPath}, frame={frameProperty.Name}");
                }

                Texture2D frameTexture = ExtractFrame(atlasTexture, frameDefinition, atlasPath, frameProperty.Name);
                try
                {
                    string outputPath = Path.Combine(destinationDirectory, relativeOutputPath);
                    string outputDirectory = Path.GetDirectoryName(outputPath);
                    if (string.IsNullOrWhiteSpace(outputDirectory))
                    {
                        throw new InvalidDataException($"输出路径无效：{outputPath}");
                    }
                    Directory.CreateDirectory(outputDirectory);
                    File.WriteAllBytes(outputPath, frameTexture.EncodeToPNG());
                    extractedCount++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(frameTexture);
                }
            }

            return extractedCount;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(atlasTexture);
        }
    }

    private static Texture2D ExtractFrame(Texture2D atlasTexture, JObject frameDefinition, string atlasPath, string frameName)
    {
        JObject frame = RequireObject(frameDefinition, "frame", atlasPath, frameName);
        int frameX = RequireInt(frame, "x", atlasPath, frameName);
        int frameY = RequireInt(frame, "y", atlasPath, frameName);
        int frameWidth = RequireInt(frame, "w", atlasPath, frameName);
        int frameHeight = RequireInt(frame, "h", atlasPath, frameName);
        bool rotated = ReadBoolean(frameDefinition, "rotated");

        if (frameX < 0 || frameY < 0 || frameWidth <= 0 || frameHeight <= 0
            || frameX + frameWidth > atlasTexture.width || frameY + frameHeight > atlasTexture.height)
        {
            throw new InvalidDataException($"帧区域越界：atlas={atlasPath}, frame={frameName}");
        }

        int bottomY = atlasTexture.height - frameY - frameHeight;
        Color[] pixels = atlasTexture.GetPixels(frameX, bottomY, frameWidth, frameHeight);
        int trimmedWidth = frameWidth;
        int trimmedHeight = frameHeight;
        if (rotated)
        {
            pixels = RotateCounterClockwise(pixels, frameWidth, frameHeight);
            trimmedWidth = frameHeight;
            trimmedHeight = frameWidth;
        }

        JObject sourceSize = frameDefinition["sourceSize"] as JObject;
        int sourceWidth = sourceSize == null ? trimmedWidth : RequireInt(sourceSize, "w", atlasPath, frameName);
        int sourceHeight = sourceSize == null ? trimmedHeight : RequireInt(sourceSize, "h", atlasPath, frameName);
        JObject spriteSourceSize = frameDefinition["spriteSourceSize"] as JObject;
        int sourceX = spriteSourceSize == null ? 0 : RequireInt(spriteSourceSize, "x", atlasPath, frameName);
        int sourceY = spriteSourceSize == null ? 0 : RequireInt(spriteSourceSize, "y", atlasPath, frameName);
        int targetY = sourceHeight - sourceY - trimmedHeight;

        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceX < 0 || sourceY < 0
            || sourceX + trimmedWidth > sourceWidth || targetY < 0 || targetY + trimmedHeight > sourceHeight)
        {
            throw new InvalidDataException($"帧裁剪信息无效：atlas={atlasPath}, frame={frameName}");
        }

        Texture2D result = new Texture2D(sourceWidth, sourceHeight, TextureFormat.RGBA32, false)
        {
            name = Path.GetFileNameWithoutExtension(frameName),
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        // 先清空整张画布为透明，再写入裁剪区域，避免 sourceSize 大于裁剪帧时
        // 未覆盖区域残留默认非透明像素（灰圈/灰底）。
        Color[] clearPixels = new Color[sourceWidth * sourceHeight];
        for (int i = 0; i < clearPixels.Length; i++)
        {
            clearPixels[i] = Color.clear;
        }

        result.SetPixels(0, 0, sourceWidth, sourceHeight, clearPixels);
        result.SetPixels(sourceX, targetY, trimmedWidth, trimmedHeight, pixels);
        result.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return result;
    }

    private static Color[] RotateCounterClockwise(Color[] source, int width, int height)
    {
        Color[] result = new Color[source.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = y * width + x;
                int destinationX = y;
                int destinationY = width - x - 1;
                result[destinationY * height + destinationX] = source[sourceIndex];
            }
        }

        return result;
    }

    private static Texture2D LoadTexture(string imagePath)
    {
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(imagePath), markNonReadable: false))
        {
            UnityEngine.Object.DestroyImmediate(texture);
            throw new InvalidDataException($"无法读取图集图片：{imagePath}");
        }

        return texture;
    }

    private static JObject RequireObject(JObject source, string name, string atlasPath, string frameName)
    {
        if (source[name] is JObject result)
        {
            return result;
        }

        throw new InvalidDataException($"帧缺少对象字段 {name}：atlas={atlasPath}, frame={frameName}");
    }

    private static int RequireInt(JObject source, string name, string atlasPath, string frameName)
    {
        if (source[name] != null && int.TryParse(source[name].ToString(), out int value))
        {
            return value;
        }

        throw new InvalidDataException($"帧缺少整数字段 {name}：atlas={atlasPath}, frame={frameName}");
    }

    private static bool ReadBoolean(JObject source, string name)
    {
        JToken value = source[name];
        return value != null && bool.TryParse(value.ToString(), out bool result) && result;
    }

    private static string GetSafeRelativePath(string atlasFrameName)
    {
        string[] parts = atlasFrameName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part == "." || part == ".."))
        {
            throw new InvalidDataException($"图集帧路径无效：{atlasFrameName}");
        }

        return Path.Combine(parts);
    }

    private static string ToFullPath(string assetPath)
    {
        DirectoryInfo projectRootInfo = Directory.GetParent(Application.dataPath);
        if (projectRootInfo == null)
        {
            throw new InvalidOperationException($"无法定位 Unity 工程根目录：{Application.dataPath}");
        }

        string projectRoot = projectRootInfo.FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }
}
