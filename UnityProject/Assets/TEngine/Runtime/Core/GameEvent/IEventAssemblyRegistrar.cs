using UnityEngine.Scripting;

namespace TEngine
{
    /// <summary>
    /// 程序集事件注册器：一次调用注册本程序集全部 [EventInterface] 接口。
    /// 实现类由 Source Generator 生成（纯生成物、无业务代码），调用方为 GameEventHelper。
    /// </summary>
    [Preserve]
    public interface IEventAssemblyRegistrar
    {
        /// <summary>
        /// 注册本程序集全部事件接口（构造各 {接口}_Gen 即完成注册）。
        /// </summary>
        /// <param name="dispatcher">事件分发器。</param>
        void Register(EventDispatcher dispatcher);
    }
}