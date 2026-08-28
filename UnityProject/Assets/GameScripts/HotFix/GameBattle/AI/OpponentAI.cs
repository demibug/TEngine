using System;
using System.Collections.Generic;

namespace GameBattle
{
    internal enum OpponentAiState
    {
        DeployReserve = 0,
        ScanBoard = 1,
        BuildPlan = 2,
        ExecutePlan = 3,
        RecruitOrIdle = 4,
    }

    /// <summary>
    /// 固定逻辑步驱动的本地对手 AI。只读取规则快照；征兵、部署与合并统一通过
    /// Recruit/DropUnit 命令执行，初始补给与波次收入通过 BattleEconomy 公共入口结算。
    /// </summary>
    internal sealed class OpponentAI
    {
        private readonly OpponentAiProfileSnapshot _profile;
        private readonly UnitSlotBoard _slotBoard;
        private readonly BattleInputController _inputController;
        private readonly BattleCommandIdAllocator _commandIdAllocator;
        private readonly BattleEconomy _economy;
        private readonly WaveManager _waveManager;
        private readonly OpponentAiPlacementPlanner _placementPlanner;
        private readonly UnitLevelService _levelService;
        private readonly List<int> _scannedUnitIds = new List<int>();

        private OpponentAiState _state = OpponentAiState.DeployReserve;
        private long _elapsedMs;
        private int _scanIndex;
        private int _plannedSourceUnitId = UnitSlot.InvalidUnitId;
        private int _plannedTargetUnitId = UnitSlot.InvalidUnitId;
        private bool _started;

        internal OpponentAiState State => _state;

        internal OpponentAI(
            OpponentAiProfileSnapshot profile,
            UnitSlotBoard slotBoard,
            BattleInputController inputController,
            BattleCommandIdAllocator commandIdAllocator,
            BattleEconomy economy,
            WaveManager waveManager,
            MapData map,
            IRandomSource strategyRandom,
            UnitLevelService levelService)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _slotBoard = slotBoard ?? throw new ArgumentNullException(nameof(slotBoard));
            _inputController = inputController ?? throw new ArgumentNullException(nameof(inputController));
            _commandIdAllocator = commandIdAllocator
                ?? throw new ArgumentNullException(nameof(commandIdAllocator));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _waveManager = waveManager ?? throw new ArgumentNullException(nameof(waveManager));
            _levelService = levelService ?? throw new ArgumentNullException(nameof(levelService));
            _placementPlanner = new OpponentAiPlacementPlanner(map, strategyRandom, levelService);
        }

