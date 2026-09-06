using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>对手逻辑手牌中一槽的只读视图。</summary>
    internal readonly struct OpponentHandSlot
    {
        internal readonly int Index;
        internal readonly OpponentDeckCard Card;
        internal readonly int UnitId;

        internal bool IsEmpty => Card == null;

        internal OpponentHandSlot(int index, OpponentDeckCard card, int unitId = UnitSlot.InvalidUnitId)
        {
            Index = index;
            Card = card;
            UnitId = unitId;
        }
    }

    /// <summary>
    /// 对手固定容量手牌。它只维护牌面和与 Reserve 投影的单位 ID，不写棋盘。
    /// </summary>
    internal sealed class OpponentHand
    {
        private readonly List<OpponentHandSlot> _slots;

        internal OpponentHand(int capacity)
        {
            int size = capacity > 0 ? capacity : RecruitDefinitions.ReserveSlotCount;
            _slots = new List<OpponentHandSlot>(size);
            for (int i = 0; i < size; i++)
            {
                _slots.Add(new OpponentHandSlot(i, null));
            }
        }

        internal int Capacity => _slots.Count;

        internal int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (!_slots[i].IsEmpty)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        internal bool IsEmpty => Count == 0;

        internal IReadOnlyList<OpponentHandSlot> Slots
            => new List<OpponentHandSlot>(_slots).AsReadOnly();

        internal bool IsSlotValid(int slotIndex)
            => slotIndex >= 0 && slotIndex < _slots.Count;

        internal OpponentHandSlot GetSlot(int slotIndex)
        {
            if (!IsSlotValid(slotIndex))
            {
                return default;
            }

            return _slots[slotIndex];
        }

        /// <summary>以新一组牌面替换整手牌；多余牌面被弃置，不触碰棋盘。</summary>
        internal void ReplaceAll(IReadOnlyList<OpponentDeckCard> cards)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                OpponentDeckCard card = cards != null && i < cards.Count ? cards[i] : null;
                _slots[i] = new OpponentHandSlot(i, card);
            }
        }

        /// <summary>以原始 drawHand 语义发放初始手牌；初始抽牌不消费牌库。</summary>
        internal void DealInitial(OpponentDeckManager deck)
        {
            if (deck == null)
            {
                throw new ArgumentNullException(nameof(deck));
            }

            ReplaceAll(deck.DrawInitialHand(Capacity));
        }

        /// <summary>以原始 drawCardNoRepeat 语义刷新手牌。</summary>
        internal void Refill(OpponentDeckManager deck)
        {
            if (deck == null)
            {
                throw new ArgumentNullException(nameof(deck));
            }

            ReplaceAll(deck.DrawRefreshHand(Capacity));
        }

        /// <summary>将手牌槽绑定到 Reserve 投影生成的单位 ID。</summary>
        internal void BindUnitIds(IReadOnlyList<BattleUnit> units)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                OpponentDeckCard card = _slots[i].Card;
                int unitId = units != null && i < units.Count && card != null
                    ? units[i].UnitId
                    : UnitSlot.InvalidUnitId;
                _slots[i] = new OpponentHandSlot(i, card, unitId);
            }
        }

        internal bool TryRemoveAt(int slotIndex, out OpponentDeckCard card)
        {
            card = null;
            if (!IsSlotValid(slotIndex) || _slots[slotIndex].IsEmpty)
            {
                return false;
            }

            card = _slots[slotIndex].Card;
            _slots[slotIndex] = new OpponentHandSlot(slotIndex, null);
            return true;
        }

        internal bool TryRemoveByUnitId(int unitId, out OpponentDeckCard card)
        {
            card = null;
            if (unitId <= UnitSlot.InvalidUnitId)
            {
                return false;
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].UnitId != unitId)
                {
                    continue;
                }

                card = _slots[i].Card;
                _slots[i] = new OpponentHandSlot(i, null);
                return true;
            }

            return false;
        }

        internal bool TryReplaceAt(int slotIndex, OpponentDeckCard card)
        {
            if (!IsSlotValid(slotIndex) || card == null)
            {
                return false;
            }

            _slots[slotIndex] = new OpponentHandSlot(slotIndex, card);
            return true;
        }

        internal void Clear()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i] = new OpponentHandSlot(i, null);
            }
        }
    }
}
