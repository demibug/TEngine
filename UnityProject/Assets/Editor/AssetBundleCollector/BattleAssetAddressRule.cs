using System.IO;
using YooAsset.Editor;

/// <summary>
/// 为 Battle 资源生成稳定且不冲突的 YooAsset 地址。
/// </summary>
public sealed class BattleAssetAddressRule : IAddressRule
{
    /// <inheritdoc />
    public string GetAssetAddress(AddressRuleData data)
    {
        if (Path.GetExtension(data.AssetPath) == ".prefab")
        {
            return Path.GetFileNameWithoutExtension(data.AssetPath);
        }

        // Spine 源文件目录内 json/png 等同名文件去扩展名后会地址冲突，
        // 这些目录保留扩展名（相对 CollectPath 的完整路径）避免冲突。
        // Prefab 已由上面的文件名去扩展名规则处理，不受影响。
        string normalizedAssetPath = data.AssetPath.Replace('\\', '/');
        if (IsSpineSourceDirectory(normalizedAssetPath))
        {
            return Path.GetRelativePath(data.CollectPath, data.AssetPath)
                .Replace('\\', '/');
        }

        string relativePath = Path.GetRelativePath(data.CollectPath, data.AssetPath);
        return Path.ChangeExtension(relativePath, null).Replace('\\', '/');
    }

    // 含 Spine 源文件（json/atlas/png）的目录，按扩展名区分地址避免同名冲突。
    private static readonly string[] SpineSourceDirectories =
    {
        "Assets/AssetRaw/Battle/Prefabs/Enemies/ZhangLiang/",
        "Assets/AssetRaw/Battle/Prefabs/Generals/ZhangFei/",
        "Assets/AssetRaw/Battle/Prefabs/Generals/HuangZhong/",
    };

    private static bool IsSpineSourceDirectory(string normalizedAssetPath)
    {
        for (int index = 0; index < SpineSourceDirectories.Length; index++)
        {
            if (normalizedAssetPath.StartsWith(
                    SpineSourceDirectories[index], System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
