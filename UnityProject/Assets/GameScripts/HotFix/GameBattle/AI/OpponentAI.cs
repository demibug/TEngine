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
        private readonly OpponentAiDecisionEngine _decisionEngine;
        private readonly OpponentEconomyService _economyService;
        private readonly OpponentAiItemController _itemController;
        private readonly UnitLevelService _levelService;
        private readonly OpponentHand _opponentHand;
        private readonly OpponentAiReplayLog _replayLog;
        private readonly IRandomSource _strategyRandom;
        private readonly Queue<OpponentAiAction> _actionQueue = new Queue<OpponentAiAction>();
        private readonly List<int> _scannedUnitIds = new List<int>();

        private OpponentAiState _state = OpponentAiState.DeployReserve;
        private long _elapsedMs;
        private long _decisionTick;
        private int _scanIndex;
        private int _plannedSourceUnitId = UnitSlot.InvalidUnitId;
        private int _plannedTargetUnitId = UnitSlot.InvalidUnitId;
        private int _fastDeployUses;
        private int _dangerResponseUses;
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
            UnitLevelService levelService,
            OpponentHand opponentHand = null,
            GeneralCatalogSnapshot generalCatalog = null,
            OpponentAiReplayLog replayLog = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _slotBoard = slotBoard ?? throw new ArgumentNullException(nameof(slotBoard));
            _inputController = inputController ?? throw new ArgumentNullException(nameof(inputController));
            _commandIdAllocator = commandIdAllocator
                ?? throw new ArgumentNullException(nameof(commandIdAllocator));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _waveManager = waveManager ?? throw new ArgumentNullException(nameof(waveManager));
            _levelService = levelService ?? throw new ArgumentNullException(nameof(levelService));
            _strategyRandom = strategyRandom ?? throw new ArgumentNullException(nameof(strategyRandom));
            _opponentHand = opponentHand;
            _replayLog = replayLog;
            _placementPlanner = new OpponentAiPlacementPlanner(map, strategyRandom, levelService);
            _decisionEngine = new OpponentAiDecisionEngine(
                map,
                strategyRandom,
                levelService,
                generalCatalog);
            _economyService = new OpponentEconomyService(
                _profile,
                _economy,
                _inputController,
                _commandIdAllocator);
            _itemController = new OpponentAiItemController(
                _inputController.MapState,
                map);
        }

        internal void StartGame()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _elapsedMs = 0;
            _decisionTick = 0;
            _state = OpponentAiState.DeployReserve;
            _fastDeployUses = 0;
            _dangerResponseUses = 0;
            ResetPlanningState();
            _actionQueue.Clear();
            _replayLog?.Clear();
            _itemController.StartGame();
            _waveManager.WaveStarted += OnWaveStarted;
            _economyService.StartGame();
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
            _actionQueue.Clear();
            _itemController.Stop();
        }

        internal void Update(long stepMs)
        {
            if (!_started || stepMs <= 0)
            {
                return;
            }

            _itemController.Update(stepMs);
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
            _decisionTick++;
            // 事件触发动作优先于普通冷却策略；否则同一 tick 的道具检查可能
            // 覆盖已经排队的危险响应。
            if (_actionQueue.Count > 0)
            {
                _state = OpponentAiState.ExecutePlan;
                ExecuteNextAction();
                return;
            }

            if (TryPrioritizeReadyItem())
            {
                _state = OpponentAiState.ExecutePlan;
                ExecuteNextAction();
                return;
            }

            if (_actionQueue.Count == 0)
            {
                if (TryPrioritizeFastDeploy())
                {
                    _state = OpponentAiState.ExecutePlan;
                    ExecuteNextAction();
                    return;
                }

                BuildActionQueue();
            }

            if (_actionQueue.Count > 0)
            {
                _state = OpponentAiState.ExecutePlan;
                ExecuteNextAction();
                return;
            }

            _state = OpponentAiState.RecruitOrIdle;
            RecruitOrIdle();
        }

        /// <summary>
        /// raw TG/UG 触发：金币低于当前征兵成本且概率命中时，一次生成最多两个
        /// FastDeploy 动作。没有合法候选时也消费本次 guard，避免每 tick 重抽。
        /// </summary>
        private bool TryPrioritizeFastDeploy()
        {
            if (!_profile.AllowFastDeploy
                || _fastDeployUses >= _profile.FastDeployMaxUses
                || _economy.GetBalance(isPlayerSide: false)
                    >= _economyService.CurrentRefreshCost)
            {
                return false;
            }

            if (_strategyRandom.NextUnit() > _profile.FastDeployProbability)
            {
                return false;
            }

            _fastDeployUses++;
            OpponentAiBoardSnapshot snapshot = new OpponentAiBoardSnapshot(
                _slotBoard.Snapshot(),
                _placementPlanner.Map,
                _levelService.MaxLevel);
            IReadOnlyList<OpponentAiAction> actions =
                _decisionEngine.BuildFastDeployPlan(snapshot, _profile);
            if (actions.Count == 0)
            {
                return false;
            }

            _actionQueue.Clear();
            for (int i = 0; i < actions.Count; i++)
            {
                _actionQueue.Enqueue(actions[i]);
            }

            return true;
        }

        /// <summary>
        /// raw onPlayerDanger 外部入口。概率判定只在显式事件发生时执行。
        /// </summary>
        internal bool OnPlayerDanger()
        {
            if (!_started
                || !_profile.AllowDangerResponse
                || _dangerResponseUses >= _profile.DangerResponseMaxUses)
            {
                return false;
            }

            // 与原工程 Rq guard 等价：即使概率未命中，也不重复消费同一次
            // 危险提示事件，避免外部重复通知改变随机进度。
            _dangerResponseUses++;
            if (_strategyRandom.NextUnit() > _profile.DangerResponseProbability)
            {
                return false;
            }

            OpponentAiBoardSnapshot snapshot = new OpponentAiBoardSnapshot(
                _slotBoard.Snapshot(),
                _placementPlanner.Map,
                _levelService.MaxLevel);
            if (!_itemController.TryBuildDangerAction(
                    snapshot,
                    _profile,
                    out OpponentAiAction dangerAction))
            {
                return false;
            }

            _actionQueue.Clear();
            _actionQueue.Enqueue(dangerAction);
            _state = OpponentAiState.ExecutePlan;
            return true;
        }

        /// <summary>兼容危险提示系统使用的别名。</summary>
        internal bool NotifyPlayerDanger() => OnPlayerDanger();

        private bool TryPrioritizeReadyItem()
        {
            if (!_itemController.IsReady(_profile))
            {
                return false;
            }

            OpponentAiBoardSnapshot snapshot = new OpponentAiBoardSnapshot(
                _slotBoard.Snapshot(),
                _placementPlanner.Map,
                _levelService.MaxLevel);
            if (!_itemController.TryBuildAction(snapshot, _profile, out OpponentAiAction itemAction))
            {
                return false;
            }

            _actionQueue.Clear();
            _actionQueue.Enqueue(itemAction);
            return true;
        }

        private void BuildActionQueue()
        {
            OpponentAiBoardSnapshot snapshot = new OpponentAiBoardSnapshot(
                _slotBoard.Snapshot(),
                _placementPlanner.Map,
                _levelService.MaxLevel);
            if (_itemController.TryBuildAction(snapshot, _profile, out OpponentAiAction itemAction))
            {
                _actionQueue.Enqueue(itemAction);
                return;
            }

            IReadOnlyList<OpponentAiAction> actions =
                _decisionEngine.BuildPlan(snapshot, _profile);
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].Type != OpponentAiActionType.Wait)
                {
                    _actionQueue.Enqueue(actions[i]);
                }
            }
        }

        private void ExecuteNextAction()
        {
            if (_actionQueue.Count == 0)
            {
                return;
            }

            OpponentAiAction action = _actionQueue.Dequeue();
            if (action.Type == OpponentAiActionType.Recruit)
            {
                RecruitOrIdle();
                if (_state != OpponentAiState.DeployReserve)
                {
                    _actionQueue.Clear();
                }

                return;
            }

            if (action.Type == OpponentAiActionType.Replace)
            {
                if (!ValidateReclaimAction(action, out UnitSlot reclaimSource))
                {
                    _actionQueue.Clear();
                    _state = OpponentAiState.DeployReserve;
                    return;
                }

                int reclaimCommandId = _commandIdAllocator.Allocate();
                BattleInputResult reclaimResult = _inputController.Execute(
                    BattleInputCommand.CreateReclaimUnit(
                        reclaimCommandId,
                        reclaimSource.SlotId.Id,
                        action.ExpectedUnitId));
                if (!reclaimResult.IsSuccess)
                {
                    _replayLog?.Record(_decisionTick, action, reclaimResult);
                    _actionQueue.Clear();
                    _state = OpponentAiState.DeployReserve;
                    return;
                }

                _replayLog?.Record(_decisionTick, action, reclaimResult);
                _state = OpponentAiState.DeployReserve;
                return;
            }

            if (action.Type == OpponentAiActionType.UseShovel
                || action.Type == OpponentAiActionType.UseFarmer)
            {
                UnitSlot sourceItem = _slotBoard.GetSlotById(action.SourceSlotId);
                if (!sourceItem.Occupant.HasValue
                    || sourceItem.SlotId.Side
                    || sourceItem.SlotId.Zone != SlotZone.Reserve
                    || sourceItem.Occupant.Value.UnitId != action.ExpectedUnitId
                    || (action.Type == OpponentAiActionType.UseShovel
                        ? !sourceItem.Occupant.Value.IsShovel
                        : sourceItem.Occupant.Value.Kind != UnitKind.Farmer))
                {
                    _actionQueue.Clear();
                    return;
                }

                int itemCommandId = _commandIdAllocator.Allocate();
                BattleInputCommand itemCommand = action.Type == OpponentAiActionType.UseShovel
                    ? BattleInputCommand.CreateUseShovel(
                        itemCommandId,
                        sourceItem.SlotId.Id,
                        action.TargetPosition)
                    : BattleInputCommand.CreateUseFarmer(
                        itemCommandId,
                        sourceItem.SlotId.Id,
                        action.TargetPosition);
                BattleInputResult itemResult = _inputController.Execute(itemCommand);
                if (!itemResult.IsSuccess)
                {
                    _replayLog?.Record(_decisionTick, action, itemResult);
                    _actionQueue.Clear();
                    _state = OpponentAiState.DeployReserve;
                    return;
                }

                _replayLog?.Record(_decisionTick, action, itemResult);
                _opponentHand?.TryRemoveByUnitId(
                    sourceItem.Occupant.Value.UnitId,
                    out _);
                _itemController.MarkUsed();
                _state = OpponentAiState.DeployReserve;
                return;
            }

            if (action.Type != OpponentAiActionType.Deploy
                && action.Type != OpponentAiActionType.Merge
                && action.Type != OpponentAiActionType.FastDeploy
                && action.Type != OpponentAiActionType.SynthesizeGeneral)
            {
                _actionQueue.Clear();
                return;
            }

            UnitSlot source = _slotBoard.GetSlotById(action.SourceSlotId);
            UnitSlot target = _slotBoard.GetSlotById(action.TargetSlotId);
            if (!source.Occupant.HasValue
                || source.Occupant.Value.UnitId != action.ExpectedUnitId
                || source.SlotId.Side
                || target.SlotId.Side
                || target.SlotId.Zone != SlotZone.Battle)
            {
                _actionQueue.Clear();
                return;
            }

            if (!ValidateDropAction(action, source, target))
            {
                _actionQueue.Clear();
                _state = OpponentAiState.DeployReserve;
                return;
            }

            int commandId = _commandIdAllocator.Allocate();
            BattleInputCommand command = BattleInputCommand.CreateDropUnit(
                commandId,
                source.SlotId.Id,
                target.SlotId.Id);
            BattleInputResult result = _inputController.Execute(command);
            if (!result.IsSuccess)
            {
                _replayLog?.Record(_decisionTick, action, result);
                _actionQueue.Clear();
                _state = OpponentAiState.DeployReserve;
                return;
            }

            _replayLog?.Record(_decisionTick, action, result);
            if (source.SlotId.Zone == SlotZone.Reserve)
            {
                _opponentHand?.TryRemoveByUnitId(
                    source.Occupant.Value.UnitId,
                    out _);
            }

            if (_actionQueue.Count == 0)
            {
                _state = OpponentAiState.DeployReserve;
            }
        }

        private bool ValidateReclaimAction(
            OpponentAiAction action,
            out UnitSlot source)
        {
            source = default;
            if (!_profile.EnableReclaim
                || !_slotBoard.ContainsSlotId(action.SourceSlotId))
            {
                return false;
            }

            source = _slotBoard.GetSlotById(action.SourceSlotId);
            return source.SlotId.Zone == SlotZone.Battle
                && !source.SlotId.Side
                && source.Occupant.HasValue
                && source.Occupant.Value.UnitId == action.ExpectedUnitId
                && (source.Occupant.Value.Kind == UnitKind.Soldier
                    || source.Occupant.Value.Kind == UnitKind.General);
        }

        private bool ValidateDropAction(
            OpponentAiAction action,
            UnitSlot source,
            UnitSlot target)
        {
            if (action.Type == OpponentAiActionType.Deploy)
            {
                return true;
            }

            if (action.Type == OpponentAiActionType.FastDeploy
                && (source.SlotId.Zone != SlotZone.Reserve || !target.IsEmpty))
            {
                return false;
            }

            SlotDropResult plan = _slotBoard.TryPlanDrop(
                source.SlotId,
                target.SlotId);
            if (!plan.Success)
            {
                return false;
            }

            switch (action.Type)
            {
                case OpponentAiActionType.Merge:
                    return plan.IsMerge;
                case OpponentAiActionType.SynthesizeGeneral:
                    return plan.Success && plan.Plan.IsSynthesize;
                case OpponentAiActionType.FastDeploy:
                    return plan.Plan.OperationType == SlotDropOperationType.Move
                        || plan.Plan.OperationType == SlotDropOperationType.Synthesize;
                default:
                    return false;
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
            else
            {
                _opponentHand?.TryRemoveByUnitId(
                    source.Occupant.HasValue ? source.Occupant.Value.UnitId : UnitSlot.InvalidUnitId,
                    out _);
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

            if (!_economyService.CanRefresh())
            {
                return;
            }

            BattleInputResult result = _economyService.TryRefresh();
            if (result.IsSuccess)
            {
                _replayLog?.Record(
                    _decisionTick,
                    new OpponentAiAction(
                        OpponentAiActionType.Recruit,
                        reason: "refresh_hand"),
                    result);
                _state = OpponentAiState.DeployReserve;
            }
        }

        private void OnWaveStarted(int waveOrder)
        {
            if (!_started)
            {
                return;
            }

            _economyService.OnWaveStarted(waveOrder);
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
