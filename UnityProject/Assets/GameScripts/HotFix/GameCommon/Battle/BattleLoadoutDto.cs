using System;

namespace GameCommon.Battle
{
    /// <summary>
    /// 不可变战斗初始装载信息 DTO（跨程序集公共契约）。
    /// </summary>
    /// <remarks>
    /// 归属：GameCommon 是跨程序集公共契约的唯一归属（见 specs/battle-event-boundary：
    /// 跨程序集事实使用 GameCommon 不可变 DTO + TEngine GameEvent）。本 DTO 由调用方在
    /// 进入战斗前构造，传入 GameBattle 的 BattleModule.Start；GameBattle 不得回写本对象。
    ///
    /// 字段来源：
    /// - MapId/Round：还原工程 manifest #9 PlayerDataCore.js 的局外数据（mapIndex/round）
    ///   从本 DTO 输入，不把完整 PlayerData 带入战斗（12.0 逐项表 Boundary 处置）。
    /// - RandomSeed：注入确定性 SeededRandomSource（Ports/IRandomSource，spec 双时钟与
    ///   黄金轨迹要求可回放随机序列，1.3 导出为函数式常量随机源 0.5）。
    /// - ConfigVersion/ConfigHash：1.8 审计结论——配置版本/hash 机制完全缺失，属
    ///   task 3.2/3.5/8.2 须新建项。本 DTO 仅声明占位字段并使用明确零值/空串语义，
    ///   不在本任务（2.3）实现版本来源或 hash 算法。
    /// - DeckPreset：spec 6.5 要求均匀四兵最简牌组；本期未启用正式 108 牌池，
    ///   使用明确 "Normal" 语义（最简四兵），不得为 null 或隐式默认。
    ///
    /// 不可变性：本结构为 readonly struct，全部字段为 readonly，构造后不可修改；
    /// 字符串字段不使用 null，未启用字段使用明确零值或 "Normal" 语义。
    /// </remarks>
    public readonly struct BattleLoadoutDto
    {
        /// <summary>
        /// 本局地图标识（对应还原工程 PlayerDataCore.mapIndex）。本期最简模式固定为 0。
        /// </summary>
        public readonly int MapId;

        /// <summary>
        /// 局外回合信息（对应还原工程 PlayerDataCore.round）。本期未启用局外进度，固定为 0。
        /// </summary>
        public readonly int Round;

        /// <summary>
        /// 本局确定性随机种子，用于构造 SeededRandomSource。
        /// 0 表示使用框架默认种子序列（由 BattleRuntimeFactory 解释）。
        /// </summary>
        public readonly int RandomSeed;

        /// <summary>
        /// 配置版本占位字段。1.8 审计：版本/hash 机制完全缺失，由 task 3.2/3.5/8.2 新建。
        /// 本期未启用，使用明确零值 0，不得解释为“任意版本均可”。
        /// </summary>
        public readonly int ConfigVersion;

        /// <summary>
        /// 配置内容 hash 占位字段（十六进制字符串）。1.8 审计：机制缺失，由 task 3.2/3.5/8.2 新建。
        /// 本期未启用，使用明确空串 <see cref="string.Empty"/>，不得为 null。
        /// </summary>
        public readonly string ConfigHash;

        /// <summary>
        /// 牌组预设。本期只支持 <see cref="BattleDeckPreset.Normal"/>（均匀四兵最简牌组，spec 6.5）；
        /// 正式 108 牌池不在本期范围。不得为 null。
        /// </summary>
        public readonly BattleDeckPreset DeckPreset;

        /// <summary>本局对手控制模式。</summary>
        public readonly BattleOpponentMode OpponentMode;

        /// <summary>本地对手 AI 难度，只有 LocalAI 模式时生效。</summary>
        public readonly OpponentAiDifficulty OpponentAiDifficulty;

        /// <summary>
        /// 四槽武器装载（弓/枪/刀/剑）。未显式传入时规范化为
        /// <see cref="BattleWeaponLoadoutDto.CreateBasicDefault"/>；显式传入的部分零值
        /// 原样保留，不做静默修复。存在性/类型/enabled 校验由 GameBattle 负责。
        /// </summary>
        public readonly BattleWeaponLoadoutDto Weapons;

        /// <summary>
        /// 构造不可变装载信息。字符串参数为 null 时强制使用空串，枚举使用明确值。
        /// 最后一个可选参数 <paramref name="weapons"/> 仅在全零（default）时规范化为
        /// <see cref="BattleWeaponLoadoutDto.CreateBasicDefault"/>，部分零值原样保留。
        /// </summary>
        /// <param name="mapId">地图标识。</param>
        /// <param name="round">局外回合。</param>
        /// <param name="randomSeed">随机种子。</param>
        /// <param name="configVersion">配置版本占位（本期 0）。</param>
        /// <param name="configHash">配置 hash 占位（本期空串，null 被规范化为空串）。</param>
        /// <param name="deckPreset">牌组预设，默认 <see cref="BattleDeckPreset.Normal"/>。</param>
        /// <param name="opponentMode">对手控制模式。</param>
        /// <param name="opponentAiDifficulty">本地对手 AI 难度。</param>
        /// <param name="weapons">四槽武器装载，默认全零并规范化为 Basic 默认。</param>
        public BattleLoadoutDto(
            int mapId,
            int round,
            int randomSeed,
            int configVersion,
            string configHash,
            BattleDeckPreset deckPreset = BattleDeckPreset.Normal,
            BattleOpponentMode opponentMode = BattleOpponentMode.None,
            OpponentAiDifficulty opponentAiDifficulty = OpponentAiDifficulty.Easy,
            BattleWeaponLoadoutDto weapons = default)
        {
            MapId = mapId;
            Round = round;
            RandomSeed = randomSeed;
            ConfigVersion = configVersion;
            // 明确拒绝 null：未启用字段用空串而非 null，避免接收方判空歧义。
            ConfigHash = configHash ?? string.Empty;
            DeckPreset = deckPreset;
            OpponentMode = opponentMode;
            OpponentAiDifficulty = opponentAiDifficulty;
            // 仅全零（未显式指定）时规范化为 Basic 默认；部分零值不得静默修复。
            Weapons = weapons.IsEmpty
                ? BattleWeaponLoadoutDto.CreateBasicDefault()
                : weapons;
        }

        /// <summary>
        /// 本期最简默认装载：map0、round 0、seed 0、config 版本/hash 占位、Normal 牌组。
        /// 供黄金基线与最简闭环使用；生产入口仍须由调用方显式构造。
        /// </summary>
        public static BattleLoadoutDto CreateMinimalDefault()
            => new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: 0,
                configVersion: 0,
                configHash: string.Empty,
                deckPreset: BattleDeckPreset.Normal,
                weapons: BattleWeaponLoadoutDto.CreateBasicDefault());

