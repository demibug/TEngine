using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
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
        private const string FallbackBackgroundName = "fallbackBackground";

        private BattleHudEntryArgs _entryArgs;
        private readonly List<UI_BattleCardItem> _cards = new List<UI_BattleCardItem>();
        private bool _isExiting;
        private int _selectedHandSlot = -1;
        private BattleDragController _dragController;

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
                card.draggable = true;
                card.onDragStart.Add(context => OnCardDragStart(context, slotIndex));
                card.onDragEnd.Add(context => OnCardDragEnd(context, slotIndex));
                SetCardVisual(
                    card,
                    occupant.HasValue ? occupant.Value.SoldierText : null,
                    _entryArgs.GetUnitIcon);
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
        /// </summary>
        private static void SetCardVisual(
            UI_BattleCardItem card,
            string soldierText,
            Func<int, Sprite> getUnitIcon)
        {
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
            if (sprite != null)
            {
                var icon = new GLoader
                {
                    name = "unitIcon",
                    touchable = false,
                    fill = FillType.Scale,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle,
                    texture = new NTexture(sprite),
                };
                icon.SetSize(CardWidth - 12f, CardWidth - 12f);
                icon.SetXY(6f, 6f);
                card.AddChild(icon);
                return;
            }

            var label = new GTextField
            {
                name = "fallbackLabel",
                autoSize = AutoSizeType.None,
                align = AlignType.Center,
                verticalAlign = VertAlignType.Middle,
                touchable = false,
            };
            TextFormat format = label.textFormat;
            format.size = 38;
            format.bold = true;
            format.color = new Color(0.25f, 0.08f, 0.03f, 1f);
            label.textFormat = format;
            label.SetSize(CardWidth, CardWidth);
            label.text = string.IsNullOrEmpty(soldierText) ? "空" : soldierText ?? "?";
            card.AddChild(label);
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
                _dragController?.BeginDrag(slots[reserveSlotIndex].SlotId.Id, context.inputEvent?.touchId ?? -1);
            }
        }

        private void OnCardDragEnd(EventContext context, int reserveSlotIndex)
        {
            context.StopPropagation();

            // 复位卡回到原位（未命中目标时表现弹回源槽）。
            if (reserveSlotIndex >= 0 && reserveSlotIndex < _cards.Count && _cards[reserveSlotIndex] != null)
            {
                _cards[reserveSlotIndex].xy = ReserveCardHomePosition(reserveSlotIndex);
            }

            if (_entryArgs == null || Stage.inst == null)
            {
                _dragController?.Cancel();
                return;
            }

            int touchId = context.inputEvent?.touchId ?? -1;
            Vector2 stagePosition = Stage.inst.GetTouchPosition(touchId);
            BattleInputResult? result = _dragController?.EndDrag(stagePosition.x, stagePosition.y);
            if (result.HasValue)
            {
                TryDropUnit(result.Value);
            }
        }

        /// <summary>Reserve 卡在主面板的初始位置。</summary>
        private Vector2 ReserveCardHomePosition(int index)
        {
            IReadOnlyList<UnitSlot> slots = _entryArgs?.GetPlayerReserveSlots?.Invoke();
            int count = slots?.Count ?? 0;
            float totalWidth = count * CardWidth + (count - 1) * CardSpacing;
            float startX = (width - totalWidth) * 0.5f;
            return new Vector2(startX + index * (CardWidth + CardSpacing), height - CardBottom - CardWidth);
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
                if (card?.m_cState != null)
                {
                    card.m_cState.selectedPage = index == _selectedHandSlot ? "Selected" : "Normal";
                }

                if (card?.GetChild(FallbackBackgroundName) is GGraph background)
                {
                    bool selected = index == _selectedHandSlot;
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

        /// <summary>提交 DropUnit 命令并刷新槽位表现。</summary>
        private void TryDropUnit(BattleInputResult result)
        {
            if (result.IsSuccess)
            {
                RefreshCards();
            }
            else
            {
                Log.Info(
                    $"[BattleDiagnostic] 换槽结果 rejected={result.RejectReason} " +
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
                BattleOperationResult result = await entryArgs.ExitAsync(OpenCancellationToken);
                if (!result.IsSuccess && !OpenCancellationToken.IsCancellationRequested)
                {
                    Log.Warning($"[BattleHudPanel] 退出战斗失败: {result}");
                    RestoreExitButton();
                }
            }
            catch (OperationCanceledException) when (OpenCancellationToken.IsCancellationRequested)
            {
                // 窗口关闭会取消当前等待，模块退出清理由独立令牌保证完成。
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleHudPanel] 退出战斗发生异常: {ex}");
                RestoreExitButton();
            }
        }

        private void RestoreExitButton()
        {
            if (OpenCancellationToken.IsCancellationRequested)
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
    /// 战斗 HUD 单次打开参数（最终方案：征兵 + 换槽/合并 + 槽位快照）。
    /// </summary>
    internal sealed class BattleHudEntryArgs
    {
        internal Func<CancellationToken, UniTask<BattleOperationResult>> ExitAsync { get; }
        internal Func<BattleInputResult> Recruit { get; }
        internal Func<int, int, BattleInputResult> DropUnit { get; }
        internal Func<IReadOnlyList<UnitSlot>> GetPlayerReserveSlots { get; }
        internal ResolveBattleSlotDelegate ResolveBattleSlotForStage { get; }
        internal Func<int, Sprite> GetUnitIcon { get; }

        internal BattleHudEntryArgs(
            Func<CancellationToken, UniTask<BattleOperationResult>> exitAsync,
            Func<BattleInputResult> recruit,
            Func<int, int, BattleInputResult> dropUnit,
            Func<IReadOnlyList<UnitSlot>> getPlayerReserveSlots,
            ResolveBattleSlotDelegate resolveBattleSlotForStage,
            Func<int, Sprite> getUnitIcon)
        {
            ExitAsync = exitAsync ?? throw new ArgumentNullException(nameof(exitAsync));
            Recruit = recruit ?? throw new ArgumentNullException(nameof(recruit));
            DropUnit = dropUnit ?? throw new ArgumentNullException(nameof(dropUnit));
            GetPlayerReserveSlots = getPlayerReserveSlots
                ?? throw new ArgumentNullException(nameof(getPlayerReserveSlots));
            ResolveBattleSlotForStage = resolveBattleSlotForStage
                ?? throw new ArgumentNullException(nameof(resolveBattleSlotForStage));
            GetUnitIcon = getUnitIcon ?? throw new ArgumentNullException(nameof(getUnitIcon));
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
}
