using System;
using YooAsset.Editor;

namespace TEngine.FairyGUIIntegration.Editor
{
    [DisplayName("定位地址: AssetRaw完整相对路径(含扩展名)")]
    public sealed class FguiAddressByRelativePath : IAddressRule
    {
        private const string Prefix = "Assets/AssetRaw/";

        string IAddressRule.GetAssetAddress(AddressRuleData data)
        {
            string path = data.AssetPath.Replace('\\', '/');
            if (!path.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"FGUI asset must be below '{Prefix}': {path}");
            return path.Substring(Prefix.Length);
        }
    }
}
