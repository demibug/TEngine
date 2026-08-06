using System;
using System.Collections.Generic;

namespace GameFUI
{
    /// <summary>
    /// 受管理 FairyGUI Window/Widget 的显式绑定注册表，是运行时创建受管理对象的唯一注册来源。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策2，显式 Descriptor Registry 是唯一运行时注册来源。
    /// <list type="bullet">
    /// <item>每个受管理类型由 owner 在初始化阶段显式注册；每个 FairyGUI Package 必须指定唯一 owner 类型。</item>
    /// <item>单个 owner 内的绑定顺序固定为：本包生成 Binder、最终 Widget、最终 Window。
    /// 后注册的最终 creator 覆盖生成类型，使创建结果为最末端业务类型而非生成基类
    /// （spec fairygui-window-runtime：业务类型覆盖生成类型）。</item>
    /// <item>注册阶段只完成绑定和同步描述写入，不得创建或显示 FairyGUI 对象。</item>
    /// <item>所有 owner 注册完成后，由装配方显式 <see cref="Freeze"/> 冻结注册表；
    /// 冻结后新增或冲突注册直接抛 <see cref="FUIException"/>
    /// （spec：重复或冲突注册——同一窗口类型或组件 URL 被不兼容的描述重复注册时，
    /// 系统 SHALL 在首次创建前报告明确错误并阻止进入半注册状态）。</item>
    /// </list>
    ///
    /// 唯一性校验覆盖三个维度：
    /// <list type="bullet">
    /// <item>类型唯一性：同一 <see cref="FUIDescriptor.TargetType"/> 不得被重复注册，
    /// 除非是同一 URL + 同一 owner 的覆盖（按绑定顺序规则）。</item>
    /// <item>URL 唯一性：同一组件 URL 不得被不兼容描述重复注册；
    /// 同一 owner 对同一 URL 的再注册视为覆盖，后注册者生效。</item>
    /// <item>owner/Package 唯一性：每个 Package 只能有一个 owner 类型，
    /// 不同 owner 重复拥有同一 Package 直接报错。</item>
    /// </list>
    ///
    /// 覆盖语义：当且仅当 URL 与 owner 均相同，后注册的描述覆盖先注册的描述，
    /// 对应 Binder → 最终 Widget → 最终 Window 的绑定顺序。TargetType 可在覆盖中变化
    /// （生成类型 → 最终业务类型），这正是覆盖的目的。
    ///
    /// 线程安全：注册与冻结发生在装配阶段（单线程），运行期只读查询；
    /// 不额外加锁，查询使用的字典在冻结后不再变更。
    ///
    /// 边界约束：本类型不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// </remarks>
    public sealed class FUIBindingRegistry
    {
        /// <summary>
        /// 按 URL 索引的当前生效描述表。覆盖时旧描述被替换，查询始终返回最末端业务类型描述。
        /// </summary>
        private readonly Dictionary<string, FUIDescriptor> _descriptorsByUrl = new Dictionary<string, FUIDescriptor>();

        /// <summary>
        /// 按 TargetType 索引的当前生效描述表。覆盖时旧类型映射被替换为新的 TargetType 映射。
        /// </summary>
        private readonly Dictionary<Type, FUIDescriptor> _descriptorsByType = new Dictionary<Type, FUIDescriptor>();

        /// <summary>
        /// 按 PackageName 索引的 owner 映射，用于校验每个 Package 只有一个 owner 类型。
        /// </summary>
        private readonly Dictionary<string, Type> _packageOwners = new Dictionary<string, Type>();

        /// <summary>
        /// 获取注册表是否已被冻结。冻结后任何新增注册直接抛 <see cref="FUIException"/>。
        /// </summary>
        public bool IsFrozen { get; private set; }

        /// <summary>
        /// 获取注册表是否处于活动状态，即已冻结且尚未被 Shutdown 清空。
        /// </summary>
        /// <remarks>
        /// 全局 <c>UIObjectFactory</c> 的 creator 只捕获 URL 并查询当前活动 Registry（任务 3.5）。
        /// 装配阶段（未冻结）查询应视为未活动，避免在半注册状态下创建对象；
        /// Shutdown 清空本地 Registry 后也应视为未活动，使迟到的全局 creator 调用明确失败
        /// （spec：Shutdown 后全局 creator 被调用——creator SHALL 因 Registry 非活动而失败并给出明确诊断）。
        /// </remarks>
        public bool IsActive => IsFrozen && !_isShutdown;

        /// <summary>
        /// 标记注册表是否已被 Shutdown 清空。Shutdown 后 IsActive 返回 false，
        /// 迟到的全局 creator 查询将因非活动而失败。
        /// </summary>
        private bool _isShutdown;

