using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>对手牌面的类别；牌库只描述牌面，不直接修改棋盘。</summary>
    internal enum OpponentCardKind
    {
        Soldier = 0,
        GeneralPart = 1,
        Farmer = 2,
        Shovel = 3,
    }

    /// <summary>对手牌库中的一张不可变牌面。</summary>
    internal sealed class OpponentDeckCard
    {
        internal int CardId { get; }
        internal string Text { get; }
        internal OpponentCardKind Kind { get; }
        internal int Level { get; }

        internal bool IsGeneralPart => Kind == OpponentCardKind.GeneralPart;
        internal bool IsShovel => Kind == OpponentCardKind.Shovel;
        internal bool IsFarmer => Kind == OpponentCardKind.Farmer;

        internal OpponentDeckCard(int cardId, string text, OpponentCardKind kind, int level = 1)
        {
            CardId = cardId;
            Text = text ?? string.Empty;
            Kind = kind;
            Level = level > 0 ? level : RecruitDefinitions.DefaultLevel;
        }

        internal OpponentDeckCard WithCardId(int cardId)
            => new OpponentDeckCard(cardId, Text, Kind, Level);

        public override string ToString()
            => $"OpponentDeckCard(Id={CardId}, Text={Text}, Kind={Kind}, Level={Level})";
    }

    /// <summary>
    /// 原始 bundle 对手牌库的确定性适配层。
    /// </summary>
    /// <remarks>
    /// <para>基础牌按原始行为保留在可变池中；武将字抽出后从池中移除，重复字的
    /// 是否保留由 <paramref name="allowGeneralPartDuplicates"/> 控制。这样既保留
    /// 原始的基础兵权重，也不会让两侧牌库共享可变状态。</para>
    /// <para>牌库只生成牌面。单位 ID 和棋盘状态仍由 <see cref="RecruitManager"/>/
    /// <see cref="UnitSlotBoard"/> 负责。</para>
    /// </remarks>
    internal sealed class OpponentDeckManager
    {
        private static readonly string[] OriginalPoolTexts = BuildOriginalPoolTexts();

        private readonly IRandomSource _randomSource;
        private readonly bool _allowGeneralParts;
        private readonly bool _allowFarmer;
        private readonly bool _includeShovels;
        private readonly bool _allowGeneralPartDuplicates;
        private readonly List<OpponentDeckCard> _remaining = new List<OpponentDeckCard>();
        private int _nextCardId = 1;

        internal OpponentDeckManager(
            IRandomSource randomSource,
            bool allowGeneralParts = true,
            bool allowFarmer = false,
            bool includeShovels = true,
            bool allowGeneralPartDuplicates = false)
        {
            _randomSource = randomSource ?? throw new ArgumentNullException(nameof(randomSource));
            _allowGeneralParts = allowGeneralParts;
            _allowFarmer = allowFarmer;
            _includeShovels = includeShovels;
            _allowGeneralPartDuplicates = allowGeneralPartDuplicates;
            Reset();
        }

        /// <summary>原始 bundle 初始牌池展开后应为 108 项。</summary>
        internal static int OriginalPoolSize => OriginalPoolTexts.Length;

        /// <summary>当前可抽取牌数；基础牌因遵循原始行为不会随抽取减少。</summary>
        internal int RemainingCount => _remaining.Count;

        /// <summary>读取牌库副本，调用方不能通过它修改内部状态。</summary>
        internal IReadOnlyList<OpponentDeckCard> RemainingCards
            => new List<OpponentDeckCard>(_remaining).AsReadOnly();

        /// <summary>按配置重置本侧独立牌库。</summary>
        internal void Reset()
        {
            _remaining.Clear();
            _nextCardId = 1;
            for (int i = 0; i < OriginalPoolTexts.Length; i++)
            {
                string text = OriginalPoolTexts[i];
                OpponentCardKind kind = Classify(text);
                if (!IsAllowed(kind))
                {
                    continue;
                }

                _remaining.Add(CreateCard(text, kind));
            }

            if (_allowFarmer)
            {
                // Farmer 由原始运行时特殊牌面注入，不计入 bundle 的 108 项基础池。
                _remaining.Add(CreateCard("农", OpponentCardKind.Farmer));
            }
        }

        /// <summary>抽一张牌；牌库为空时返回安全的 1 级刀牌。</summary>
        internal OpponentDeckCard Draw()
        {
            if (_remaining.Count == 0)
            {
                return CreateCard("刀", OpponentCardKind.Soldier);
            }

            int index = SelectIndex(_remaining.Count);
            OpponentDeckCard card = _remaining[index];
            if (card.IsGeneralPart)
            {
                _remaining.RemoveAt(index);
                if (!_allowGeneralPartDuplicates)
                {
                    RemoveAnotherCopy(card.Text);
                }
            }

            return card;
        }

        /// <summary>为一次刷新生成固定数量的逻辑手牌牌面。</summary>
        internal IReadOnlyList<OpponentDeckCard> DrawHand(int handSize)
        {
            int count = Math.Max(0, handSize);
            var hand = new List<OpponentDeckCard>(count);
            for (int i = 0; i < count; i++)
            {
                hand.Add(Draw());
            }

            return hand;
        }

        /// <summary>按原始 xO 语义向牌库注入铲子；不触碰手牌或棋盘。</summary>
        internal void InjectShovels(int count)
        {
            if (!_includeShovels || count <= 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                _remaining.Add(CreateCard("铲", OpponentCardKind.Shovel));
            }
        }

        /// <summary>按原始 dP 语义复制部分武将字。</summary>
        internal void CopyGeneralParts()
        {
            if (!_allowGeneralParts)
            {
                return;
            }

            int initialCount = _remaining.Count;
            var copies = new List<OpponentDeckCard>();
            for (int i = 0; i < initialCount; i++)
            {
                OpponentDeckCard card = _remaining[i];
                if (!card.IsGeneralPart || _randomSource.NextUnit() >= 0.5f)
                {
                    continue;
                }

                copies.Add(CreateCard(card.Text, card.Kind, card.Level));
            }

            _remaining.AddRange(copies);
        }

        private OpponentDeckCard CreateCard(string text, OpponentCardKind kind, int level = 1)
            => new OpponentDeckCard(_nextCardId++, text, kind, level);

        private bool IsAllowed(OpponentCardKind kind)
        {
            if (kind == OpponentCardKind.GeneralPart)
            {
                return _allowGeneralParts;
            }

            if (kind == OpponentCardKind.Shovel)
            {
                return _includeShovels;
            }

            if (kind == OpponentCardKind.Farmer)
            {
                return _allowFarmer;
            }

            return true;
        }

        private void RemoveAnotherCopy(string text)
        {
            for (int i = 0; i < _remaining.Count; i++)
            {
                if (string.Equals(_remaining[i].Text, text, StringComparison.Ordinal))
                {
                    _remaining.RemoveAt(i);
                    return;
                }
            }
        }

        private int SelectIndex(int count)
        {
            int selected = (int)Math.Floor(_randomSource.NextUnit() * count);
            return Math.Max(0, Math.Min(selected, count - 1));
        }

        private static OpponentCardKind Classify(string text)
        {
            switch (text)
            {
                case "刀":
                case "弓":
                case "枪":
                case "骑":
                    return OpponentCardKind.Soldier;
                case "铲":
                    return OpponentCardKind.Shovel;
                case "农":
                    return OpponentCardKind.Farmer;
                default:
                    return OpponentCardKind.GeneralPart;
            }
        }

        private static string[] BuildOriginalPoolTexts()
        {
            var texts = new List<string>(108);
            AddRepeated(texts, "刀", 21);
            AddRepeated(texts, "弓", 19);
            AddRepeated(texts, "枪", 18);
            AddRepeated(texts, "骑", 17);
            AddRepeated(texts, "铲", 11);
            AddRepeated(texts, "刘", 1);
            AddRepeated(texts, "赵", 2);
            AddRepeated(texts, "云", 1);
            AddRepeated(texts, "关", 1);
            AddRepeated(texts, "羽", 1);
            AddRepeated(texts, "平", 1);
            AddRepeated(texts, "兴", 1);
            AddRepeated(texts, "马", 2);
            AddRepeated(texts, "超", 1);
            AddRepeated(texts, "张", 2);
            AddRepeated(texts, "飞", 1);
            AddRepeated(texts, "苞", 1);
            AddRepeated(texts, "翼", 1);
            AddRepeated(texts, "黄", 2);
            AddRepeated(texts, "忠", 1);
            AddRepeated(texts, "盖", 1);
            AddRepeated(texts, "祖", 1);
            AddRepeated(texts, "备", 1);
            return texts.ToArray();
        }

        private static void AddRepeated(List<string> target, string text, int count)
        {
            for (int i = 0; i < count; i++)
            {
                target.Add(text);
            }
        }
    }
}
