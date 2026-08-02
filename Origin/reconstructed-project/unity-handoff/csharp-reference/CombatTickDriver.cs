using System;
using System.Collections.Generic;

namespace Game.Combat.Runtime
{
    public interface ICombatTickable { void Tick(int deltaMs); }

    public sealed class CombatTickDriver
    {
        public const int FixedStepMs = 80;
        public const int MaxAccumulatedMs = 500;
        private int _accumulatorMs;
        private bool _paused;
        private readonly List<ICombatTickable> _tickables = new();

        public void Register(ICombatTickable tickable) => _tickables.Add(tickable);
        public void Unregister(ICombatTickable tickable) => _tickables.Remove(tickable);
        public void Pause() => _paused = true;
        public void Resume() { _paused = false; _accumulatorMs = 0; }

        public void Advance(int frameDeltaMs)
        {
            if (_paused) return;
            _accumulatorMs = Math.Min(MaxAccumulatedMs, _accumulatorMs + Math.Max(0, frameDeltaMs));
            while (_accumulatorMs >= FixedStepMs)
            {
                for (var i = 0; i < _tickables.Count; i++) _tickables[i].Tick(FixedStepMs);
                _accumulatorMs -= FixedStepMs;
            }
        }
    }
}
