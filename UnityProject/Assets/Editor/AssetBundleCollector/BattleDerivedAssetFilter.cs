using System;
using YooAsset.Editor;

/// <summary>
/// 收集 Battle 派生资源，并排除只读 Laya 源图集目录。
/// </summary>
public sealed class BattleDerivedAssetFilter : IFilterRule
{
    private const string LAYA_SOURCE_PATH = "Assets/AssetRaw/Battle/Source/Laya";

    /// <inheritdoc />
    public string FindAssetType => EAssetSearchType.All.ToString();

    /// <inheritdoc />
    public bool IsCollectAsset(FilterRuleData data)
    {
        string assetPath = data.AssetPath;
        return !string.Equals(assetPath, LAYA_SOURCE_PATH, StringComparison.Ordinal)
            && !assetPath.StartsWith(LAYA_SOURCE_PATH + "/", StringComparison.Ordinal);
    }
}