        /// <summary>
        /// 注册一个窗口或 Widget 描述。
        /// </summary>
        /// <remarks>
        /// 注册阶段只能完成绑定和同步描述写入，不得创建或显示 FairyGUI 对象。
        /// 绑定顺序固定为：本包生成 Binder、最终 Widget、最终 Window；
        /// 同一 URL + 同一 owner 的再注册视为覆盖，后注册者生效，使创建结果为最末端业务类型。
        /// 冲突或冻结后注册直接抛 <see cref="FUIException"/>，包含明确的冲突信息（类型、URL、owner 等）。
        /// </remarks>
        /// <param name="descriptor">受管理对象描述，字段不可变。</param>
        /// <exception cref="FUIException">描述字段非法、与已注册描述冲突或在冻结后注册。</exception>
        public void Register(FUIDescriptor descriptor)
        {
            // 冻结后任何新增注册直接报错。
            if (IsFrozen)
            {
                throw new FUIException(
                    $"注册表已冻结，禁止新增注册：url={descriptor.URL}, targetType={descriptor.TargetType?.FullName}, owner={descriptor.OwnerType?.FullName}。");
            }

            ValidateDescriptor(descriptor);

            string url = descriptor.URL;
            string packageName = descriptor.PackageName;
            Type targetType = descriptor.TargetType;
            Type ownerType = descriptor.OwnerType;

            // 校验 owner/Package 唯一性：每个 Package 只能有一个 owner 类型。
            if (_packageOwners.TryGetValue(packageName, out Type existingOwner))
            {
                if (existingOwner != ownerType)
                {
                    throw new FUIException(
                        $"Package owner 冲突：Package '{packageName}' 已由 owner '{existingOwner.FullName}' 注册，" +
                        $"不得再由 owner '{ownerType.FullName}' 注册。每个 Package 只能有一个 owner 类型。");
                }
            }
            else
            {
                _packageOwners[packageName] = ownerType;
            }

            bool isOverride = false;

            // 校验 URL 唯一性：同一 URL 已存在时，仅允许同一 owner 的覆盖。
            if (_descriptorsByUrl.TryGetValue(url, out FUIDescriptor existingByUrl))
            {
                if (existingByUrl.OwnerType != ownerType)
                {
                    // 同一 URL 被不同 owner 注册：违反 Package owner 唯一性，属不兼容冲突。
                    throw new FUIException(
                        $"URL 注册冲突：url='{url}' 已由 owner '{existingByUrl.OwnerType.FullName}' 注册" +
                        $"（TargetType={existingByUrl.TargetType?.FullName}），" +
                        $"不得再由 owner '{ownerType.FullName}' 注册（TargetType={targetType?.FullName}）。" +
                        $"同一组件 URL 不得被不兼容描述重复注册。");
                }

                // 同一 URL + 同一 owner：合法覆盖，后注册者生效。
                isOverride = true;
            }

            // 校验类型唯一性：同一 TargetType 已存在时，仅允许同一 URL + 同一 owner 的覆盖。
            if (_descriptorsByType.TryGetValue(targetType, out FUIDescriptor existingByType))
            {
                if (existingByType.URL != url || existingByType.OwnerType != ownerType)
                {
                    throw new FUIException(
                        $"窗口/Widget 类型注册冲突：TargetType '{targetType.FullName}' 已注册为" +
                        $" url='{existingByType.URL}'（owner={existingByType.OwnerType.FullName}），" +
                        $"不得再注册为 url='{url}'（owner={ownerType.FullName}）。" +
                        $"同一类型不得被不兼容描述重复注册。");
                }

                // 同一 TargetType + 同一 URL + 同一 owner：与上述覆盖一致，合法。
                isOverride = true;
            }

            // 执行覆盖或新增。
            // 覆盖时若旧描述的 TargetType 与新描述不同（生成类型 → 最终业务类型），
            // 需移除旧 TargetType 映射，避免悬挂的旧类型条目。
            if (isOverride && existingByUrl.TargetType != targetType)
            {
                _descriptorsByType.Remove(existingByUrl.TargetType);
            }

            _descriptorsByUrl[url] = descriptor;
            _descriptorsByType[targetType] = descriptor;
        }

        /// <summary>
        /// 冻结注册表。冻结后任何新增或冲突注册直接抛 <see cref="FUIException"/>。
        /// </summary>
        /// <remarks>
        /// 所有 owner 注册完成后，由装配方显式调用本方法冻结注册表（design.md 决策2）。
        /// 冻结后才允许运行期创建查询（<see cref="IsActive"/> 为 true），
        /// 避免在半注册状态下创建受管理对象。
        /// 重复冻结是幂等的，不会抛异常。
        /// </remarks>
        public void Freeze()
        {
            IsFrozen = true;
        }

