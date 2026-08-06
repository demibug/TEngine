using System;
using System.Collections.Generic;
using FairyGUI;

namespace GameFUI
{
    /// <summary>
    /// 加载操作账本：记录单次包 Acquire 操作新增的所有权资源，并支持按反向顺序原子回滚。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策9“包失败回滚使用本次操作账本”。
    /// 每次 Acquire 创建一个账本，按实际推进顺序记录本次新增的：
    /// <list type="bullet">
    /// <item>依赖租约（递归 Acquire 依赖包获得）</item>
    /// <item>外部资源 handle（并发预载获得）</item>
    /// <item>包注册（UIPackage.AddPackage 获得）</item>
    /// <item>描述 handle（加载描述资源获得）</item>
    /// </list>
    /// 任一步骤失败或取消时，调用 <see cref="Rollback"/> 按反向顺序释放本次新增所有权，
    /// 不触碰其他调用方已持有的共享记录（spec：失败和取消执行原子回滚）。
    ///
    /// 关键约束：
    /// - 只释放本次新增所有权：账本只记录“本次 Acquire 新增”的资源，不包含此前已就绪的共享依赖、
    ///   其他调用方持有的 lease 或已存在的 handle。回滚时只操作账本内条目，
    ///   保证已有共享依赖持有者继续正常使用（spec：依赖资源加载失败场景）。
    /// - 反向顺序：回滚顺序与记录顺序严格相反，对应正向加载的逆过程，
    ///   避免先释放上游再释放下游导致的悬空引用（design.md 决策9 最终释放顺序的失败版）。
    /// - 幂等：账本回滚后清空自身条目，重复调用 <see cref="Rollback"/> 不再产生任何释放动作。
    ///
    /// 边界约束：本类型不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// 只消费 FairyGUI 的 <see cref="UIPackage.RemovePackage"/> 与 GameFUI 内部的
    /// <see cref="IFUIAssetHandle"/>、<see cref="PackageLease"/>。
    /// </remarks>
    internal sealed class LoadOperationLedger
    {
        /// <summary>
        /// 本次新增的依赖租约，按递归 Acquire 获得顺序追加。
        /// </summary>
        /// <remarks>
        /// 回滚时按反向顺序释放，使后获取的依赖先归还。
        /// 只包含本次 Acquire 新增的租约；共享依赖被其他调用方持有的 lease 不在此列。
        /// </remarks>
        private readonly List<PackageLease> _dependencyLeases = new List<PackageLease>();

        /// <summary>
        /// 本次新增的外部资源 handle，按预载完成顺序追加。
        /// </summary>
        /// <remarks>
        /// 回滚时全部 Dispose，避免无所有者 handle（spec：资源所有权唯一且释放有序）。
        /// 只包含本次预载新增的 handle。
        /// </remarks>
        private readonly List<IFUIAssetHandle> _externalHandles = new List<IFUIAssetHandle>();

        /// <summary>
        /// 本次新增的描述 handle；描述加载成功后记录，未到达该阶段时为 null。
        /// </summary>
        private IFUIAssetHandle _descHandle;

        /// <summary>
        /// 本次新增注册的 <see cref="UIPackage"/>；AddPackage 成功后记录，未到达该阶段时为 null。
        /// </summary>
        private UIPackage _registeredPackage;

        /// <summary>
        /// 关联的包名，用于诊断与 RemovePackage 调用。
        /// </summary>
        private readonly string _packageName;

        /// <summary>
        /// 是否已完成回滚；true 表示账本已清空，后续 Rollback 不再产生动作。
        /// </summary>
        private bool _rolledBack;

        /// <summary>
        /// 构造加载操作账本。
        /// </summary>
        /// <param name="packageName">本次 Acquire 的目标包名，用于诊断与 RemovePackage。</param>
        /// <exception cref="ArgumentNullException">packageName 为 null 或空。</exception>
        public LoadOperationLedger(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                throw new ArgumentNullException(nameof(packageName));
            }

            _packageName = packageName;
        }

