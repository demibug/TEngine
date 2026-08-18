using System;

namespace GameBattle
{
    /// <summary>
    /// 按 MapId 找不到对应地图行时的结构化配置缺失异常。
    /// </summary>
    /// <remarks>
    /// <para>由 <see cref="LubanBattleConfigProvider.GetSnapshot(int)"/> 在 MapId 对应行
    /// 不存在时抛出，启动配置准备将其转换为 <see cref="BattleErrorCode.ConfigMissing"/>。
    /// 禁止静默回退 map0（spec "MapId is the sole battle map selector"）。</para>
    /// </remarks>
    internal sealed class BattleMapConfigMissingException : Exception
    {
        /// <summary>缺失的 MapId。</summary>
        public int MapId { get; }

        public BattleMapConfigMissingException(int mapId)
            : base($"MapId={mapId} 无对应地图行；请检查 map.xlsx 与导出的 battle_tbmap.bytes")
        {
            MapId = mapId;
        }
    }
}
