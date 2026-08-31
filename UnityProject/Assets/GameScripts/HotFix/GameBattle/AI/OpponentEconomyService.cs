using System;

namespace GameBattle
{
    /// <summary>
    /// 对手经济策略门面。BattleEconomy 仍是金币和刷新费用的唯一写入者。
    /// </summary>
    internal sealed class OpponentEconomyService
    {
        private readonly OpponentAiProfileSnapshot _profile;
        private readonly BattleEconomy _economy;
        private readonly BattleInputController _inputController;
        private readonly BattleCommandIdAllocator _commandIdAllocator;
        private bool _initialBonusAwarded;

        internal OpponentEconomyService(
            OpponentAiProfileSnapshot profile,
            BattleEconomy economy,
            BattleInputController inputController,
            BattleCommandIdAllocator commandIdAllocator)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _inputController = inputController ?? throw new ArgumentNullException(nameof(inputController));
            _commandIdAllocator = commandIdAllocator
                ?? throw new ArgumentNullException(nameof(commandIdAllocator));
        }

        internal int RefreshBaseCost => Math.Max(0, _profile.RefreshBaseCost);
        internal int RefreshCostIncrement => Math.Max(0, _profile.RefreshCostIncrement);
        internal int ItemCooldownMs => Math.Max(0, _profile.ItemCooldownMs);
        internal bool AllowFastDeploy => _profile.AllowFastDeploy;

        /// <summary>
        /// 返回 BattleEconomy 当前费用；若旧兼容状态尚未初始化，则用 AI 配置预测值。
        /// </summary>
        internal int CurrentRefreshCost
        {
            get
            {
                int actual = _economy.GetRefreshCost(false);
                return actual >= 0 ? actual : ExpectedRefreshCost;
            }
        }

        internal int ExpectedRefreshCost
            => RefreshBaseCost
               + RefreshCostIncrement * _economy.GetRefreshCount(false);

        internal bool IsRefreshCostAligned
            => _economy.GetRefreshCost(false) == ExpectedRefreshCost;

        internal bool CanRefresh()
            => _economy.CanAfford(false, CurrentRefreshCost);

        internal BattleInputResult TryRefresh()
        {
            if (!CanRefresh())
            {
                return BattleInputResult.Fail(
                    commandId: -1,
                    rejectReason: BattleInputRejectReason.InsufficientGoldForRecruit,
                    diagnosticMessage: $"金币不足，无法支付对手刷新费用 {CurrentRefreshCost}");
            }

            int commandId = _commandIdAllocator.Allocate();
            return _inputController.Execute(
                BattleInputCommand.CreateRecruit(commandId, playerSide: false));
        }

        internal void StartGame()
        {
            if (_initialBonusAwarded)
            {
                return;
            }

            _initialBonusAwarded = true;
            _economy.Award(false, _profile.InitialBonusGold, "opponent-ai-initial");
        }

        internal void OnWaveStarted(int waveOrder)
        {
            int income = _profile.GetIncomeForWave(waveOrder);
            _economy.Award(false, income, "opponent-ai-wave");
        }
    }
}
