using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameLogic;
using TEngine;
using UIBattle;
using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 战斗 HUD 业务窗口。
    /// </summary>
    /// <remarks>
    /// <para><b>最终方案：</b>固定创建待上场槽（不根据手牌长度动态重建），显示空槽、
    /// 单位、兵种、等级。Refresh 文案与操作改为"征兵"。支持待上场槽拖拽及弹回。</para>
    /// </remarks>
    internal sealed class BattleHudPanel : UI_BattleHudPanel
    {
        private const float CardWidth = 100f;
        private const float CardSpacing = 12f;
        private const float CardBottom = 132f;
        private const float CardHandleInset = 6f;
        private const float CardHandleSize = CardWidth - 12f;
        private const string FallbackBackgroundName = "fallbackBackground";
        private const string DragHandleName = "dragHandle";

        private static readonly Vector2 DragHandleHomePosition =
            new Vector2(CardHandleInset, CardHandleInset);

        private BattleHudEntryArgs _entryArgs;
        private readonly List<UI_BattleCardItem> _cards = new List<UI_BattleCardItem>();
        private bool _isExiting;
        private int _selectedHandSlot = -1;
        private BattleDragController _dragController;
        private GObject _dragShadow;
        private Vector2 _dragOriginPosition;
        private float _dragShadowPointerOffsetX;

        protected override void RegisterOpenEvents()
        {
            if (m_btnExit != null)
            {
                m_btnExit.onClick.Add(OnExitClicked);
            }

            if (m_btnRefresh != null)
            {
                m_btnRefresh.onClick.Add(OnRefreshClicked);
            }

            // 场上单位拖动：全局 Stage 捕获（阶段 2）。
            if (Stage.inst != null)
            {
                Stage.inst.onTouchBegin.AddCapture(OnStageTouchBegin);
                Stage.inst.onTouchMove.Add(OnStageTouchMove);
                Stage.inst.onTouchEnd.AddCapture(OnStageTouchEnd);
            }
        }

        protected override void ClearOpenEvents()
        {
            if (m_btnExit != null)
            {
                m_btnExit.onClick.Remove(OnExitClicked);
            }

            if (m_btnRefresh != null)
            {
                m_btnRefresh.onClick.Remove(OnRefreshClicked);
            }

            // 对称注销 Stage 捕获并取消拖拽（阶段 2）。
            if (Stage.inst != null)
            {
                Stage.inst.onTouchBegin.RemoveCapture(OnStageTouchBegin);
                Stage.inst.onTouchMove.Remove(OnStageTouchMove);
                Stage.inst.onTouchEnd.RemoveCapture(OnStageTouchEnd);
            }

            _dragController?.Cancel();
            DestroyDragShadow();

            ClearCards();
            _dragController = null;
            _entryArgs = null;
            _isExiting = false;
            _selectedHandSlot = -1;
        }

        protected override void OnRefresh()
        {
            _entryArgs = UserData as BattleHudEntryArgs;
            _isExiting = false;
            if (m_btnExit != null)
            {
                m_btnExit.touchable = _entryArgs != null;
                m_btnExit.grayed = _entryArgs == null;
            }

            if (_entryArgs == null)
            {
                Log.Error("[BattleHudPanel] 退出参数缺失，无法退出当前战斗。");
                ClearCards();
                return;
            }

            // 拖拽控制器：统一处理 Reserve 与 Battle 源，松手解析任意目标槽（修复 P0 四向拖拽）。
            _dragController = new BattleDragController(
                dropUnit: _entryArgs.DropUnit,
                resolveTargetSlot: ResolveTargetSlotForStage);

            RefreshCards();
        }

        /// <summary>
        /// 按槽位快照固定重建待上场槽（不根据手牌长度动态重建）。
        /// </summary>
        private void RefreshCards()
        {
            ClearCards();
            IReadOnlyList<UnitSlot> slots = _entryArgs?.GetPlayerReserveSlots?.Invoke();
            if (slots == null)
            {
                _selectedHandSlot = -1;
                return;
            }

            float totalWidth = slots.Count * CardWidth + (slots.Count - 1) * CardSpacing;
            float startX = (width - totalWidth) * 0.5f;
            for (int index = 0; index < slots.Count; index++)
            {
                int slotIndex = index;
                UnitSlot slot = slots[index];
                BattleUnit? occupant = slot.Occupant;
                UI_BattleCardItem card = UI_BattleCardItem.CreateInstance();
                card.SetSize(CardWidth, CardWidth);
                card.SetXY(startX + index * (CardWidth + CardSpacing), height - CardBottom - CardWidth);
                card.onClick.Add(context => OnCardClicked(context, slotIndex));
                SetCardVisual(
                    card,
                    occupant,
                    _entryArgs.GetUnitIcon,
                    _entryArgs.GetGeneralPartIcon,
                    _entryArgs.GetPropIcon);

                // 统一拖放规则：根卡不 draggable，起拖只允许发生在非空单位主体的拖拽柄上
                // （底框/空槽/等级数字不能起拖）。空槽无拖拽柄，无拖拽。
                if (card.GetChild(DragHandleName) is GObject dragHandle)
                {
                    dragHandle.onDragStart.Add(context => OnCardDragStart(context, slotIndex));
                    dragHandle.onDragMove.Add(context => OnCardDragMove(context, slotIndex));
                    dragHandle.onDragEnd.Add(context => OnCardDragEnd(context, slotIndex));
                }

                AddChild(card);
                _cards.Add(card);
            }

            if (_selectedHandSlot < 0 || _selectedHandSlot >= _cards.Count)
            {
                _selectedHandSlot = 0;
            }
            ApplyCardSelection();
        }

        private void ClearCards()
        {
            for (int index = _cards.Count - 1; index >= 0; index--)
            {
                UI_BattleCardItem card = _cards[index];
                if (card != null && card.parent == this)
                {
                    RemoveChild(card, dispose: true);
                }
            }
            _cards.Clear();
        }

        /// <summary>
        /// 使用士兵 Prefab 的默认立绘设置卡牌图标；资源不可用或空槽时才回退为兵种文字。
        /// 有单位时在卡面叠加等级数字（最终方案"待上场槽显示等级"）。
        /// 非空卡直接以单位主体作为不覆盖底框的拖拽柄，使起拖与可见对象保持一致。
        /// </summary>
        private static void SetCardVisual(
            UI_BattleCardItem card,
            BattleUnit? unit,
            Func<int, Sprite> getUnitIcon,
            Func<string, Sprite> getGeneralPartIcon,
            Func<PropType, Sprite> getPropIcon)
        {
            bool hasUnit = unit.HasValue;
            string displayText = !unit.HasValue ? null
                : unit.Value.Kind == UnitKind.GeneralPart ? unit.Value.GeneralPartText
                : unit.Value.Kind == UnitKind.General ? unit.Value.GeneralPartText
                : unit.Value.SoldierText;
            string soldierText = unit.HasValue && unit.Value.Kind == UnitKind.Soldier
                ? unit.Value.SoldierText
                : string.Empty;
            int level = unit.HasValue ? unit.Value.Level : 0;
            var background = new GGraph
            {
                name = FallbackBackgroundName,
                touchable = false,
            };
            background.DrawRect(
                CardWidth,
                CardWidth,
                2,
                new Color(0.12f, 0.08f, 0.04f, 1f),
                new Color(0.90f, 0.78f, 0.55f, 0.96f));
            card.AddChildAt(background, 0);

            int soldierType = soldierText == "刀" ? 0
                : soldierText == "弓" ? 1
                : soldierText == "枪" ? 2
                : soldierText == "骑" ? 3
                : -1;
            Sprite sprite = soldierType >= 0 ? getUnitIcon?.Invoke(soldierType) : null;
            if (sprite == null && unit.HasValue
                && (unit.Value.Kind == UnitKind.GeneralPart || unit.Value.Kind == UnitKind.General))
            {
                sprite = getGeneralPartIcon?.Invoke(unit.Value.GeneralPartText);
            }
            if (sprite == null && unit.HasValue && unit.Value.Kind == UnitKind.Prop)
            {
                sprite = getPropIcon?.Invoke(unit.Value.PropType);
            }

            if (sprite != null)
            {
                var icon = new GLoader
                {
                    name = DragHandleName,
                    touchable = true,
                    draggable = true,
                    fill = FillType.Scale,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle,
                    texture = new NTexture(sprite),
                };
                icon.SetSize(CardWidth - 12f, CardWidth - 12f);
                icon.SetXY(6f, 6f);
                card.AddChild(icon);
                if (unit.HasValue && unit.Value.Kind == UnitKind.General)
                {
                    AddIdentityBadge(card, unit.Value.GeneralName);
                }
                if (!unit.HasValue || unit.Value.Kind != UnitKind.Prop)
                {
                    AddLevelBadge(card, level);
                }
                return;
            }

            var label = new GTextField
            {
                name = hasUnit ? DragHandleName : "fallbackLabel",
                autoSize = AutoSizeType.None,
                align = AlignType.Center,
                verticalAlign = VertAlignType.Middle,
                touchable = hasUnit,
                draggable = hasUnit,
            };
            TextFormat format = label.textFormat;
            format.size = 38;
            format.bold = true;
            format.color = new Color(0.25f, 0.08f, 0.03f, 1f);
            label.textFormat = format;
            label.SetSize(CardHandleSize, CardHandleSize);
            label.SetXY(CardHandleInset, CardHandleInset);
            label.text = string.IsNullOrEmpty(displayText) ? "空" : displayText;
            card.AddChild(label);
            if (unit.HasValue && unit.Value.Kind == UnitKind.General)
            {
                AddIdentityBadge(card, unit.Value.GeneralName);
            }
            if (!unit.HasValue || unit.Value.Kind != UnitKind.Prop)
            {
                AddLevelBadge(card, level);
            }
        }

        /// <summary>在待上场卡右下角叠加等级数字（空槽不显示）。</summary>
        private static void AddLevelBadge(UI_BattleCardItem card, int level)
        {
            if (level <= 0)
            {
                return;
            }

            var badge = new GTextField
            {
                name = "levelBadge",
                autoSize = AutoSizeType.None,
                align = AlignType.Center,
                verticalAlign = VertAlignType.Middle,
                touchable = true,
            };
            TextFormat format = badge.textFormat;
            format.size = 26;
            format.bold = true;
            format.color = new Color(1f, 0.85f, 0.2f, 1f);
            badge.textFormat = format;
            badge.SetSize(40f, 32f);
            badge.SetXY(CardWidth - 48f, CardWidth - 40f);
            badge.text = "Lv" + level;
            card.AddChild(badge);
        }

        private static void AddIdentityBadge(UI_BattleCardItem card, string text)
        {
            var badge = new GTextField
            {
                name = "identityBadge",
                autoSize = AutoSizeType.None,
                align = AlignType.Center,
                verticalAlign = VertAlignType.Middle,
                touchable = false,
                text = string.IsNullOrEmpty(text) ? "武将" : text,
            };
            TextFormat format = badge.textFormat;
            format.size = 18;
            format.bold = true;
            format.color = Color.white;
            badge.textFormat = format;
            badge.SetSize(CardWidth - 8f, 24f);
            badge.SetXY(4f, 4f);
            card.AddChild(badge);
        }

        private void OnCardClicked(EventContext context, int handSlotIndex)
        {
            context.StopPropagation();
            _selectedHandSlot = handSlotIndex;
            ApplyCardSelection();
        }

        private void OnCardDragStart(EventContext context, int reserveSlotIndex)
        {
            context.StopPropagation();
            _selectedHandSlot = reserveSlotIndex;
            ApplyCardSelection();

            // 经统一拖拽控制器开始拖拽（Reserve 卡源；战场单位源由世界表现层接入）。
            IReadOnlyList<UnitSlot> slots = _entryArgs?.GetPlayerReserveSlots?.Invoke();
            if (slots != null && reserveSlotIndex >= 0 && reserveSlotIndex < slots.Count)
            {
                BattleUnit? occupant = slots[reserveSlotIndex].Occupant;
                int touchId = context.inputEvent?.touchId ?? -1;
                _dragController?.BeginDrag(slots[reserveSlotIndex].SlotId.Id, touchId);
                if (occupant.HasValue && occupant.Value.Kind == UnitKind.General && Stage.inst != null)
                {
                    Vector2 stagePosition = context.inputEvent != null
                        ? context.inputEvent.position
                        : Stage.inst.GetTouchPosition(touchId);
                    CreateDragShadow(occupant.Value, stagePosition);
                }
            }
        }

        /// <summary>Reserve General 使用单格影子拖动，原卡柄固定在槽位原点。</summary>
        private void OnCardDragMove(EventContext context, int reserveSlotIndex)
        {
            IReadOnlyList<UnitSlot> slots = _entryArgs?.GetPlayerReserveSlots?.Invoke();
            if (slots == null || reserveSlotIndex < 0 || reserveSlotIndex >= slots.Count
                || !slots[reserveSlotIndex].Occupant.HasValue
                || slots[reserveSlotIndex].Occupant.Value.Kind != UnitKind.General)
            {
                return;
            }

            if (reserveSlotIndex < _cards.Count
                && _cards[reserveSlotIndex]?.GetChild(DragHandleName) is GObject handle)
            {
                handle.xy = DragHandleHomePosition;
            }

            if (_dragShadow != null && context.inputEvent != null)
            {
                MoveDragShadow(context.inputEvent.position);
            }
        }

        private void OnCardDragEnd(EventContext context, int reserveSlotIndex)
        {
            context.StopPropagation();

            IReadOnlyList<UnitSlot> slots = _entryArgs?.GetPlayerReserveSlots?.Invoke();
            bool isGeneral = slots != null
                && reserveSlotIndex >= 0
                && reserveSlotIndex < slots.Count
                && slots[reserveSlotIndex].Occupant.HasValue
                && slots[reserveSlotIndex].Occupant.Value.Kind == UnitKind.General;
            bool isShovel = slots != null
                && reserveSlotIndex >= 0
                && reserveSlotIndex < slots.Count
                && slots[reserveSlotIndex].Occupant.HasValue
                && slots[reserveSlotIndex].Occupant.Value.IsShovel;
            int sourceSlotId = slots != null && reserveSlotIndex >= 0 && reserveSlotIndex < slots.Count
                ? slots[reserveSlotIndex].SlotId.Id
                : -1;

            // 复位拖拽柄回到单位主体原位（未命中目标时表现弹回源槽；命中提交由刷新重建卡面）。
            if (reserveSlotIndex >= 0 && reserveSlotIndex < _cards.Count && _cards[reserveSlotIndex] != null)
            {
                if (_cards[reserveSlotIndex].GetChild(DragHandleName) is GObject handle)
                {
                    handle.xy = DragHandleHomePosition;
                }
            }

            if (_entryArgs == null || Stage.inst == null)
            {
                _dragController?.Cancel();
                if (isGeneral)
                {
                    AnimateDragShadowBack();
                }
                return;
            }

            int touchId = context.inputEvent?.touchId ?? -1;
            Vector2 stagePosition = Stage.inst.GetTouchPosition(touchId);
            if (isShovel)
            {
                _dragController?.Cancel();
                BattleInputResult shovelResult = _entryArgs.UseShovel(
                    sourceSlotId,
                    stagePosition.x,
                    stagePosition.y);
                TryDropUnit(shovelResult);
                return;
            }

            BattleInputResult? result = _dragController?.EndDrag(stagePosition.x, stagePosition.y, touchId);
            if (isGeneral)
            {
                if (result.HasValue && result.Value.IsSuccess)
                {
                    DestroyDragShadow();
                }
                else
                {
                    AnimateDragShadowBack();
                }
            }
            if (result.HasValue)
            {
                TryDropUnit(result.Value);
            }
        }

        // ====================================================================
        // Stage 捕获：场上单位拖动源（阶段 2）
        // --------------------------------------------------------------------
        // 场上单位没有单独输入绑定，改用 FairyGUI Stage 全局捕获：
        //   - onTouchBegin：识别场上源槽 → BeginDrag；创建 UI 拖动影子。
        //   - onTouchMove：只移动影子，不修改规则状态。
        //   - onTouchEnd：统一结束拖动（touchId 校验），提交成功销毁影子，失败弹回。
        // ====================================================================

        private void OnStageTouchBegin(EventContext context)
        {
            if (_dragController == null || _entryArgs == null || context.inputEvent == null)
            {
                return;
            }

            int touchId = context.inputEvent.touchId;
            Vector2 stagePosition = context.inputEvent.position;

            // 排除 HUD 可交互控件：征兵按钮、退出按钮、待上场卡片。
            if (IsOverHudControl(stagePosition))
            {
                return;
            }

            // 识别场上源槽：从外层战场槽位框架起拖，不依赖单位内部动画 Renderer。
            // 先锁定玩家战场槽，再检查该槽是否有单位。
            if (_entryArgs.ResolveBattleSourceForStage?.Invoke(
                    stagePosition.x, stagePosition.y, out int battleSlotId) != true
                || battleSlotId < 0)
            {
                return;
            }

            UnitSlot slot = _entryArgs.GetSlotById(battleSlotId);
            if (slot.SlotId.Zone != SlotZone.Battle
                || !slot.SlotId.Side
                || slot.IsEmpty)
            {
                return;
            }

            // 开始拖动并创建纯 UI 拖动影子（真实战斗单位不移动）。
            _dragController.BeginDrag(slot.SlotId.Id, touchId);
            CreateDragShadow(slot.Occupant.Value, stagePosition);
        }

        private void OnStageTouchMove(EventContext context)
        {
            if (_dragController == null || !_dragController.IsDragging
                || context.inputEvent == null || _dragShadow == null)
            {
                return;
            }

            // P1 修复：错误 touchId 直接返回，不移动影子（多指保护与控制器状态一致）。
            if (_dragController.TouchId != context.inputEvent.touchId)
            {
                return;
            }

            // 只移动影子，不修改规则状态。影子存 HUD 本地坐标（P2 修复）。
            MoveDragShadow(context.inputEvent.position);
        }

        private void OnStageTouchEnd(EventContext context)
        {
            if (_dragController == null || !_dragController.IsDragging || context.inputEvent == null)
            {
                return;
            }

            // P1 修复：错误 touchId 直接返回，不销毁影子、不弹回。
            int touchId = context.inputEvent.touchId;
            if (_dragController.TouchId != touchId)
            {
                return;
            }

            // P0 修复：Stage End 只结算 Battle 来源。
            // 待上场卡（Reserve 源）的拖动由 OnCardDragEnd 独占结算，
            // 避免 Stage 捕获提前消费导致 sourceSlotId == targetSlotId 而不提交。
            UnitSlot source = _entryArgs.GetSlotById(_dragController.SourceSlotId);
            if (source.SlotId.Zone != SlotZone.Battle)
            {
                return;
            }

            Vector2 stagePosition = context.inputEvent.position;
            BattleInputResult? result = _dragController.EndDrag(
                stagePosition.x, stagePosition.y, touchId);

            // P1 修复：区分成功、拒绝、未命中三种结束结果。
            if (!result.HasValue)
            {
                // 未命中目标：影子弹回原点后销毁。
                AnimateDragShadowBack();
                return;
            }

            if (result.Value.IsSuccess)
            {
                // 提交成功：直接销毁影子，槽位变化由现有事件驱动。
                DestroyDragShadow();
            }
            else
            {
                // 业务拒绝（兵种/等级/满级/跨阵营等）：影子弹回原点后销毁。
                AnimateDragShadowBack();
            }

            TryDropUnit(result.Value);
        }

        /// <summary>是否命中 HUD 可交互控件（征兵/退出按钮、待上场卡片）。</summary>
        private bool IsOverHudControl(Vector2 stagePosition)
        {
            if (ContainsStagePoint(m_btnExit, stagePosition)
                || ContainsStagePoint(m_btnRefresh, stagePosition))
            {
                return true;
            }

            for (int index = 0; index < _cards.Count; index++)
            {
                if (ContainsStagePoint(_cards[index], stagePosition))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>创建纯 UI 拖动影子（复用卡片图标生成方式；武将也按点击格显示单字）。</summary>
        private void CreateDragShadow(BattleUnit unit, Vector2 stagePosition)
        {
            if (_dragShadow != null)
            {
                DestroyDragShadow();
            }

            int soldierType = unit.Kind != UnitKind.Soldier ? -1
                : unit.SoldierText == "刀" ? 0
                : unit.SoldierText == "弓" ? 1
                : unit.SoldierText == "枪" ? 2
                : unit.SoldierText == "骑" ? 3
                : -1;
            Sprite sprite = soldierType >= 0 ? _entryArgs?.GetUnitIcon?.Invoke(soldierType) : null;
            if (sprite == null
                && (unit.Kind == UnitKind.GeneralPart || unit.Kind == UnitKind.General))
            {
                sprite = _entryArgs?.GetGeneralPartIcon?.Invoke(unit.GeneralPartText);
            }

            GObject shadow;
            if (sprite != null)
            {
                var loader = new GLoader
                {
                    touchable = false,
                    fill = FillType.Scale,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle,
                    texture = new NTexture(sprite),
                };
                loader.SetSize(CardWidth - 12f, CardWidth - 12f);
                shadow = loader;
            }
            else
            {
                var label = new GTextField
                {
                    touchable = false,
                    autoSize = AutoSizeType.None,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle,
                };
                TextFormat format = label.textFormat;
                format.size = 38;
                format.bold = true;
                format.color = Color.white;
                label.textFormat = format;
                label.text = unit.Kind == UnitKind.GeneralPart || unit.Kind == UnitKind.General
                    ? unit.GeneralPartText
                    : unit.SoldierText;
                shadow = label;
            }

            shadow.SetSize(CardWidth - 12f, CardWidth - 12f);
            PositionDragShadow(shadow, stagePosition);
        }

        private void PositionDragShadow(
            GObject shadow,
            Vector2 stagePosition)
        {
            _dragShadowPointerOffsetX = shadow.width * 0.5f;
            Vector2 localPosition = GlobalToLocal(stagePosition);
            shadow.xy = new Vector2(
                localPosition.x - _dragShadowPointerOffsetX,
                localPosition.y - shadow.height * 0.5f);
            _dragOriginPosition = shadow.xy;
            AddChild(shadow);
            _dragShadow = shadow;
        }

        private void MoveDragShadow(Vector2 stagePosition)
        {
            if (_dragShadow == null)
            {
                return;
            }

            Vector2 localPosition = GlobalToLocal(stagePosition);
            _dragShadow.xy = new Vector2(
                localPosition.x - _dragShadowPointerOffsetX,
                localPosition.y - _dragShadow.height * 0.5f);
        }

        /// <summary>影子弹回原点后销毁（GTween，无 Coroutine）。</summary>
        private void AnimateDragShadowBack()
        {
            if (_dragShadow == null)
            {
                return;
            }

            GObject shadow = _dragShadow;
            Vector2 origin = _dragOriginPosition;
            _dragShadow = null;
            shadow.TweenMove(origin, 0.2f).OnComplete(() =>
            {
                if (shadow != null && shadow.parent == this)
                {
                    RemoveChild(shadow, dispose: true);
                }
            });
        }

        /// <summary>立即销毁拖动影子。</summary>
        private void DestroyDragShadow()
        {
            if (_dragShadow == null)
            {
                return;
            }

            GObject shadow = _dragShadow;
            _dragShadow = null;
            if (shadow.parent == this)
            {
                RemoveChild(shadow, dispose: true);
            }
        }

        /// <summary>
        /// 把舞台坐标解析为任意目标槽位 ID（修复 P0 四向拖拽）。
        /// 先命中 Reserve 卡（Reserve→Reserve），再尝试战场槽（Battle→Battle / Reserve→Battle）；
        /// 未命中返回 -1（弹回）。
        /// </summary>
        private int ResolveTargetSlotForStage(float stageX, float stageY)
        {
            var stagePosition = new Vector2(stageX, stageY);

            // 1. Reserve 卡命中（Reserve→Reserve / Battle→Reserve）。
            IReadOnlyList<UnitSlot> reserves = _entryArgs?.GetPlayerReserveSlots?.Invoke();
            if (reserves != null)
            {
                for (int i = 0; i < reserves.Count; i++)
                {
                    if (i < _cards.Count && _cards[i] != null
                        && ContainsStagePoint(_cards[i], stagePosition))
                    {
                        return reserves[i].SlotId.Id;
                    }
                }
            }

            // 2. 战场槽命中（Reserve→Battle / Battle→Battle）。
            int battleSlotId = -1;
            if (_entryArgs?.ResolveBattleSlotForStage?.Invoke(stageX, stageY, out battleSlotId) == true
                && battleSlotId >= 0)
            {
                return battleSlotId;
            }

            return -1;
        }

        private void ApplyCardSelection()
        {
            for (int index = 0; index < _cards.Count; index++)
            {
                UI_BattleCardItem card = _cards[index];
                bool selected = index == _selectedHandSlot;
                if (card?.m_cState != null)
                {
                    card.m_cState.selectedPage = selected ? "Selected" : "Normal";
                }

                if (card?.GetChild(FallbackBackgroundName) is GGraph background)
                {
                    background.DrawRect(
                        CardWidth,
                        CardWidth,
                        selected ? 4 : 2,
                        selected
                            ? new Color(0.10f, 0.70f, 0.95f, 1f)
                            : new Color(0.12f, 0.08f, 0.04f, 1f),
                        selected
                            ? new Color(1f, 0.88f, 0.48f, 1f)
                            : new Color(0.90f, 0.78f, 0.55f, 0.96f));
                }
            }
        }

        /// <summary>处理拖放类命令结果并刷新槽位表现。</summary>
        private void TryDropUnit(BattleInputResult result)
        {
            if (result.IsSuccess)
            {
                RefreshCards();
            }
            else
            {
                Log.Info(
                    $"[BattleDiagnostic] 拖放结果 rejected={result.RejectReason} " +
                    $"message={result.DiagnosticMessage}");
            }
        }

        private static bool ContainsStagePoint(GObject control, Vector2 stagePosition)
        {
            if (control == null)
            {
                return false;
            }

            Vector2 localPosition = control.GlobalToLocal(stagePosition);
            return localPosition.x >= 0f && localPosition.x <= control.width
                && localPosition.y >= 0f && localPosition.y <= control.height;
        }

        private void OnRefreshClicked()
        {
            if (_entryArgs == null)
            {
                return;
            }

            // P1 修复：征兵前强制取消进行中的拖动并销毁影子，
            // 避免多指下旧拖动对新单位提交旧槽位。
            _dragController?.Cancel();
            DestroyDragShadow();

            BattleInputResult result = _entryArgs.Recruit();
            if (result.IsSuccess)
            {
                RefreshCards();
            }
            else
            {
                Log.Debug($"[BattleHudPanel] 征兵被拒绝: {result.RejectReason}");
            }
        }

        private void OnExitClicked()
        {
            if (_isExiting || _entryArgs == null)
            {
                return;
            }

            ExitBattleAsync().Forget();
        }

        private async UniTaskVoid ExitBattleAsync()
        {
            BattleHudEntryArgs entryArgs = _entryArgs;
            if (entryArgs == null || _isExiting)
            {
                return;
            }

            _isExiting = true;
            if (m_btnExit != null)
            {
                m_btnExit.touchable = false;
                m_btnExit.grayed = true;
            }

            try
            {
                BattleOperationResult result = await entryArgs.ExitAsync();
                if (!ReferenceEquals(_entryArgs, entryArgs))
                {
                    return;
                }

                if (!result.IsSuccess)
                {
                    Log.Warning($"[BattleHudPanel] 退出战斗失败: {result}");
                    RestoreExitButton(entryArgs);
                }
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(_entryArgs, entryArgs))
                {
                    return;
                }

                Log.Error($"[BattleHudPanel] 退出战斗发生异常: {ex}");
                RestoreExitButton(entryArgs);
            }
        }

        private void RestoreExitButton(BattleHudEntryArgs entryArgs)
        {
            if (!ReferenceEquals(_entryArgs, entryArgs))
            {
                return;
            }

            _isExiting = false;
            if (m_btnExit != null)
            {
                m_btnExit.touchable = true;
                m_btnExit.grayed = false;
            }
        }
    }

    /// <summary>
    /// 战斗 HUD 单次打开参数（最终方案：征兵 + 换槽/合并 + 槽位快照 + 场上拖动）。
    /// </summary>
    internal sealed class BattleHudEntryArgs
    {
        internal Func<UniTask<BattleOperationResult>> ExitAsync { get; }
        internal Func<BattleInputResult> Recruit { get; }
        internal Func<int, int, BattleInputResult> DropUnit { get; }
        internal Func<int, float, float, BattleInputResult> UseShovel { get; }
        internal Func<IReadOnlyList<UnitSlot>> GetPlayerReserveSlots { get; }
        internal ResolveBattleSlotDelegate ResolveBattleSlotForStage { get; }
        internal ResolveBattleSourceDelegate ResolveBattleSourceForStage { get; }
        internal Func<int, UnitSlot> GetSlotById { get; }
        internal Func<int, Sprite> GetUnitIcon { get; }
        internal Func<string, Sprite> GetGeneralPartIcon { get; }
        internal Func<PropType, Sprite> GetPropIcon { get; }

        internal BattleHudEntryArgs(
            Func<UniTask<BattleOperationResult>> exitAsync,
            Func<BattleInputResult> recruit,
            Func<int, int, BattleInputResult> dropUnit,
            Func<int, float, float, BattleInputResult> useShovel,
            Func<IReadOnlyList<UnitSlot>> getPlayerReserveSlots,
            ResolveBattleSlotDelegate resolveBattleSlotForStage,
            ResolveBattleSourceDelegate resolveBattleSourceForStage,
            Func<int, UnitSlot> getSlotById,
            Func<int, Sprite> getUnitIcon,
            Func<string, Sprite> getGeneralPartIcon,
            Func<PropType, Sprite> getPropIcon)
        {
            ExitAsync = exitAsync ?? throw new ArgumentNullException(nameof(exitAsync));
            Recruit = recruit ?? throw new ArgumentNullException(nameof(recruit));
            DropUnit = dropUnit ?? throw new ArgumentNullException(nameof(dropUnit));
            UseShovel = useShovel ?? throw new ArgumentNullException(nameof(useShovel));
            GetPlayerReserveSlots = getPlayerReserveSlots
                ?? throw new ArgumentNullException(nameof(getPlayerReserveSlots));
            ResolveBattleSlotForStage = resolveBattleSlotForStage
                ?? throw new ArgumentNullException(nameof(resolveBattleSlotForStage));
            ResolveBattleSourceForStage = resolveBattleSourceForStage
                ?? throw new ArgumentNullException(nameof(resolveBattleSourceForStage));
            GetSlotById = getSlotById ?? throw new ArgumentNullException(nameof(getSlotById));
            GetUnitIcon = getUnitIcon ?? throw new ArgumentNullException(nameof(getUnitIcon));
            GetGeneralPartIcon = getGeneralPartIcon ?? throw new ArgumentNullException(nameof(getGeneralPartIcon));
            GetPropIcon = getPropIcon ?? throw new ArgumentNullException(nameof(getPropIcon));
        }
    }

    /// <summary>
    /// 把 Stage 坐标解析为玩家战场槽位标识的委托（最终方案：屏幕坐标只负责识别 SlotId）。
    /// </summary>
    /// <param name="screenX">Stage X 坐标。</param>
    /// <param name="screenY">Stage Y 坐标。</param>
    /// <param name="targetSlotId">解析出的玩家战场槽位固定标识；未命中为 -1。</param>
    /// <returns>解析成功且命中战场槽返回 true。</returns>
    internal delegate bool ResolveBattleSlotDelegate(
        float screenX,
        float screenY,
        out int targetSlotId);

    /// <summary>
    /// 把 Stage 坐标解析为可起拖的玩家战场源槽位标识的委托（统一拖放规则：源命中外层槽位框架）。
    /// </summary>
    /// <param name="screenX">Stage X 坐标。</param>
    /// <param name="screenY">Stage Y 坐标。</param>
    /// <param name="battleSlotId">解析出的玩家战场源槽位固定标识；未命中为 -1。</param>
    /// <returns>解析成功、命中非空玩家战场槽位框架返回 true。</returns>
    /// <remarks>
    /// <para>先锁定玩家战场槽（完整槽位命中），再检查该槽是否有单位。起拖不依赖
    /// 活动单位内部的 Body/Spine Renderer，投放目标仍使用完整的
    /// <see cref="ResolveBattleSlotForStage"/> 命中。</para>
    /// </remarks>
    internal delegate bool ResolveBattleSourceDelegate(
        float screenX,
        float screenY,
        out int battleSlotId);
}