        /// <summary>供当前单机入口使用的本地 AI 默认装载。</summary>
        public static BattleLoadoutDto CreateLocalAiDefault(
            OpponentAiDifficulty difficulty = OpponentAiDifficulty.Easy,
            BattleWeaponLoadoutDto weapons = default)
            => new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: 0,
                configVersion: 0,
                configHash: string.Empty,
                deckPreset: BattleDeckPreset.Normal,
                opponentMode: BattleOpponentMode.LocalAI,
                opponentAiDifficulty: difficulty,
                weapons: weapons.IsEmpty ? BattleWeaponLoadoutDto.CreateBasicDefault() : weapons);
    }

    /// <summary>
    /// 牌组预设枚举。本期仅 <see cref="Normal"/>；后续模式另行扩展，不静默复用 108 牌池。
    /// </summary>
    public enum BattleDeckPreset
    {
        /// <summary>
        /// 均匀四兵最简牌组（刀/弓/枪/骑），对应 spec 6.5 与 DeckDefinitions minimalMode。
        /// </summary>
        Normal = 0,
    }

    /// <summary>对手控制模式。当前只实现关闭和单机本地 AI。</summary>
    public enum BattleOpponentMode
    {
        None = 0,
        LocalAI = 1,
    }

    /// <summary>原工程钳制为 0..3 的四档对手 AI 难度。</summary>
    public enum OpponentAiDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Expert = 3,
    }
}
