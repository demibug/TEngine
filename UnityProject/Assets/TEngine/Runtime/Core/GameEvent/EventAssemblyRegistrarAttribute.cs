using System;
using UnityEngine.Scripting;

namespace TEngine
{
    /// <summary>
    /// 程序集级事件注册标记：标明本程序集的事件接口由哪个 Registrar 类完成注册。
    /// 由 Source Generator 在含 [EventInterface] 接口的编译单元中自动生成（一个程序集至多一个），
    /// 严禁手写添加（GameEventAnalyzer EVENT004 会报编译错误）。
    /// </summary>
    [Preserve]
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class EventAssemblyRegistrarAttribute : Attribute
    {
        /// <summary>
        /// 本程序集的事件注册器类型。
        /// </summary>
        public Type RegistrarType { get; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="registrarType">注册器类型。</param>
        public EventAssemblyRegistrarAttribute(Type registrarType)
        {
            RegistrarType = registrarType;
        }
    }
}