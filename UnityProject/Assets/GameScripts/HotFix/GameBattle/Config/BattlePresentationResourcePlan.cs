using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.4：BattlePresentationResourcePlan —— 表现预加载资源计划
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 7 / specs/configured-enemy-spawning/spec.md / task 5.4）：
    //   从本局所选 BattleConfigSnapshot 的 OrderedWavePlan 解析"实际会被使用"的普通
    //   敌人与 Boss 资源地址，作为 IBattleViewPort.PreloadAsync 的稳定输入。表现层只为
    //   这些去重地址加载 Prefab 并建立既有地址池，不再固定预加载 Mob0 或全目录。
    //
    // 收集规则：
    //   1. Normal 行经 EnemyCatalog 解析 resourceAddress；Boss 行经 BossCatalog 解析 resourcePath。
    //   2. Null view 纯逻辑运行可显式跳过 Boss 表现；生产空 Boss 地址明确失败。
    //   3. 按计划 order（首次出现顺序）去重，保证稳定与最小。
    //   4. 未知 enemyKey / 空地址显式失败（BattleConfigDataException），不静默 fallback；
    //      目录/计划缺失同为显式失败（快照已由 Validator 校验，此处为防御性契约）。
    //   5. 不读取旧数组字段（legacy Enemy/Wave 快照），不依赖映射行序。
    // ============================================================================

    /// <summary>
    /// 表现预加载资源计划：从所选有序波次计划解析去重的敌人/Boss 资源地址。
    /// </summary>
    /// <remarks>
    /// <para><b>用途（task 5.4）：</b>由 <see cref="BattleModule"/> 在现有
    /// <c>UniTask.WhenAll</c> 预加载事务中调用本类型收集地址，再传给
    /// <see cref="IBattleViewPort.PreloadAsync"/>。地址集合不可变、稳定去重且只包含
    /// 本局计划实际会生成的敌对实体。</para>
    ///
    /// <para><b>显式失败（spec "Reject an unsupported normal enemy"）：</b>
    /// 计划缺失、目录缺失、Normal 行 enemyKey 为空/未知、解析出的资源地址为空时，
    /// 抛出 <see cref="BattleConfigDataException"/>（结构化类别），禁止回退到目录首行
    /// 或固定 <c>Mob0</c>。</para>
    /// </remarks>
    internal static class BattlePresentationResourcePlan
    {
        /// <summary>
        /// 从本局所选配置快照收集敌人/Boss 资源地址（按计划首次出现顺序去重）。
        /// </summary>
        /// <param name="config">不可变本局配置快照（非 null）。</param>
        /// <returns>去重后的普通敌人资源地址只读列表（可能为空数组）。</returns>
        /// <exception cref="ArgumentNullException">config 为 null。</exception>
        /// <exception cref="BattleConfigDataException">目录/计划缺失、Normal 行 enemyKey 为空或
        /// 未知、资源地址为空（结构化显式失败）。</exception>
        internal static IReadOnlyList<string> CollectEnemyResourceAddresses(
            BattleConfigSnapshot config,
            bool requireBossPresentation = true)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.EnemyCatalog == null)
            {
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.MissingSection,
                    "无法构建表现预加载计划：敌人目录缺失",
                    "EnemyCatalog");
            }

            if (config.OrderedWavePlan == null || config.OrderedWavePlan.Rows == null)
            {
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.WavePlanMissing,
                    "无法构建表现预加载计划：有序波次计划缺失",
                    "OrderedWavePlan");
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (WavePlanEntry row in config.OrderedWavePlan.Rows)
            {
                if (row.Kind == WavePlanKind.Boss)
                {
                    if (!requireBossPresentation)
                    {
                        continue;
                    }

                    if (config.BossCatalog == null
                        || !config.BossCatalog.TryGetByKey(
                            row.BossKey, out BossDefinitionSnapshot bossDefinition))
                    {
                        throw new BattleConfigDataException(
                            BattleConfigErrorCategory.BossKeyUnknown,
                            $"order={row.Order} 的 bossKey='{row.BossKey}' 无法解析表现资源",
                            $"WavePlan.{row.Order}.BossKey");
                    }

                    string bossAddress = bossDefinition.ResourcePath;
                    if (string.IsNullOrEmpty(bossAddress))
                    {
                        throw new BattlePresentationLoadException(
                            "BossPresentationUnavailable",
                            $"<empty:{row.BossKey}>",
                            new InvalidOperationException(
                                $"Boss '{row.BossKey}' 的 Spine 4.3 Prefab 尚未就绪，resourcePath 为空"));
                    }

                    if (seen.Add(bossAddress))
                    {
                        result.Add(bossAddress);
                    }

                    continue;
                }

                if (row.Kind != WavePlanKind.Normal)
                {
                    continue;
                }

                string enemyKey = row.EnemyKey;
                if (string.IsNullOrEmpty(enemyKey))
                {
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.EnemyKeyUnknown,
                        $"order={row.Order} 的 Normal 行 enemyKey 为空，无法解析资源地址",
                        $"WavePlan.{row.Order}.EnemyKey");
                }

                if (!config.EnemyCatalog.TryGetByKey(enemyKey, out EnemyDefinitionSnapshot definition))
                {
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.EnemyKeyUnknown,
                        $"order={row.Order} 的 enemyKey='{enemyKey}' 不在敌人目录中，无法解析资源地址",
                        $"WavePlan.{row.Order}.EnemyKey");
                }

                string address = definition.ResourceAddress;
                if (string.IsNullOrEmpty(address))
                {
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.EnemyCatalogInvalid,
                        $"enemyKey='{enemyKey}' 的资源地址为空，无法预加载",
                        $"EnemyCatalog.{enemyKey}.ResourceAddress");
                }

                // 按计划首次出现顺序去重（address 是表现池唯一键）。
                if (seen.Add(address))
                {
                    result.Add(address);
                }
            }

            return result;
        }
    }
}
