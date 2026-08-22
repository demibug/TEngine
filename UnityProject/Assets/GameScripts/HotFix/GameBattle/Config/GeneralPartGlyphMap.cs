using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>
    /// 武将拆字到字形贴图索引的静态映射。
    /// </summary>
    /// <remarks>
    /// 索引对应原始工程 GeneralDefinitions.GENERAL_PART_WORDS 的 20 个单字，
    /// 贴图位于 Assets/AssetRaw/Battle/Sprites/Extracted/GameObject/soldier/generalParts_{index}.png，
    /// 由 BattleAssetAddressRule 生成去扩展名的 YooAsset 地址。本期只用基础字形，
    /// 不使用 _4/_5 等级变体（当前卡牌不分等级字形）。
    /// </remarks>
    internal static class GeneralPartGlyphMap
    {
        private const string SpriteAddressPrefix = "Sprites/Extracted/GameObject/soldier/generalParts_";

        private static readonly IReadOnlyDictionary<string, int> PartWordToIndex = new Dictionary<string, int>
        {
            ["赵"] = 0, ["云"] = 1, ["张"] = 2, ["飞"] = 3, ["马"] = 4, ["超"] = 5,
            ["关"] = 6, ["羽"] = 7, ["平"] = 8, ["兴"] = 9, ["黄"] = 10, ["忠"] = 11,
            ["苞"] = 12, ["翼"] = 13, ["盖"] = 14, ["祖"] = 15, ["甄"] = 16, ["宓"] = 17,
            ["刘"] = 18, ["备"] = 19,
        };

        /// <summary>尝试获取拆字对应的贴图索引；未知拆字返回 false。</summary>
        internal static bool TryGetIndex(string partWord, out int index)
        {
            if (string.IsNullOrEmpty(partWord))
            {
                index = -1;
                return false;
            }

            return PartWordToIndex.TryGetValue(partWord, out index);
        }

        /// <summary>获取拆字对应的字形贴图 YooAsset 地址；未知拆字返回 null。</summary>
        internal static string GetSpriteAddress(string partWord)
        {
            return TryGetIndex(partWord, out int index)
                ? SpriteAddressPrefix + index
                : null;
        }
    }
}
