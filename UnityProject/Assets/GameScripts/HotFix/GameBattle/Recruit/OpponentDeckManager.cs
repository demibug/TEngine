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
    /// <para>基础牌按原始行为保留在可变池中；武将字抽出后从池中移除。是否再移除
    /// 一张同字牌由本侧是否执行过 <see cref="CopyGeneralParts"/> 决定，而不是由
    /// 价值评估配置决定。这样既保留原始的基础兵权重，也不会让两侧牌库共享可变状态。</para>
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
        private readonly List<OpponentDeckCard> _remaining = new List<OpponentDeckCard>();
        private bool _generalPartsCopied;
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
            // 保留旧构造参数以兼容现有 Factory/调用方；牌池重复语义由
            // CopyGeneralParts 设置的本侧状态决定，不能由 EnableValueEvaluation
            // （当前旧参数的生产来源）间接控制。
            _ = allowGeneralPartDuplicates;
            Reset();
        }

        /// <summary>原始 bundle 初始牌池展开后应为 108 项。</summary>
        internal static int OriginalPoolSize => OriginalPoolTexts.Length;

        /// <summary>当前可抽取牌数；基础牌因遵循原始行为不会随抽取减少。</summary>
        internal int RemainingCount => _remaining.Count;

        /// <summary>本侧是否已经执行过武将字复制；供 no-repeat 抽牌决定重复字处理。</summary>
        internal bool GeneralPartsCopied => _generalPartsCopied;

        /// <summary>读取牌库副本，调用方不能通过它修改内部状态。</summary>
        internal IReadOnlyList<OpponentDeckCard> RemainingCards
            => new List<OpponentDeckCard>(_remaining).AsReadOnly();

        /// <summary>按配置重置本侧独立牌库。</summary>
        internal void Reset()
        {
            _remaining.Clear();
            _generalPartsCopied = false;
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

        /// <summary>按原始 drawCardNoRepeat 语义抽一张牌。</summary>
        internal OpponentDeckCard Draw()
            => DrawCardNoRepeat();

        /// <summary>
        /// 按原始 drawCardNoRepeat 语义抽一张牌：基础兵、铲子和农民可重复；
        /// 武将字抽出后移除，复制状态已置位时再移除一张同字牌。
        /// </summary>
        internal OpponentDeckCard DrawCardNoRepeat()
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
                if (_generalPartsCopied)
                {
                    RemoveAnotherCopy(card.Text);
                }
            }

            return card;
        }

        /// <summary>按原始 drawHand 语义生成固定数量的初始逻辑手牌牌面。</summary>
        internal IReadOnlyList<OpponentDeckCard> DrawHand(int handSize)
            => DrawInitialHand(handSize);

        /// <summary>
        /// 按原始 drawHand 语义生成初始手牌。初始抽牌只读取牌池，不消费牌，
        /// 因而基础牌和武将字都可以在初始手牌中重复出现；每个手牌槽仍获得独立牌 ID。
        /// </summary>
        internal IReadOnlyList<OpponentDeckCard> DrawInitialHand(int handSize)
        {
            int count = Math.Max(0, handSize);
            var hand = new List<OpponentDeckCard>(count);
            for (int i = 0; i < count; i++)
            {
                hand.Add(DrawInitialCard());
            }

            return hand;
        }

        /// <summary>
        /// 按原始 drawCardNoRepeat 语义生成一次刷新手牌；基础牌不消费，
        /// 非基础武将字按本侧复制状态消费。
        /// </summary>
        internal IReadOnlyList<OpponentDeckCard> DrawRefreshHand(int handSize)
        {
            int count = Math.Max(0, handSize);
            var hand = new List<OpponentDeckCard>(count);
            for (int i = 0; i < count; i++)
            {
                hand.Add(DrawCardNoRepeat());
            }

            return hand;
        }

        /// <summary>
        /// 按原始 xO 语义向本侧牌库注入铲子：第 3 天及以前，按玩家铲子数的
        /// floor(count / 5) 注入；第 4 天起不再注入。此方法不触碰手牌或棋盘。
        /// </summary>
        internal void InjectShovels(int day, int playerShovelCount)
        {
            if (!_includeShovels || day > 3 || playerShovelCount <= 0)
            {
                return;
            }

            AddShovels(playerShovelCount / 5);
        }

        /// <summary>
        /// 兼容旧调用方的直接数量注入；新的生产语义应使用
        /// <see cref="InjectShovels(int, int)"/> 传入天数和玩家铲子数。
        /// </summary>
        internal void InjectShovels(int count)
        {
            AddShovels(count);
        }

        private void AddShovels(int count)
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

        /// <summary>按原始 dP 语义以 50% 概率复制本侧现有武将字。</summary>
        internal void CopyGeneralParts()
        {
            if (!_allowGeneralParts)
            {
                _generalPartsCopied = true;
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
            _generalPartsCopied = true;
        }

        private OpponentDeckCard DrawInitialCard()
        {
            if (_remaining.Count == 0)
            {
                return CreateCard("刀", OpponentCardKind.Soldier);
            }

            int index = SelectIndex(_remaining.Count);
            OpponentDeckCard card = _remaining[index];
            // drawHand creates a new card from a non-consuming text draw. Do the
            // same here so repeated initial slots do not share a CardId.
            return CreateCard(card.Text, card.Kind, card.Level);
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
