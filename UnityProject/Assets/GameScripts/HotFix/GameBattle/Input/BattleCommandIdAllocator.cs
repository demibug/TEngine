using System;

namespace GameBattle
{
    /// <summary>UI 与本地对手 AI 共享的单局输入命令编号分配器。</summary>
    internal sealed class BattleCommandIdAllocator
    {
        private int _nextId = 1;

        internal int Allocate()
        {
            if (_nextId == int.MaxValue)
            {
                throw new InvalidOperationException("单局 BattleInputCommand 数量超过 int 上限");
            }

            int id = _nextId;
            _nextId++;
            return id;
        }
    }
}