        /// <summary>
        /// 记录本次新增的依赖租约。
        /// </summary>
        /// <param name="lease">递归 Acquire 依赖包获得的租约，不得为 null。</param>
        /// <remarks>
        /// 在 <see cref="PackageLoader.AcquireDependenciesAsync"/> 每获得一个依赖 lease 后调用。
        /// 回滚时按反向顺序释放这些 lease，递减对应依赖包引用计数。
        /// </remarks>
        public void RecordDependencyLease(PackageLease lease)
        {
            if (lease == null)
            {
                return;
            }

            _dependencyLeases.Add(lease);
        }

        /// <summary>
        /// 记录本次新增的外部资源 handle。
        /// </summary>
        /// <param name="handle">并发预载获得的外部资源 handle，不得为 null。</param>
        /// <remarks>
        /// 在 <see cref="PackageLoader.PreloadExternalAssetsAsync"/> 写表成功后调用。
        /// 回滚时全部 Dispose。
        /// </remarks>
        public void RecordExternalHandle(IFUIAssetHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            _externalHandles.Add(handle);
        }

        /// <summary>
        /// 记录本次新增的描述 handle。
        /// </summary>
        /// <param name="handle">描述资源 handle，不得为 null。</param>
        /// <remarks>
        /// 在描述加载成功且通过校验后调用。回滚时 Dispose。
        /// 注意：若描述加载本身失败，加载器已在 catch 中 Dispose 该 handle，
        /// 不应再调用本方法记录；本方法只在描述成功用于后续 AddPackage 时记录。
        /// </remarks>
        public void RecordDescriptorHandle(IFUIAssetHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            _descHandle = handle;
        }

        /// <summary>
        /// 记录本次新增注册的 <see cref="UIPackage"/>。
        /// </summary>
        /// <param name="package">AddPackage 成功返回的 UIPackage，不得为 null。</param>
        /// <remarks>
        /// 在 AddPackage 成功后调用。回滚时通过 <see cref="UIPackage.RemovePackage"/> 移除注册。
        /// </remarks>
        public void RecordRegisteredPackage(UIPackage package)
        {
            if (package == null)
            {
                return;
            }

            _registeredPackage = package;
        }

        /// <summary>
        /// 按反向顺序回滚本次 Acquire 新增的全部所有权资源。
        /// </summary>
        /// <param name="reason">回滚原因（失败异常或取消），用于诊断日志。</param>
        /// <remarks>
        /// 反向顺序与正向加载对应（design.md 决策9）：
        /// <code>
        /// 正向：描述 handle -> AddPackage -> 外部资源预载 -> 依赖 Acquire
        /// 反向：依赖 lease 释放 -> 外部 handle Dispose -> RemovePackage -> 描述 handle Dispose
        /// </code>
        ///
        /// 该顺序保证：
        /// - 先释放依赖引用，使依赖包可在无上层持有时进入可回收状态；
        /// - 再 Dispose 外部资源 handle，此时 UIPackage 仍注册，避免 resolver 访问到已释放 handle
        ///   产生悬空资产（resolver 只在 CreateObject 时被 FairyGUI 调用，回滚阶段不构造对象，
        ///   但保持顺序一致可防止任何意外路径下的悬空访问）；
        /// - 再 RemovePackage，从 FairyGUI 全局注册表移除本次新增的包注册，
        ///   使重试 Acquire 可从干净状态开始（spec：回滚后 Failed 记录可安全重试）；
        /// - 最后 Dispose 描述 handle，对应最先加载的资源。
        ///
        /// 幂等：回滚完成后清空全部条目并标记 <see cref="_rolledBack"/>，
        /// 重复调用不再产生任何释放动作。
        ///
        /// 只释放本次新增所有权：账本只持有本次 Acquire 新增的资源引用，
        /// 不触碰其他调用方此前已持有的共享依赖 lease、共享 handle 或已就绪的 UIPackage。
        /// </remarks>
        public void Rollback(Exception reason)
        {
            // 幂等保护：已回滚则直接返回。
            if (_rolledBack)
            {
                return;
            }

            _rolledBack = true;

            string reasonText = reason?.Message ?? reason?.GetType().Name ?? "未知原因";

            // 1. 反向释放本次新增的依赖租约。
            //    只释放账本中的 lease；其他调用方持有的同一依赖包 lease 不受影响
            //    （spec：已有共享依赖持有者继续正常使用）。
            RollbackDependencyLeases();

            // 2. Dispose 本次新增的外部资源 handle。
            //    handle 失败或释放均不向上抛出，保证回滚链路完整执行。
            RollbackExternalHandles();

            // 3. 移除本次新增注册的 UIPackage。
            //    从 FairyGUI 全局注册表移除，使重试可从干净状态开始。
            RollbackRegisteredPackage(reasonText);

            // 4. Dispose 本次新增的描述 handle。
            //    对应正向流程最先加载的资源，放最后释放。
            RollbackDescriptorHandle();

            // 清空账本条目，避免重复回滚。
            _dependencyLeases.Clear();
            _externalHandles.Clear();
            _descHandle = null;
            _registeredPackage = null;
        }

