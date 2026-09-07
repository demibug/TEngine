using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 游戏事件数据类。
    /// <remarks>分发契约：
    /// 1. 回调异常原样向上传播，首个异常立即结束本次分发，不吞掉、不包装、不继续执行余下回调；
    /// 2. 每个事件独立维护分发深度，所有 Callback 重载以 finally 恢复状态，即使异常退出也恢复；
    /// 3. 同事件嵌套分发共用最外层开始时的已生效监听快照，回调中增删只写入下一版本监听列表，
    ///    仅该事件最外层分发退出时按调用顺序提交，因此嵌套期间不会提前提交；
    /// 4. 无分发时增删立即生效；
    /// 5. 清表（Shutdown / ClearEventTable）后旧事件对象脱离分发器，其 finally 提交只作用于自身，不会把旧监听写回分发器。</remarks>
    /// </summary>
    internal class EventDelegateData
    {
        private readonly int _eventType = 0;
        private List<Delegate> _listExist = new List<Delegate>();
        private List<Delegate> _nextList = null;
        private int _dispatchDepth = 0;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="eventType">事件类型。</param>
        internal EventDelegateData(int eventType)
        {
            _eventType = eventType;
        }

        /// <summary>
        /// 添加注册委托。
        /// </summary>
        /// <param name="handler">事件处理回调。</param>
        /// <returns>是否添加回调成功。</returns>
        internal bool AddHandler(Delegate handler)
        {
            // 成员关系按“已应用全部待操作后的下一版本列表”判定：待新增重复返回 false，移除后重注册视为新监听。
            if ((_nextList ?? _listExist).Contains(handler))
            {
                Log.Fatal("Repeated Add Handler");
                return false;
            }

            if (_dispatchDepth > 0)
            {
                EnsureNextList().Add(handler);
            }
            else
            {
                _listExist.Add(handler);
            }

            return true;
        }

        /// <summary>
        /// 移除反注册委托。
        /// </summary>
        /// <param name="handler">事件处理回调。</param>
        internal void RmvHandler(Delegate handler)
        {
            if (_dispatchDepth > 0)
            {
                // 分发中只登记到下一版本列表；缺失或重复移除时 List.Remove 返回 false，按无操作处理，不抛新异常。
                EnsureNextList().Remove(handler);
                return;
            }

            if (!_listExist.Remove(handler))
            {
                Log.Fatal("Delete handle failed, not exist, EventId: {0}", RuntimeId.ToString(_eventType));
            }
        }

        /// <summary>
        /// 确保下一版本监听列表存在（首次有效变更时懒拷贝当前快照，无变更的分发不产生拷贝）。
        /// </summary>
        /// <returns>下一版本监听列表。</returns>
        private List<Delegate> EnsureNextList()
        {
            if (_nextList == null)
            {
                _nextList = new List<Delegate>(_listExist);
            }

            return _nextList;
        }

        /// <summary>
        /// 结束一次分发，递减深度，仅该事件最外层分发退出时提交待变更列表。
        /// <remarks>finally 中调用，正常与异常退出都会提交，不做业务回滚。</remarks>
        /// </summary>
        private void EndDispatch()
        {
            _dispatchDepth--;
            if (_dispatchDepth == 0 && _nextList != null)
            {
                _listExist = _nextList;
                _nextList = null;
            }
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        public void Callback()
        {
            _dispatchDepth++;
            try
            {
                for (var i = 0; i < _listExist.Count; i++)
                {
                    var d = _listExist[i];
                    if (d is Action action)
                    {
                        action();
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        public void Callback<TArg1>(TArg1 arg1)
        {
            _dispatchDepth++;
            try
            {
                for (var i = 0; i < _listExist.Count; i++)
                {
                    var d = _listExist[i];
                    if (d is Action<TArg1> action)
                    {
                        action(arg1);
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        public void Callback<TArg1, TArg2>(TArg1 arg1, TArg2 arg2)
        {
            _dispatchDepth++;
            try
            {
                for (var i = 0; i < _listExist.Count; i++)
                {
                    var d = _listExist[i];
                    if (d is Action<TArg1, TArg2> action)
                    {
                        action(arg1, arg2);
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <param name="arg3">事件参数3。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        /// <typeparam name="TArg3">事件参数3类型。</typeparam>
        public void Callback<TArg1, TArg2, TArg3>(TArg1 arg1, TArg2 arg2, TArg3 arg3)
        {
            _dispatchDepth++;
            try
            {
                for (var i = 0; i < _listExist.Count; i++)
                {
                    var d = _listExist[i];
                    if (d is Action<TArg1, TArg2, TArg3> action)
                    {
                        action(arg1, arg2, arg3);
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <param name="arg3">事件参数3。</param>
        /// <param name="arg4">事件参数4。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        /// <typeparam name="TArg3">事件参数3类型。</typeparam>
        /// <typeparam name="TArg4">事件参数4类型。</typeparam>
        public void Callback<TArg1, TArg2, TArg3, TArg4>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4)
        {
            _dispatchDepth++;
            try
            {
                for (var i = 0; i < _listExist.Count; i++)
                {
                    var d = _listExist[i];
                    if (d is Action<TArg1, TArg2, TArg3, TArg4> action)
                    {
                        action(arg1, arg2, arg3, arg4);
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <param name="arg3">事件参数3。</param>
        /// <param name="arg4">事件参数4。</param>
        /// <param name="arg5">事件参数5。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        /// <typeparam name="TArg3">事件参数3类型。</typeparam>
        /// <typeparam name="TArg4">事件参数4类型。</typeparam>
        /// <typeparam name="TArg5">事件参数5类型。</typeparam>
        public void Callback<TArg1, TArg2, TArg3, TArg4, TArg5>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5)
        {
            _dispatchDepth++;
            try
            {
                for (var i = 0; i < _listExist.Count; i++)
                {
                    var d = _listExist[i];
                    if (d is Action<TArg1, TArg2, TArg3, TArg4, TArg5> action)
                    {
                        action(arg1, arg2, arg3, arg4, arg5);
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <param name="arg3">事件参数3。</param>
        /// <param name="arg4">事件参数4。</param>
        /// <param name="arg5">事件参数5。</param>
        /// <param name="arg6">事件参数6。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        /// <typeparam name="TArg3">事件参数3类型。</typeparam>
        /// <typeparam name="TArg4">事件参数4类型。</typeparam>
        /// <typeparam name="TArg5">事件参数5类型。</typeparam>
        /// <typeparam name="TArg6">事件参数6类型。</typeparam>
        public void Callback<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6)
        {
            _dispatchDepth++;
            try
            {
                for (var i = 0; i < _listExist.Count; i++)
                {
                    var d = _listExist[i];
                    if (d is Action<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> action)
                    {
                        action(arg1, arg2, arg3, arg4, arg5, arg6);
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }
    }
}
