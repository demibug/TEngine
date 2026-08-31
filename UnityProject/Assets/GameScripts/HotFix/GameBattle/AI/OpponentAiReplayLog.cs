using System;
using System.Collections.Generic;
using System.Text;

namespace GameBattle
{
    /// <summary>
    /// 可选的本地对手 AI 稳定回放记录器。只记录已成功提交的动作，
    /// 不把运行时对象或 Unity 表现层引用带入日志。
    /// </summary>
    internal sealed class OpponentAiReplayLog
    {
        private readonly List<string> _entries = new List<string>();
        private readonly bool _enabled;

        internal OpponentAiReplayLog(bool enabled = true)
        {
            _enabled = enabled;
        }

        internal IReadOnlyList<string> Entries => _entries.AsReadOnly();

        internal void Clear()
        {
            _entries.Clear();
        }

        internal void Record(
            long decisionTick,
            OpponentAiAction action,
            BattleInputResult result)
        {
            if (!_enabled || action == null)
            {
                return;
            }

            GridPosition position = action.TargetPosition;
            string outcome = result.IsSuccess
                ? "ok"
                : result.RejectReason.ToString();
            _entries.Add(
                $"tick={decisionTick};action={action.Type};source={action.SourceSlotId};" +
                $"target={action.TargetSlotId};secondary={action.SecondarySourceSlotId};" +
                $"position={position.X},{position.Y};expected={action.ExpectedUnitId};" +
                $"reason={action.Reason};result={outcome}");
        }

        internal string ToStableText()
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(_entries[i]);
            }

            return builder.ToString();
        }
    }
}