        /// <summary>
        /// 反向释放本次新增的依赖租约。
        /// </summary>
        /// <remarks>
        /// 只释放 <see cref="_dependencyLeases"/> 中的 lease，不触碰其他调用方持有的 lease。
        /// 释放后从关联 PackageRecord.DependencyLeases 中移除，避免最终释放阶段重复释放。
        /// </remarks>
        private void RollbackDependencyLeases()
        {
            if (_dependencyLeases.Count == 0)
            {
                return;
            }

            // 反向释放：后获取的依赖先归还。
            for (int i = _dependencyLeases.Count - 1; i >= 0; i--)
            {
                PackageLease lease = _dependencyLeases[i];
                if (lease == null || lease.IsReleased)
                {
                    continue;
                }

                try
                {
                    lease.Release();
                }
                catch (Exception)
                {
                    // 租约释放异常不中断回滚链路；继续释放其余租约。
                    // 诊断信息由上层 PackageLoader.OnAcquireFailed 记录。
                }
            }
        }

        /// <summary>
        /// Dispose 本次新增的外部资源 handle。
        /// </summary>
        private void RollbackExternalHandles()
        {
            if (_externalHandles.Count == 0)
            {
                return;
            }

            // 反向 Dispose：与预载写入顺序相反。
            for (int i = _externalHandles.Count - 1; i >= 0; i--)
            {
                IFUIAssetHandle handle = _externalHandles[i];
                if (handle == null)
                {
                    continue;
                }

                try
                {
                    handle.Dispose();
                }
                catch (Exception)
                {
                    // handle Dispose 异常不中断回滚链路。
                }
            }
        }

        /// <summary>
        /// 移除本次新增注册的 UIPackage。
        /// </summary>
        /// <param name="reasonText">回滚原因文本，用于诊断。</param>
        /// <remarks>
        /// 通过 <see cref="UIPackage.RemovePackage(string)"/> 按包名移除。
        /// 若包已被其他路径移除（理论上不应发生），捕获异常避免中断回滚。
        /// </remarks>
        private void RollbackRegisteredPackage(string reasonText)
        {
            if (_registeredPackage == null)
            {
                return;
            }

            try
            {
                // 按包名移除：RemovePackage 接受 id 或 name，包名作为逻辑身份可直接使用。
                // 移除后 FairyGUI 全局 _packageInstByName/_packageList 不再持有本次新增的注册。
                UIPackage.RemovePackage(_packageName);
            }
            catch (Exception)
            {
                // RemovePackage 抛出（如包已被移除）不中断回滚链路。
                // 回滚目标是从 FairyGUI 注册表移除本次新增项；若已不在则目标已达成。
            }
        }

        /// <summary>
        /// Dispose 本次新增的描述 handle。
        /// </summary>
        private void RollbackDescriptorHandle()
        {
            if (_descHandle == null)
            {
                return;
            }

            try
            {
                _descHandle.Dispose();
            }
            catch (Exception)
            {
                // 描述 handle Dispose 异常不中断回滚链路。
            }
        }
    }
}