        internal void StartGame()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _elapsedMs = 0;
            _state = OpponentAiState.DeployReserve;
            ResetPlanningState();
            _waveManager.WaveStarted += OnWaveStarted;
            _economy.Award(false, _profile.InitialBonusGold, "opponent-ai-initial");
        }

        internal void Stop()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _waveManager.WaveStarted -= OnWaveStarted;
            ResetPlanningState();
        }

        internal void Update(long stepMs)
        {
            if (!_started || stepMs <= 0)
            {
                return;
            }

            _elapsedMs += stepMs;
            if (_elapsedMs < _profile.DecisionIntervalMs)
            {
                return;
            }

            _elapsedMs = 0;
            AdvanceDecision();
        }

        private void AdvanceDecision()
        {
            switch (_state)
            {
                case OpponentAiState.DeployReserve:
                    DeployOneReserveUnit();
                    break;
                case OpponentAiState.ScanBoard:
                    ScanOneBoardSlot();
                    break;
                case OpponentAiState.BuildPlan:
                    BuildMergePlan();
                    _state = OpponentAiState.ExecutePlan;
                    break;
                case OpponentAiState.ExecutePlan:
                    ExecutePlan();
                    _state = OpponentAiState.RecruitOrIdle;
                    break;
                case OpponentAiState.RecruitOrIdle:
                    RecruitOrIdle();
                    break;
            }
        }

        private void DeployOneReserveUnit()
        {
            UnitSlotSnapshot snapshot = _slotBoard.Snapshot();
            IReadOnlyList<UnitSlot> reserves = snapshot.GetSlots(false, SlotZone.Reserve);
            UnitSlot source = default;
            bool foundSource = false;
            for (int i = 0; i < reserves.Count; i++)
            {
                if (!reserves[i].IsEmpty)
                {
                    source = reserves[i];
                    foundSource = true;
                    break;
                }
            }

            if (!foundSource)
            {
                BeginScan();
                return;
            }

            IReadOnlyList<UnitSlot> battleSlots = snapshot.GetSlots(false, SlotZone.Battle);
            BattleUnit sourceUnit = source.Occupant.Value;
            if (!_placementPlanner.TryChooseTarget(
                    sourceUnit, battleSlots, _profile, out UnitSlotId target))
            {
                BeginScan();
                return;
            }

            int commandId = _commandIdAllocator.Allocate();
            BattleInputCommand command = BattleInputCommand.CreateDropUnit(
                commandId, source.SlotId.Id, target.Id);
            BattleInputResult result = _inputController.Execute(command);
            if (!result.IsSuccess)
            {
                BeginScan();
            }
        }

        private void BeginScan()
        {
            _state = OpponentAiState.ScanBoard;
            _scanIndex = 0;
            _scannedUnitIds.Clear();
        }

        private void ScanOneBoardSlot()
        {
            IReadOnlyList<UnitSlot> slots = _slotBoard.Snapshot()
                .GetSlots(false, SlotZone.Battle);
            if (_scanIndex >= slots.Count)
            {
                _state = OpponentAiState.BuildPlan;
                return;
            }

            UnitSlot slot = slots[_scanIndex];
            _scanIndex++;
            if (slot.Occupant.HasValue && slot.Occupant.Value.Kind == UnitKind.Soldier)
            {
                _scannedUnitIds.Add(slot.OccupantUnitId);
            }
        }

        private void BuildMergePlan()
        {
            _plannedSourceUnitId = UnitSlot.InvalidUnitId;
            _plannedTargetUnitId = UnitSlot.InvalidUnitId;
            if (_profile.PlacementPolicy != OpponentAiPlacementPolicy.RouteAware)
            {
                return;
            }

            IReadOnlyList<UnitSlot> slots = _slotBoard.Snapshot()
                .GetSlots(false, SlotZone.Battle);

            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].Occupant.HasValue
                    || !_scannedUnitIds.Contains(slots[i].OccupantUnitId))
                {
                    continue;
                }

                BattleUnit source = slots[i].Occupant.Value;
                for (int j = i + 1; j < slots.Count; j++)
                {
                    if (!slots[j].Occupant.HasValue
                        || !_scannedUnitIds.Contains(slots[j].OccupantUnitId))
                    {
                        continue;
                    }

                    BattleUnit target = slots[j].Occupant.Value;
                    if (source.Kind == UnitKind.Soldier
                        && target.Kind == UnitKind.Soldier
                        && !_levelService.IsMaxLevel(source.Level)
                        && source.SoldierType == target.SoldierType
                        && source.Level == target.Level)
                    {
                        _plannedSourceUnitId = source.UnitId;
                        _plannedTargetUnitId = target.UnitId;
                        return;
                    }
                }
            }
        }

        private void ExecutePlan()
        {
            if (_plannedSourceUnitId == UnitSlot.InvalidUnitId
                || _plannedTargetUnitId == UnitSlot.InvalidUnitId)
            {
                return;
            }

            UnitSlotSnapshot snapshot = _slotBoard.Snapshot();
            if (!TryFindUnitSlot(snapshot, _plannedSourceUnitId, out UnitSlot source)
                || !TryFindUnitSlot(snapshot, _plannedTargetUnitId, out UnitSlot target))
            {
                return;
            }

            int commandId = _commandIdAllocator.Allocate();
            BattleInputCommand command = BattleInputCommand.CreateDropUnit(
                commandId, source.SlotId.Id, target.SlotId.Id);
            _inputController.Execute(command);
        }

        private void RecruitOrIdle()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.Snapshot()
                .GetSlots(false, SlotZone.Reserve);
            for (int i = 0; i < reserves.Count; i++)
            {
                if (!reserves[i].IsEmpty)
                {
                    _state = OpponentAiState.DeployReserve;
                    return;
                }
            }

            int cost = _economy.GetRefreshCost(false);
            if (!_economy.CanAfford(false, cost))
            {
                return;
            }

            int commandId = _commandIdAllocator.Allocate();
            BattleInputCommand command = BattleInputCommand.CreateRecruit(commandId, false);
            BattleInputResult result = _inputController.Execute(command);
            if (result.IsSuccess)
            {
                _state = OpponentAiState.DeployReserve;
            }
        }

        private void OnWaveStarted(int waveOrder)
        {
            if (!_started)
            {
                return;
            }

            int income = _profile.GetIncomeForWave(waveOrder);
            _economy.Award(false, income, "opponent-ai-wave");
        }

        private static bool TryFindUnitSlot(
            UnitSlotSnapshot snapshot,
            int unitId,
            out UnitSlot result)
        {
            IReadOnlyList<UnitSlot> slots = snapshot.GetAllSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].OccupantUnitId == unitId)
                {
                    result = slots[i];
                    return true;
                }
            }

            result = default;
            return false;
        }

        private void ResetPlanningState()
        {
            _scanIndex = 0;
            _scannedUnitIds.Clear();
            _plannedSourceUnitId = UnitSlot.InvalidUnitId;
            _plannedTargetUnitId = UnitSlot.InvalidUnitId;
        }
    }
}
