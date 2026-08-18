using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.4：SkillHandlerRegistry —— 小型显式 handler 注册表与 handler 契约
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 1/4 / specs/combat-skill-lifecycle/spec.md）：
    //   handlerKey → ISkillHandler 的小型显式字典，attach 时才要求 handler 存在。
    //   目录可含未实现 handler 的技能行，但这类技能不得 attach（spec "Catalog rows
    //   MAY exist without a registered handler, but such rows MUST NOT be attached"）。
    //
    // 不变量：
    //   1. 拒绝空 key、null handler 与重复注册（严格注册，不做覆盖）。
    //   2. 未注册查询返回 false，不抛异常、不 fallback。
    //   3. handler 只接收不可变上下文；具体 handler 经构造注入窄依赖，不访问全局
    //      Runtime。本 change 不注册任何生产 handler（无 NoOp/Passive 假实现）。
    // ============================================================================

    /// <summary>
    /// 技能 handler 契约：接收不可变激活上下文，拥有 effect/complete/cancel 三个调用点。
    /// </summary>
    /// <remarks>
    /// <para>spec "Concrete effects are explicit handlers"：具体 handler 通过构造
    /// 注入自己的 Unit/Buff/表现窄端口，Runner 不持有 <c>BattleRuntime</c>，也不新增
    /// 通用表现协议或效果 DSL。</para>
    /// <list type="bullet">
    /// <item><see cref="Effect"/>：激活的 effect 时间点执行（提交点，此时尚未有任何
    /// 外部效果被本激活提交）。</item>
    /// <item><see cref="Complete"/>：激活的 complete 时间点执行；effect 必然已经提交
    /// （spec "Effect precedes completion"）。</item>
    /// <item><see cref="Cancel"/>：激活被取消时执行；<paramref name="effectCommitted"/>
    /// 为 true 表示 effect 已提交（取消保留已提交外部效果，不自动回滚）。</item>
    /// </list>
    /// </remarks>
    internal interface ISkillHandler
    {
        /// <summary>激活的 effect 时间点：执行技能效果并提交外部效果。</summary>
        /// <param name="context">不可变激活上下文。</param>
        void Effect(SkillActivationContext context);

        /// <summary>激活的 complete 时间点：效果已完成，执行收尾。</summary>
        /// <param name="context">不可变激活上下文。</param>
        void Complete(SkillActivationContext context);

        /// <summary>激活被取消：撤销未来回调；effect 已提交时不自动回滚外部效果。</summary>
        /// <param name="context">不可变激活上下文。</param>
        /// <param name="effectCommitted">effect 是否已提交（取消时可能为 false 或 true）。</param>
        void Cancel(SkillActivationContext context, bool effectCommitted);
    }

    /// <summary>
    /// 以 handlerKey 唯一登记技能 handler；不提供缺失 fallback（spec "handler key strict"）。
    /// </summary>
    /// <remarks>
    /// <para>注册键与 Skill 定义中的 <c>HandlerKey</c> 严格匹配；空 key、null handler
    /// 与重复注册均为编程错误，抛出明确异常。未注册查询返回 false，不抛异常。</para>
    /// </remarks>
    internal sealed class SkillHandlerRegistry
    {
        private readonly Dictionary<string, ISkillHandler> _handlers =
            new Dictionary<string, ISkillHandler>(StringComparer.Ordinal);

        /// <summary>按 handlerKey 唯一登记 handler。</summary>
        /// <param name="handlerKey">handler 键（必须非空）。</param>
        /// <param name="handler">handler 实例（必须非 null）。</param>
        /// <exception cref="ArgumentException"><paramref name="handlerKey"/> 为空。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 null。</exception>
        /// <exception cref="InvalidOperationException">handlerKey 已注册。</exception>
        public void Register(string handlerKey, ISkillHandler handler)
        {
            if (string.IsNullOrEmpty(handlerKey))
            {
                throw new ArgumentException("handlerKey 不能为空", nameof(handlerKey));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (_handlers.ContainsKey(handlerKey))
            {
                throw new InvalidOperationException($"Skill handler 已注册：handlerKey='{handlerKey}'");
            }

            _handlers.Add(handlerKey, handler);
        }

        /// <summary>按 handlerKey 查询 handler。</summary>
        /// <param name="handlerKey">handler 键。</param>
        /// <param name="handler">命中的 handler；未注册时为 null。</param>
        /// <returns>已注册时返回 true，否则 false。</returns>
        public bool TryGet(string handlerKey, out ISkillHandler handler)
        {
            return _handlers.TryGetValue(handlerKey, out handler);
        }

    }
}
