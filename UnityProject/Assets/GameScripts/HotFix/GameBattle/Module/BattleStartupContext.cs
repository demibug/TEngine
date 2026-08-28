using System;
using GameCommon.Battle;
using GameConfig;

namespace GameBattle
{
    /// <summary>
    /// 一次战斗启动的配置准备结果：Loadout 与已解析、已校验的不可变配置快照。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 决策 3）：</b>启动模块先调用 <see cref="Prepare"/>
    /// 的纯配置准备入口，只解析并校验一份与 Loadout 对应的快照；准备成功后才把
    /// <see cref="Config"/>.Map 交给 WorldHost，最后把同一 Context 交给
    /// <see cref="BattleRuntimeFactory"/> 创建 Runtime。这避免 Module 直接依赖 Luban
    /// Tables，也避免 WorldHost 和 RuntimeFactory 分别解析一次配置。</para>
    /// <para><b>生命周期（design.md 决策 3）：</b>Context 仅在 Loadout 完全匹配时复用
    /// （<see cref="Matches"/>）；退出、失败或显示新入口时由 BattleModule 清空。</para>
    /// <para><b>错误处理：</b>预期失败返回结构化 <see cref="ErrorCode"/> 而非异常
    /// （决策 0.7）。MapId 缺行返回 <see cref="BattleErrorCode.ConfigMissing"/>，
    /// 字段非法返回 <see cref="BattleErrorCode.ConfigInvalid"/>，均在世界加载前阻断。</para>
    /// </remarks>
    internal sealed class BattleStartupContext
    {
        /// <summary>是否准备成功（等价于 <see cref="ErrorCode"/> == None）。</summary>
        public bool IsValid => ErrorCode == BattleErrorCode.None;

        /// <summary>准备失败时的稳定错误码。成功时为 <see cref="BattleErrorCode.None"/>。</summary>
        public BattleErrorCode ErrorCode { get; }

        /// <summary>诊断信息（仅用于日志）。调用方 MUST NOT 解析此文本判断失败原因。</summary>
        public string DiagnosticMessage { get; }

        /// <summary>本局不可变装载信息。</summary>
        public BattleLoadoutDto Loadout { get; }

        /// <summary>已解析、已校验的不可变配置快照。仅成功时非 null。</summary>
        public BattleConfigSnapshot Config { get; }

        private BattleStartupContext(
            BattleErrorCode errorCode,
            string diagnosticMessage,
            BattleLoadoutDto loadout,
            BattleConfigSnapshot config)
        {
            ErrorCode = errorCode;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
            Loadout = loadout;
            Config = config;
        }

        /// <summary>
        /// 判断本 Context 是否与指定 Loadout 完全匹配（仅匹配时允许复用）。
        /// </summary>
        public bool Matches(BattleLoadoutDto loadout)
        {
            return Loadout.MapId == loadout.MapId
                   && Loadout.Round == loadout.Round
                   && Loadout.RandomSeed == loadout.RandomSeed
                   && Loadout.ConfigVersion == loadout.ConfigVersion
                   && string.Equals(Loadout.ConfigHash, loadout.ConfigHash, StringComparison.Ordinal)
                   && Loadout.DeckPreset == loadout.DeckPreset
                   && Loadout.OpponentMode == loadout.OpponentMode
                   && Loadout.OpponentAiDifficulty == loadout.OpponentAiDifficulty
                   && Loadout.Weapons.BowWeaponId == loadout.Weapons.BowWeaponId
                   && Loadout.Weapons.SpearWeaponId == loadout.Weapons.SpearWeaponId
                   && Loadout.Weapons.KnifeWeaponId == loadout.Weapons.KnifeWeaponId
                   && Loadout.Weapons.SwordWeaponId == loadout.Weapons.SwordWeaponId;
        }

        /// <summary>
        /// 纯配置准备入口：解析并校验一次 Loadout 对应的配置快照（默认运行时能力）。
        /// </summary>
        /// <param name="loadout">不可变战斗装载信息。</param>
        /// <returns>成功时携带已校验快照；失败时携带结构化错误码，不加载世界。</returns>
        /// <remarks>
        /// <para>等价于 <see cref="Prepare(BattleLoadoutDto, BattleRuntimeCapabilities)"/>
        /// 使用 <see cref="BattleRuntimeCapabilities.Production"/>（首期仅支持 ZhangLiang）。</para>
        /// </remarks>
        public static BattleStartupContext Prepare(BattleLoadoutDto loadout)
        {
            return Prepare(loadout, BattleRuntimeCapabilities.Production);
        }