        /// <summary>
        /// 按组件 URL 查询当前生效描述。
        /// </summary>
        /// <param name="url">组件 URL，格式如 <c>ui://UIBattle/BattleStartPanel</c>。</param>
        /// <param name="descriptor">查到时输出当前生效描述，始终为最末端业务类型描述。</param>
        /// <returns>存在则返回 true，否则 false。</returns>
        public bool TryGetDescriptor(string url, out FUIDescriptor descriptor)
        {
            return _descriptorsByUrl.TryGetValue(url, out descriptor);
        }

        /// <summary>
        /// 按 TargetType 查询当前生效描述。
        /// </summary>
        /// <param name="targetType">最终业务类型。</param>
        /// <param name="descriptor">查到时输出当前生效描述。</param>
        /// <returns>存在则返回 true，否则 false。</returns>
        public bool TryGetDescriptor(Type targetType, out FUIDescriptor descriptor)
        {
            return _descriptorsByType.TryGetValue(targetType, out descriptor);
        }

        /// <summary>
        /// 返回当前已注册的全部组件 URL 的只读快照。
        /// </summary>
        /// <returns>已注册 URL 的只读集合；调用时复制一份，避免调用方修改影响注册表内部状态。</returns>
        /// <remarks>
        /// <para>
        /// 本方法为只读查询，不改变现有 <see cref="Register"/>、<see cref="Freeze"/>、<see cref="Shutdown"/>
        /// 等方法的逻辑或状态。它供 <c>FUIModule.FreezeBindings</c> 在冻结后获取全部已注册 URL，
        /// 用于安装全局无状态 creator（任务 5.1 返工修复）。
        /// </para>
        /// <para>
        /// 之前 <c>FUIModule</c> 在 <c>RegisterDescriptor</c> 中自行维护一份 URL 列表，
        /// 但 design.md 决策10 的装配流程允许 owner 直接通过 <see cref="Register"/> 注册
        /// （如 <c>TestFUIOwner.RegisterUIBattle</c>），会绕过 <c>RegisterDescriptor</c>，
        /// 导致自行维护的列表为空、creator 不被安装。本方法使 <c>FreezeBindings</c> 直接从
        /// 注册表获取已注册 URL，不再依赖注册路径，保证装配流程一致性。
        /// </para>
        /// <para>
        /// 冻结前后均可调用；Shutdown 后返回空集合。返回的集合是当前键的快照副本，
        /// 调用方可安全遍历，不会因后续注册表变更产生并发修改问题。
        /// </para>
        /// </remarks>
        public IReadOnlyCollection<string> GetRegisteredUrls()
        {
            // 复制一份当前已注册 URL 的快照，避免调用方遍历时注册表内部字典被修改。
            // 返回 IReadOnlyCollection<string> 而非内部字典引用，保证只读契约。
            string[] snapshot = new string[_descriptorsByUrl.Count];
            _descriptorsByUrl.Keys.CopyTo(snapshot, 0);
            return snapshot;
        }

        /// <summary>
        /// 清空注册表并标记为已 Shutdown，使 <see cref="IsActive"/> 返回 false。
        /// </summary>
        /// <remarks>
        /// 模块退出时由 FUIModule 调用，清理本地描述、owner 和活动 Registry
        /// （spec：模块退出完整清理——清理本地描述、owner、活动 Registry 和静态模块缓存）。
        /// 清空后迟到的全局 creator 查询将因 <see cref="IsActive"/> 为 false 而明确失败，
        /// 不得创建持有旧业务依赖的窗口。
        /// 本方法不调用全局 <c>UIObjectFactory.Clear()</c>，以免清除其他 FairyGUI 扩展。
        /// </remarks>
        public void Shutdown()
        {
            _descriptorsByUrl.Clear();
            _descriptorsByType.Clear();
            _packageOwners.Clear();
            _isShutdown = true;
        }

        /// <summary>
        /// 校验描述字段合法性，确保 URL、PackageName、OwnerType 和 TargetType 非空。
        /// </summary>
        /// <param name="descriptor">待校验描述。</param>
        /// <exception cref="FUIException">字段非法时抛出。</exception>
        private static void ValidateDescriptor(FUIDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(descriptor.URL))
            {
                throw new FUIException("注册失败：描述的 URL 不能为空。");
            }

            if (string.IsNullOrEmpty(descriptor.PackageName))
            {
                throw new FUIException($"注册失败：描述（url='{descriptor.URL}'）的 PackageName 不能为空。");
            }

            if (descriptor.OwnerType == null)
            {
                throw new FUIException($"注册失败：描述（url='{descriptor.URL}'）的 OwnerType 不能为空。");
            }

            if (descriptor.TargetType == null)
            {
                throw new FUIException($"注册失败：描述（url='{descriptor.URL}'）的 TargetType 不能为空。");
            }
        }
    }
}
