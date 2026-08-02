using System;

namespace Game.Combat.Ports
{
    public interface ICombatClock { long NowMilliseconds { get; } }
    public interface IRandomSource { float Next01(); int Range(int minInclusive, int maxExclusive); }
    public interface ICombatView { object Spawn(string key, object state); void Remove(object handle); }
    public interface IAudioPort { void Play(string key, bool loop = false); void Stop(string key); }
    public interface IVfxPort { object Create(string key, object context); void Remove(object handle); }
    public interface IScenePort { void Open(string scene, object args = null); void Close(string scene); }
    public interface IResourcePort { T Load<T>(string key); }
}