        /// <summary>
        /// 纯配置准备入口：解析并校验一次 Loadout 对应的配置快照。
        /// </summary>
        /// <param name="loadout">不可变战斗装载信息。</param>
        /// <param name="capabilities">启动事务声明的运行时能力（Boss 波能力 gate 消费）。</param>
        /// <returns>成功时携带已校验快照；失败时携带结构化错误码，不加载世界。</returns>
        /// <remarks>
        /// <para><b>事务顺序（spec "Battle startup is transactional"）：</b>
        /// 先解析并校验配置（本方法），再准备世界、建立绑定、创建 Runtime。</para>
        /// <para><b>MapId 唯一选择（spec "MapId is the sole battle map selector"）：</b>
        /// 只使用 loadout.MapId 经 <see cref="LubanBattleConfigProvider.GetSnapshot(int)"/>
        /// 精确选择地图行，缺行返回 ConfigMissing，禁止回退 map0。</para>
        /// <para><b>能力 gate（spec "Reject Boss content when the capability is absent"）：</b>
        /// 所选计划含 Boss 行而 <paramref name="capabilities"/> 未声明支持时，在世界加载前
        /// 返回 <see cref="BattleErrorCode.ConfigInvalid"/>（类别 BossCapabilityUnsupported），
        /// 不静默跳过或降级。</para>
        /// </remarks>
        public static BattleStartupContext Prepare(
            BattleLoadoutDto loadout,
            BattleRuntimeCapabilities capabilities)
        {
            // 1. loadout 结构校验（牌组预设/配置版本，权威校验与 Factory 共用）。
            if (!BattleRuntimeFactory.TryValidateLoadout(
                    loadout,
                    out BattleErrorCode loadoutError,
                    out string loadoutMessage))
            {
                return Fail(loadoutError, loadoutMessage, loadout);
            }

            // 2. MapId 身份校验：负数在触及配置表前阻断。
            if (loadout.MapId < 0)
            {
                return Fail(
                    BattleErrorCode.ConfigInvalid,
                    $"装载信息 MapId={loadout.MapId} 为负，不能选择地图行",
                    loadout);
            }

            // 3. 访问应用级配置表（与 Factory 相同，应用级持有配置数据，决策 0.11）。
            Tables tables;
            try
            {
                tables = ConfigSystem.Instance.Tables;
            }
            catch (Exception ex)
            {
                return Fail(
                    BattleErrorCode.ConfigMissing,
                    $"访问配置表失败：{ex.GetType().Name}",
                    loadout);
            }

            if (tables == null)
            {
                return Fail(
                    BattleErrorCode.ConfigMissing,
                    "配置表 Tables 为 null，无法准备地图配置",
                    loadout);
            }

            // 4. 按 MapId 精确选择地图行并完整消费为不可变快照。
            BattleConfigSnapshot snapshot;
            try
            {
                var provider = new LubanBattleConfigProvider(tables);
                snapshot = provider.GetSnapshot(loadout.MapId);
            }
            catch (BattleMapConfigMissingException ex)
            {
                return Fail(
                    BattleErrorCode.ConfigMissing,
                    $"MapId={ex.MapId} 无对应地图行（battle_tbmap.bytes），禁止回退 map0",
                    loadout);
            }
            catch (BattleConfigDataException ex)
            {
                // 敌人目录/波次计划无法构建合法业务快照：转结构化校验结果（task 2.4/2.5）。
                return FailFromValidationError(ex, loadout);
            }
            catch (Exception ex)
            {
                return Fail(
                    BattleErrorCode.ConfigInvalid,
                    $"配置快照复制失败（MapId={loadout.MapId}）：{ex.GetType().Name}: {ex.Message}",
                    loadout);
            }

            // 5. 通用配置校验（结构 + 交叉引用 + 运行时能力三层，task 2.7/2.8）。
            BattleConfigValidationResult validationResult =
                BattleConfigValidator.Validate(snapshot, capabilities);
            if (!validationResult.IsValid)
            {
                return Fail(
                    validationResult.ErrorCode,
                    validationResult.DiagnosticMessage,
                    loadout);
            }

            // 6. 严格地图校验（身份与预期 MapId 完全相等/资源/路径连续性/双路入口/marker 域/enemyTypeIndex）。
            BattleConfigValidationResult mapResult =
                BattleConfigValidator.ValidateMapConfig(snapshot.Map, loadout.MapId);
            if (!mapResult.IsValid)
            {
                return Fail(
                    mapResult.ErrorCode,
                    mapResult.DiagnosticMessage,
                    loadout);
            }

            return Ok(loadout, snapshot);
        }

        /// <summary>
        /// 把 Provider 的结构化配置数据异常转换为校验结果并返回失败 Context。
        /// </summary>
        private static BattleStartupContext FailFromValidationError(
            BattleConfigDataException ex, BattleLoadoutDto loadout)
        {
            BattleConfigValidationResult result = new BattleConfigValidationResult(
                new[]
                {
                    new BattleConfigValidationError(ex.Category, ex.Message, ex.Path),
                });
            return Fail(result.ErrorCode, result.DiagnosticMessage, loadout);
        }

        /// <summary>构造成功 Context。</summary>
        internal static BattleStartupContext Ok(BattleLoadoutDto loadout, BattleConfigSnapshot config)
            => new BattleStartupContext(BattleErrorCode.None, string.Empty, loadout, config);

        /// <summary>构造失败 Context（不携带配置快照）。</summary>
        internal static BattleStartupContext Fail(
            BattleErrorCode errorCode,
            string diagnosticMessage,
            BattleLoadoutDto loadout)
            => new BattleStartupContext(errorCode, diagnosticMessage, loadout, config: null);
    }
}
