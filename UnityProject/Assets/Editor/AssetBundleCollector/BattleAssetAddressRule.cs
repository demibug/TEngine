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

        string relativePath = Path.GetRelativePath(data.CollectPath, data.AssetPath);
        return Path.ChangeExtension(relativePath, null).Replace('\\', '/');
    }
}
