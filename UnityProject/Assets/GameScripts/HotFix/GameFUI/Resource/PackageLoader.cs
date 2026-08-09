using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using TEngine;
using UnityEngine;

namespace GameFUI
{
    /// <summary>
    /// 包加载器：负责描述文件 handle 加载、<see cref="UIPackage.AddPackage"/>、基于已加载 handle 表的同步资源解析器，
    /// 以及包内外部资源（贴图、音频、二进制）的并发预载。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策8“包加载采用异步预载、同步解析”。
    /// Acquire 流程中，本加载器承担以下步骤：
    /// 1. 通过 <see cref="IFUIResourceProvider"/> 异步加载 <c>{PackageName}_fui</c> 描述资源（TextAsset），取出描述字节；
    /// 2. 用描述字节调用 <see cref="UIPackage.AddPackage(byte[], string, LoadResource)"/>，
    ///    传入 <see cref="PackageName"/> 作为 assetNamePrefix 与同步资源解析器回调；
    /// 3. 将返回的 <see cref="UIPackage"/> 写入 <see cref="PackageRecord.Package"/>；
    /// 4. 将描述 handle 写入 <see cref="PackageRecord.DescHandle"/>（修复 4.5 成功路径未写入的泄漏风险）；
    /// 5. 枚举 <see cref="UIPackage.GetItems"/> 中带外部文件的外部资源项（Atlas/Sound/Misc），
    ///    通过 <see cref="IFUIResourceProvider"/> 并发加载 AssetHandle，全部写入 <see cref="PackageRecord.AssetHandles"/>。
    ///
    /// 关键解耦：AddPackage 只解析描述，不在这一阶段构造窗口（FairyGUI 的 LoadPackage 只构建 _items 列表，
    /// 不触发 _loadFunc）。同步 resolver 可以先安装，在 PackageRecord Ready 后才允许 CreateObject；
    /// resolver 只从 <see cref="PackageRecord.AssetHandles"/> 已完成的 handle 表返回资产，
    /// 并设置 <see cref="DestroyMethod.None"/>（不由 FairyGUI 销毁）。
    /// 这使 Show 的资源就绪屏障与 FairyGUI 的懒加载回调解耦。
    ///
    /// 资源命名：FairyGUI 在 LoadPackage 中对 Atlas/Sound/Misc 项执行
    /// <c>pi.file = assetNamePrefix + "_" + pi.file</c>（assetNamePrefix 即 <see cref="PackageName"/>），
    /// 随后在 LoadAtlas/LoadSound/LoadBinary 中计算 <c>fileName = Path.GetFileNameWithoutExtension(item.file)</c>
    /// 作为传入 resolver 的 name 参数。因此 resolver 收到的 name 等价于
    /// <c>Path.GetFileNameWithoutExtension(item.file)</c>，与 spec“外部项按
    /// Path.GetFileNameWithoutExtension(item.file) 映射规范 location”一致，直接用其作为 handle 表的 key。
    /// 预载阶段对每个外部项以 <c>Path.GetFileNameWithoutExtension(item.file)</c> 作为 location 加载 handle，
    /// 确保与 resolver 查询 key 完全对齐。
    ///
    /// 外部资源类型映射（依据 FairyGUI GetItemAsset/LoadAtlas/LoadSound/LoadBinary）：
    /// - <see cref="PackageItemType.Atlas"/> → <see cref="Texture"/>（图集纹理）；
    /// - <see cref="PackageItemType.Sound"/> → <see cref="AudioClip"/>（音频）；
    /// - <see cref="PackageItemType.Misc"/> → <see cref="TextAsset"/>（二进制，如字体描述）。
    /// Image/MovieClip/Font/Component 项无独立外部文件（引用图集或仅含 rawData），不参与预载。
    /// Spine/DragoneBones 的 file 在 LoadPackage 中按 assetPath 拼接（目录前缀而非包名前缀），
    /// 其加载语义与本 change 的 YooAsset location 规则不一致，首版不预载，留给后续 change 处理。
    ///
    /// 边界约束：本类型不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// 不修改 FairyGUI 源码，只消费其公开 AddPackage/LoadResource/DestroyMethod/GetItems/PackageItem API。
    /// </remarks>
    internal static class PackageLoader
    {
        /// <summary>
        /// 描述资源的 location 后缀，与 spec“使用 {PackageName}_fui 作为包描述资源 location”一致。
        /// </summary>
        private const string DescLocationSuffix = "_fui";

        /// <summary>
        /// 包记录注册表，按逻辑包名索引全部已创建的 <see cref="PackageRecord"/>。
        /// </summary>
        /// <remarks>
        /// 设计依据：design.md 决策8“查找/创建 PackageRecord，合并同包任务”。
        /// 本注册表是 Acquire 流程查找既有记录、合并同包并发加载任务的唯一入口。
        /// 同一逻辑包名在全模块内只存在一个 <see cref="PackageRecord"/>，使并发调用方能够通过
        /// <see cref="PackageRecord.SharedLoadTask"/> 共享同一次加载，并使多个上层包共享同一依赖时
        /// 只加载一次该依赖（spec：两个包共享依赖 → 依赖只加载一次）。
        ///
        /// 生命周期：注册表由本加载器在 Acquire 时按需创建并填充，模块 Shutdown 或测试重置时通过
        /// <see cref="ClearRegistry"/> 清空。最终卸载判定与 Delayed 卸载策略由后续 4.9/6.x 任务在上层
        /// PackageManager 中实现；本注册表只承担“按名查找记录 + 合并任务”的最窄职责。
        ///
        /// 并发访问：Acquire 流程在 PackageManager 单线程驱动下访问本注册表，不额外加锁；
        /// 同包并发任务合并通过 <see cref="PackageRecord.SharedLoadTask"/>（UniTaskCompletionSource）实现，
        /// 后续调用方 await 同一任务即可，无需对注册表本身加锁。
        ///
        /// 边界约束：本注册表不引用 GameLogic/GamePlay/GameBattle。
        /// </remarks>
        private static readonly Dictionary<string, PackageRecord> _records = new Dictionary<string, PackageRecord>();

        /// <summary>
        /// 当前模块的包卸载策略，由 <see cref="Configure"/> 在模块装配阶段设置（6.4 实现）。
        /// </summary>
        /// <remarks>
        /// 设计依据：design.md 决策8“首个实现允许 KeepUntilShutdown 卸载策略，但从第一天就维护 lease
        /// 和 handle 所有权；完成共享依赖、缓存和失败测试后再启用 Delayed 策略”。
        /// 默认 <see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>，保持与 4.9 已实现行为一致；
        /// 选择 <see cref="FUIPackageUnloadPolicy.Delayed"/> 时，引用计数归零后启动延迟卸载任务
        /// （<see cref="ScheduleDelayedUnload"/>），延迟期到期且通过零引用检查后执行最终释放。
        ///
        /// 配置时机：由 <see cref="FUIModule"/> 构造阶段调用 <see cref="Configure"/> 设置
        /// （5.x 集成任务负责接线；当前阶段为后续集成预留入口）。
        /// 未配置时使用默认值 KeepUntilShutdown，不影响 4.9 已验证的 KeepUntilShutdown 路径。
        ///
        /// 线程安全：以 int 存储（<see cref="System.Threading.Volatile"/> 无枚举重载），
        /// 通过 <see cref="UnloadPolicy"/> 属性转换为 <see cref="FUIPackageUnloadPolicy"/> 返回；
        /// 运行期不修改（装配阶段一次性设置），读取侧无需加锁。
        /// </remarks>
        private static int _unloadPolicy = (int)FUIPackageUnloadPolicy.KeepUntilShutdown;

        /// <summary>
        /// 当前模块的包卸载策略（只读快照）。
        /// </summary>
        internal static FUIPackageUnloadPolicy UnloadPolicy
        {
            get { return (FUIPackageUnloadPolicy)System.Threading.Volatile.Read(ref _unloadPolicy); }
        }

        /// <summary>
        /// 全局窗口状态查询接口，由 <see cref="FUIModule"/> 在装配阶段通过
        /// <see cref="SetWindowStateProvider"/> 注册（6.5 实现）。
        /// </summary>
        /// <remarks>
        /// 设计依据：design.md 决策9“最终卸载必须……没有存活或缓存窗口、没有创建任务、没有上层依赖”。
        /// 6.4 阶段 <see cref="IFUIWindowStateProvider"/> 作为参数在 <see cref="UnloadPackage"/>/
        /// <see cref="UnloadAllForShutdown"/> 中传递，但 <see cref="ScheduleDelayedUnload"/> 无法获得
        /// 该参数（fire-and-forget 异步任务），故 6.4 在延迟卸载路径中以 null 传入，留下窗口约束缺口。
        /// 6.5 在此增加全局注册入口，使延迟卸载任务与强制 Shutdown 路径都能查询窗口状态。
        ///
        /// 注册时机：由 <see cref="FUIModule"/> 在 <see cref="FUIModule.FreezeBindings"/> 阶段调用
        /// <see cref="SetWindowStateProvider"/> 注册自身（FUIModule 实现 IFUIWindowStateProvider）。
        /// 未注册时（值为 null）视为无窗口约束，与 6.4 行为一致，保证 4.9 单包/Shutdown 测试不受影响。
        /// 模块 Shutdown 时通过 <see cref="ClearWindowStateProvider"/> 清空，避免跨模块残留。
        ///
        /// 线程安全：使用 <see cref="System.Threading.Volatile"/> 读写。注册在装配阶段一次性完成，
        /// 运行期不修改；读取侧（CanUnload/ScheduleDelayedUnload）无需加锁。
        ///
        /// 边界约束：本字段不反向依赖 GameLogic/GamePlay/GameBattle；<see cref="IFUIWindowStateProvider"/>
        /// 接口定义于 GameFUI 程序集内（PackageRecord.cs）。
        /// </remarks>
        private static IFUIWindowStateProvider _windowStateProvider;

        /// <summary>
        /// 注册全局窗口状态查询接口，供包卸载前置检查（含 Delayed 延迟卸载路径）使用（6.5 实现）。
        /// </summary>
        /// <param name="provider">窗口状态查询接口；通常传入实现 <see cref="IFUIWindowStateProvider"/> 的 <see cref="FUIModule"/> 实例。</param>
        /// <remarks>
        /// 由 <see cref="FUIModule.FreezeBindings"/> 在装配阶段调用，注册自身为全局窗口状态提供者。
        /// 注册后，<see cref="CanUnload"/> 与 <see cref="ScheduleDelayedUnload"/> 在延迟卸载路径中
        /// 将自动查询窗口状态，使存活窗口、缓存窗口、创建任务与上层依赖统一纳入包卸载前置检查
        /// （spec：包租约控制缓存和卸载——只有不存在存活或缓存对象、创建任务、上层依赖和待完成资源操作时，包才可卸载）。
        /// 传 null 等价于 <see cref="ClearWindowStateProvider"/>，用于模块 Shutdown 清理。
        /// </remarks>
        internal static void SetWindowStateProvider(IFUIWindowStateProvider provider)
        {
            System.Threading.Volatile.Write(ref _windowStateProvider, provider);
        }

        /// <summary>
        /// 清空全局窗口状态查询接口，供模块 Shutdown 清理使用（6.5 实现）。
        /// </summary>
        /// <remarks>
        /// 由 <see cref="FUIModule.Shutdown"/> 在包回收前/后调用，避免跨模块残留。
        /// 清空后 <see cref="CanUnload"/> 与 <see cref="ScheduleDelayedUnload"/> 视为无窗口约束，
        /// 与 6.4 行为一致，保证强制 Shutdown 路径不被窗口状态拦截（Shutdown 本就强制回收全部包）。
        /// </remarks>
        internal static void ClearWindowStateProvider()
        {
            System.Threading.Volatile.Write(ref _windowStateProvider, null);
        }

        /// <summary>
        /// 获取当前已注册的全局窗口状态查询接口（仅供诊断与测试）。
        /// </summary>
        internal static IFUIWindowStateProvider WindowStateProvider
        {
            get { return System.Threading.Volatile.Read(ref _windowStateProvider); }
        }

        /// <summary>
        /// Delayed 策略下零引用后的延迟卸载时间（毫秒），由 <see cref="Configure"/> 设置（6.4 实现）。
        /// </summary>
        /// <remarks>
        /// 对应 <see cref="FUIOptions.UnloadDelaySeconds"/>，在 <see cref="Configure"/> 中转换为毫秒存储。
        /// 仅在 <see cref="UnloadPolicy"/> 为 <see cref="FUIPackageUnloadPolicy.Delayed"/> 时生效。
        /// 延迟期内重新 Acquire 通过递增 <see cref="PackageRecord.PendingUnloadVersion"/> 取消旧卸载任务
        /// （design.md 决策8，spec：延迟卸载期间重新 Acquire）。
        ///
        /// 线程安全：使用 <see cref="System.Threading.Volatile"/> 读写。
        /// </remarks>
        private static int _unloadDelayMs = (int)(5f * 1000f);

        /// <summary>
        /// 获取当前延迟卸载时间（毫秒，仅供诊断与测试）。
        /// </summary>
        internal static int UnloadDelayMs
        {
            get { return System.Threading.Volatile.Read(ref _unloadDelayMs); }
        }

        /// <summary>
        /// 配置包卸载策略与延迟时间，由 <see cref="FUIModule"/> 在装配阶段调用（6.4 实现）。
        /// </summary>
        /// <param name="policy">包卸载策略。</param>
        /// <param name="unloadDelaySeconds">Delayed 策略下零引用后的延迟卸载时间（秒）。</param>
        /// <remarks>
        /// 本方法供 5.x 集成任务在 <see cref="FUIModule"/> 构造阶段从 <see cref="FUIOptions"/> 读取并传入。
        /// 当前阶段（6.4）实现机制与入口，不修改 <see cref="FUIModule"/>（5.2 同批次并行约束）。
        /// 默认值（KeepUntilShutdown / 5s）保证未调用本方法时行为与 4.9 一致。
        ///
        /// 边界约束：本方法只设置静态配置，不触发任何加载或卸载操作。
        /// </remarks>
        internal static void Configure(FUIPackageUnloadPolicy policy, float unloadDelaySeconds)
        {
            // 以 int 存储：Volatile 无枚举重载，策略枚举转换为 int 存储与读写。
            System.Threading.Volatile.Write(ref _unloadPolicy, (int)policy);
            // 延迟时间转换为毫秒存储，与 UniTask.Delay 的 int 毫秒参数对齐。
            // 负数或零视为立即卸载（最小延迟 1ms 避免零延迟风暴）。
            int ms = (int)(unloadDelaySeconds * 1000f);
            if (ms < 1)
            {
                ms = 1;
            }
            System.Threading.Volatile.Write(ref _unloadDelayMs, ms);
        }

        /// <summary>
        /// 静态构造：安装引用计数归零回调，使 Delayed 策略能在 lease 释放归零时自动触发延迟卸载评估。
        /// </summary>
        /// <remarks>
        /// 6.4 实现：<see cref="PackageRecord.OnReferenceCountReachedZero"/> 是
        /// <see cref="PackageRecord.TryReleaseReference"/> 检测到归零后通知本加载器的唯一入口。
        /// 静态构造保证在任何 <see cref="AcquireAsync"/> / <see cref="PackageLease"/> 创建之前完成回调安装，
        /// 避免遗漏触发。回调内部根据 <see cref="UnloadPolicy"/> 分流：
        /// - KeepUntilShutdown：直接返回，包保留到 Shutdown；
        /// - Delayed：调用 <see cref="ScheduleDelayedUnload"/> 调度延迟卸载任务。
        /// </remarks>
        static PackageLoader()
        {
            PackageRecord.OnReferenceCountReachedZero = HandleReferenceCountReachedZero;
        }

        /// <summary>
        /// 按逻辑包名查找已存在的 <see cref="PackageRecord"/>；不存在时返回 null。
        /// </summary>
        /// <param name="packageName">逻辑包名。</param>
        /// <returns>已存在的包记录，或 null。</returns>
        /// <remarks>供 Acquire 流程与后续 PackageManager 查询引用计数、卸载判定使用。</remarks>
        internal static PackageRecord FindRecord(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            _records.TryGetValue(packageName, out PackageRecord record);
            return record;
        }

        /// <summary>
        /// 清空包记录注册表，供模块 Shutdown 或测试重置使用。
        /// </summary>
        /// <remarks>
        /// 本方法只清空注册表索引，不 Dispose 记录持有的 handle 与 UIPackage。
        /// 最终资源回收（RemovePackage、Dispose handle、释放依赖租约）由
        /// <see cref="UnloadPackage"/> / <see cref="UnloadAllForShutdown"/> 统一负责（4.9）。
        /// 调用方应在此之前已完成各记录的最终释放（或直接调用
        /// <see cref="UnloadAllForShutdown"/>，其内部已清空注册表）。
        /// </remarks>
        internal static void ClearRegistry()
        {
            _records.Clear();
        }

        /// <summary>
        /// 异步加载描述文件、注册 <see cref="UIPackage"/>、写入描述 handle，并并发预载包内全部外部资源，
        /// 确保返回时窗口首屏可能使用的外部资源已全部 Ready（spec：Acquire 成功代表资源已经可用于首屏构造）。
        /// </summary>
        /// <param name="record">目标包记录，其 <see cref="PackageRecord.PackageName"/> 决定描述 location 与 assetNamePrefix。</param>
        /// <param name="provider">资源 provider，用于异步加载描述资源与外部资源。</param>
        /// <param name="ledger">本次 Acquire 操作账本，本方法在推进每个成功步骤后向其登记新增所有权（4.8）。</param>
        /// <param name="cancellationToken">取消令牌；响应取消时抛出 <see cref="OperationCanceledException"/>。</param>
        /// <returns>本次加载得到的 <see cref="UIPackage"/>（已写入 record，外部资源已全部预载完成）；失败时抛出 <see cref="FUIException"/>。</returns>
        /// <remarks>
        /// 本方法完成描述加载、包注册、描述 handle 写入与外部资源并发预载，是 Acquire 流程的核心实现。
        /// 成功返回时，<see cref="PackageRecord.AssetHandles"/> 已包含全部外部资源 handle 且均处于完成状态，
        /// resolver 在后续 CreateObject 期间可同步取得真实资产，无需依赖异步占位回填。
        ///
        /// 失败回滚约定（design.md 决策9“包失败回滚使用本次操作账本”）：
        /// 本方法在推进每个成功步骤后，将新增所有权资源登记到 <paramref name="ledger"/>，
        /// 由 <see cref="AcquireAsync"/> 的失败处理统一调用 <see cref="LoadOperationLedger.Rollback"/>
        /// 按反向顺序原子回滚。具体登记点：
        /// - 描述 handle 校验成功后 → <see cref="LoadOperationLedger.RecordDescriptorHandle"/>；
        /// - AddPackage 成功后 → <see cref="LoadOperationLedger.RecordRegisteredPackage"/>；
        /// - 每个外部资源 handle 写表成功后 → <see cref="LoadOperationLedger.RecordExternalHandle"/>。
        ///
        /// 描述加载/AddPackage 自身失败时，描述 handle 尚未登记到账本（登记点在校验通过之后），
        /// 加载器在 catch 中直接 Dispose 该 handle，不向 record 写入任何句柄，
        /// 避免账本与 record 持有已释放句柄。外部资源预载失败时，已写表的 handle 已登记到账本，
        /// 未写表的成功 handle 由 PreloadExternalAssetsAsync 的 catch 直接 Dispose；
        /// UIPackage 移除与描述 handle 释放由账本 Rollback 统一执行，保证反向顺序一致。
        /// </remarks>
        /// <exception cref="ArgumentNullException">record、provider 或 ledger 为 null。</exception>
        /// <exception cref="FUIException">描述资源加载失败、描述字节为空、AddPackage 返回 null 或外部资源预载失败。</exception>
        public static async UniTask<UIPackage> LoadPackageAsync(PackageRecord record, IFUIResourceProvider provider, LoadOperationLedger ledger, CancellationToken cancellationToken = default)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            string packageName = record.PackageName;
            // 描述资源 location：{PackageName}_fui（spec：包名与资源 location 使用统一规则）。
            string descLocation = packageName + DescLocationSuffix;

            // 异步预载描述资源。取消保持标准取消语义；其他 provider 异常统一包装为带包名/location
            // 上下文的 FUIException。此时尚未取得 handle，也未向账本登记任何所有权，无需回滚。
            IFUIAssetHandle descHandle;
            try
            {
                descHandle = await provider.LoadAssetAsyncHandle<TextAsset>(descLocation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FUIException(
                    $"[PackageLoader] 描述资源加载失败：{packageName}（location={descLocation}）", ex);
            }

            if (descHandle == null)
            {
                throw new FUIException(
                    $"[PackageLoader] 描述资源加载失败：provider 返回空 handle，pkg={packageName}，location={descLocation}");
            }

            TextAsset descAsset;
            try
            {
                // 描述资源必须已完成且可取为 TextAsset；未就绪或类型不匹配视为加载失败。
                if (!descHandle.IsDone)
                {
                    throw new FUIException($"[PackageLoader] 描述资源未完成加载：{descLocation}");
                }

                descAsset = descHandle.GetAssetObject<TextAsset>();
                if (descAsset == null)
                {
                    string lastError = descHandle.LastError;
                    throw new FUIException($"[PackageLoader] 描述资源为空或类型不匹配：{descLocation}" +
                        (string.IsNullOrEmpty(lastError) ? string.Empty : $"，错误：{lastError}"));
                }
            }
            catch
            {
                // 取描述字节失败时释放本次新建的描述 handle，避免无所有者 handle（spec：资源所有权唯一且释放有序）。
                // 此时 handle 尚未登记到账本（登记点在校验通过之后），故直接 Dispose。
                descHandle.Dispose();
                throw;
            }

            byte[] descBytes;
            try
            {
                // 从 TextAsset 提取描述字节；空字节无法解析包，视为加载失败。
                descBytes = descAsset.bytes;
                if (descBytes == null || descBytes.Length == 0)
                {
                    throw new FUIException($"[PackageLoader] 描述字节为空：{descLocation}");
                }
            }
            catch
            {
                // 描述字节提取失败：handle 尚未登记到账本，直接 Dispose。
                descHandle.Dispose();
                throw;
            }

            // 描述 handle 校验通过并成功取出字节，登记到本次操作账本。
            // 此后描述 handle 的释放由账本 Rollback 统一负责（4.8：补齐描述 handle 回滚）。
            ledger.RecordDescriptorHandle(descHandle);

            // 安装同步资源解析器：只从 record.AssetHandles 已完成的 handle 表返回资产，不触发新的异步加载。
            // assetNamePrefix 传 PackageName，使外部项 file 解析为 {PackageName}_{原file}（spec 命名统一规则）。
            UIPackage package;
            try
            {
                package = UIPackage.AddPackage(descBytes, packageName, CreateSynchronousResolver(record));
            }
            catch (Exception ex)
            {
                // AddPackage 抛出（如描述格式错误）：描述 handle 已登记到账本，由账本 Rollback 释放，
                // 加载器不再单独 Dispose，保证反向顺序与账本一致（4.8：统一由账本回滚）。
                throw new FUIException($"[PackageLoader] AddPackage 失败：{packageName}（location={descLocation}）", ex);
            }

            if (package == null)
            {
                // AddPackage 返回 null 表示 LoadPackage 解析失败：描述 handle 已登记到账本，由账本 Rollback 释放。
                throw new FUIException($"[PackageLoader] AddPackage 返回 null，描述解析失败：{packageName}（location={descLocation}）");
            }

            // AddPackage 成功：将 UIPackage 登记到本次操作账本。
            // 此后 UIPackage 的移除由账本 Rollback 通过 UIPackage.RemovePackage 统一负责（4.8：补齐包注册回滚）。
            ledger.RecordRegisteredPackage(package);

            // 将 UIPackage 写入 PackageRecord，完成描述与包注册的核心写入。
            record.Package = package;

            // 将描述 handle 纳入 PackageRecord 统一持有，修复 4.5 成功路径未写入的泄漏风险
            // （spec：资源所有权唯一且释放有序）。此后 descHandle 由 record 在最终释放阶段 Dispose，
            // 加载器不再单独持有其引用。失败回滚时账本会 Dispose 该 handle，并在此后由
            // OnAcquireFailed 清空 record.DescHandle，避免 record 持有已释放句柄。
            record.DescHandle = descHandle;

            // 并发预载包内全部外部资源（Atlas/Sound/Misc），确保 Acquire 成功前全部进入 Ready
            // （spec：Acquire 成功代表资源已经可用于首屏构造）。
            // 预载阶段将每个写表成功的 handle 登记到账本；失败时未写表的 handle 由预载 catch 直接 Dispose，
            // 已写表的 handle 与 UIPackage、描述 handle 由账本 Rollback 统一回滚（4.8）。
            await PreloadExternalAssetsAsync(record, package, provider, ledger, cancellationToken);

            return package;
        }

        /// <summary>
        /// 递归 Acquire 指定逻辑包：查找/创建 <see cref="PackageRecord"/>、合并同包并发加载任务、
        /// 加载描述与外部资源、递归 Acquire 依赖包、检测依赖环，成功后向调用方返回独立 <see cref="PackageLease"/>。
        /// </summary>
        /// <param name="packageName">逻辑包名，作为包唯一身份与描述 location 前缀。</param>
        /// <param name="provider">资源 provider，用于异步加载描述与外部资源。</param>
        /// <param name="cancellationToken">取消令牌；响应取消时抛出 <see cref="OperationCanceledException"/>。</param>
        /// <param name="acquireChain">
        /// 当前 Acquire 链路中正在加载的包名有序列表，用于依赖环诊断并保留链路顺序。
        /// 顶层调用传 null（内部自动创建）；递归调用由上层传入自身链路副本。
        /// 4.8 增强为有序链（List 替代 HashSet），使环诊断输出真实依赖路径而非无序成员。
        /// </param>
        /// <returns>代表本次调用方对包引用所有权的独立 <see cref="PackageLease"/>；调用方不再持有时应 <see cref="PackageLease.Release"/>。</returns>
        /// <remarks>
        /// 本方法是 Acquire 流程的完整编排器（design.md 决策8）：
        /// <code>
        /// 查找/创建 PackageRecord，合并同包任务
        ///   -> LoadPackageAsync（描述 handle + AddPackage + 并发预载外部资源，按步骤登记到操作账本）
        ///   -> 读取 UIPackage.dependencies 并递归 Acquire 每个依赖包（依赖 lease 登记到操作账本）
        ///   -> 依赖 lease 存入 record.DependencyLeases
        ///   -> 标记 Ready 并返回独立 lease
        /// </code>
        ///
        /// 失败回滚（4.8 操作账本，design.md 决策9）：
        /// 本次 Acquire 创建 <see cref="LoadOperationLedger"/>，随加载推进登记新增所有权资源；
        /// 任一步骤失败时 <see cref="OnAcquireFailed"/> 调用账本 Rollback 按反向顺序原子回滚，
        /// 只释放本次新增所有权，不触碰其他调用方已持有的共享记录（spec：失败和取消执行原子回滚）。
        ///
        /// 同包任务合并（spec：两个窗口同时请求同一包 → 只执行一次加载，返回独立租约）：
        /// 首个调用方创建 <see cref="PackageRecord.SharedLoadTask"/> 并执行加载；后续同包调用方
        /// 直接 await 同一任务，完成后各自获得独立 <see cref="PackageLease"/>（引用计数各自递增）。
        ///
        /// 共享依赖租约（spec：两个包共享依赖 → 依赖只加载一次，任一上层持有时保持可用）：
        /// 包 A 与包 B 都依赖包 C 时，C 的 <see cref="PackageRecord"/> 只创建一次、只加载一次。
        /// A 的 Acquire 递归获取 C 时得到 lease_a 存入 A.DependencyLeases；B 的 Acquire 递归获取 C 时
        /// 发现 C 已 Ready，直接获得独立 lease_b 存入 B.DependencyLeases。C 的引用计数 = 2，
        /// 任一上层释放后只要另一上层仍持有，C 的引用计数仍 > 0，保持可用。
        ///
        /// 依赖环诊断（spec：依赖图出现循环 → 终止该次 Acquire、报告完整依赖链并回滚）：
        /// 使用 <paramref name="acquireChain"/> 跟踪当前递归链路中正在加载的包名（有序链，见
        /// <see cref="BuildDependencyChainText"/>）。在进入某包的加载流程前先检测其是否已出现在
        /// 当前链路中；若是则抛出含完整有序依赖链的 <see cref="FUIException"/>，终止本次 Acquire。
        /// 环检测发生在加入 SharedLoadTask 之前，避免环路上层无限等待自身下游完成而死锁。
        /// 环检测后的完整资源回滚（依赖 lease、外部资源 handle、UIPackage 移除、描述 handle 释放）
        /// 由 4.8 操作账本（<see cref="LoadOperationLedger"/>）按反向顺序原子执行。
        ///
        /// 失败语义（4.8 实现完整反向原子回滚，spec：失败和取消执行原子回滚）：
        /// 任一步骤失败（描述加载、AddPackage、外部资源预载、依赖 Acquire、依赖环）时，
        /// <see cref="OnAcquireFailed"/> 调用 <see cref="LoadOperationLedger.Rollback"/> 按反向顺序
        /// 回滚本次新增的全部所有权（依赖 lease → 外部 handle → RemovePackage → 描述 handle），
        /// 标记记录 Failed、清空 record 中本次新增的 handle/Package 引用使重试可从干净状态开始，
        /// 并通过 SharedLoadTask 向所有等待方传播异常。回滚只释放本次新增所有权，
        /// 不触碰其他调用方此前已持有的共享依赖 lease 或共享 handle。
        /// 本任务保证不向调用方返回处于半就绪状态的 lease。
        ///
        /// 边界约束：不 using 且不反向依赖 GameLogic/GamePlay/GameBattle。
        /// </remarks>
        /// <exception cref="ArgumentNullException">packageName 或 provider 为 null/空。</exception>
        /// <exception cref="FUIException">依赖环、描述/外部资源加载失败或依赖 Acquire 失败。</exception>
        /// <exception cref="OperationCanceledException">取消令牌触发时。</exception>
        public static async UniTask<PackageLease> AcquireAsync(string packageName, IFUIResourceProvider provider, CancellationToken cancellationToken = default, List<string> acquireChain = null)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                throw new ArgumentNullException(nameof(packageName));
            }

            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            // 4.9 停止新访问：若目标包已进入最终释放或已卸载，拒绝创建新引用，防止悬空访问
            // （design.md 决策9 顺序首步“停止新访问”）。
            // Unloading 表示释放流程已启动；Disposed 表示资源已全部回收、包已从 FairyGUI 移除。
            PackageRecord existing = FindRecord(packageName);
            if (existing != null && (existing.State == PackageLoadState.Unloading || existing.State == PackageLoadState.Disposed))
            {
                throw new FUIException($"[PackageLoader] 包正在卸载或已卸载，拒绝 Acquire：{packageName}（state={existing.State}）");
            }

            // 依赖环诊断：每个递归调用维护自身的链路有序列表副本，使不同顶层调用链互不干扰。
            // 顶层调用（acquireChain == null）创建新列表；递归调用复用上层传入的列表。
            // 4.8 增强为有序链（List 替代 HashSet），使环诊断输出真实依赖路径而非无序成员。
            List<string> chain = acquireChain ?? new List<string>();

            // 在进入本包加载流程前检测依赖环：若本包名已出现在当前链路中，说明依赖图存在循环。
            // 此检测必须早于 SharedLoadTask 合并，否则环路上层会 await 自身下游的死锁任务。
            if (chain.Contains(packageName))
            {
                // 构造完整有序依赖链信息：从链路起始到重复包名，标注环入口。
                string chainText = BuildDependencyChainText(chain, packageName);
                throw new FUIException($"[PackageLoader] 检测到包依赖环：{chainText}");
            }

            // 查找或创建 PackageRecord（design.md 决策8：查找/创建 PackageRecord，合并同包任务）。
            PackageRecord record = GetOrCreateRecord(packageName);

            // 同包任务合并：若已有进行中的加载任务，直接 await 同一任务，不重复执行加载。
            // await 完成后向当前调用方返回独立 lease（引用计数递增）。
            UniTaskCompletionSource<PackageRecord> sharedTask = record.SharedLoadTask;
            if (sharedTask != null)
            {
                // 共享等待：同包并发调用方复用同一次加载结果（spec：两个窗口同时请求同一包）。
                PackageRecord completedRecord = await sharedTask.Task;
                // 6.4 待卸载 version 取消：加载完成后若 Delayed 策略曾调度延迟卸载任务，
                // 递增版本使旧卸载任务过期。此处加载任务完成意味着记录已 Ready 或即将返回 lease，
                // 任何在此期间等待的延迟卸载任务都不应继续执行（spec：延迟卸载期间重新 Acquire）。
                CancelPendingUnload(completedRecord);
                // 加载成功后向每个调用方返回独立 lease；若加载失败，await 已抛出异常，不会执行到这里。
                return new PackageLease(completedRecord);
            }

            // 若记录已 Ready（之前已加载完成且仍存活），直接返回独立 lease，不重复加载。
            // 此分支覆盖“共享依赖被第二个上层包请求”的场景（spec：两个包共享依赖 → 依赖只加载一次）。
            if (record.State == PackageLoadState.Ready && record.Package != null)
            {
                // 6.4 待卸载 version 取消：Ready 记录被重新 Acquire 时，递增 PendingUnloadVersion
                // 使正在等待的延迟卸载任务过期（design.md 决策8：延迟卸载前重新 Acquire 通过
                // 递增待卸载版本取消旧卸载任务）。随后创建 lease 递增引用计数，双重保护使旧卸载任务
                // 即使错过版本检查也会被 CanUnload 的零引用检查拦截。
                CancelPendingUnload(record);
                return new PackageLease(record);
            }

            // 本调用方成为加载执行者：创建 SharedLoadTask 供后续并发调用方合并等待。
            record.SharedLoadTask = new UniTaskCompletionSource<PackageRecord>();
            record.State = PackageLoadState.Loading;

            // 6.7 诊断：测量本次加载耗时，写入 record.LoadDurationMs 供诊断日志使用。
            System.Diagnostics.Stopwatch loadStopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 本次 Acquire 的操作账本：记录本次新增的所有权资源，失败时按反向顺序原子回滚（4.8，design.md 决策9）。
            LoadOperationLedger ledger = new LoadOperationLedger(packageName);

            try
            {
                // 将本包加入当前链路，供下游递归检测环。chain 为引用类型，下游递归直接复用同一集合；
                // 但下游在返回前不应移除本包名（本包在整条链中始终存在），故采用“加入后不在递归返回时移除”
                // 的策略——每条递归路径维护独立 chain 副本，避免兄弟依赖间互相误判为环。
                // 此处加入本包名，递归调用传入 chain 副本（见 AcquireDependenciesAsync）。
                chain.Add(packageName);

                // 执行描述加载、AddPackage 与外部资源并发预载（4.5+4.6+4.7 已实现，4.8 增加账本登记）。
                // 加载器在推进每个成功步骤后向 ledger 登记新增所有权；失败时由下方 catch 统一回滚。
                await LoadPackageAsync(record, provider, ledger, cancellationToken);

                // 读取 UIPackage.dependencies 并递归 Acquire 每个依赖包（design.md 决策8）。
                // 依赖 lease 存入 record.DependencyLeases 并登记到 ledger，失败时由账本回滚本次新增引用。
                await AcquireDependenciesAsync(record, provider, cancellationToken, chain, ledger);

                // 全部成功：标记 Ready 并向所有等待方传播成功结果。
                record.State = PackageLoadState.Ready;
                record.SharedLoadTask.TrySetResult(record);

                // 6.7 诊断日志：包加载成功——覆盖加载耗时、引用计数、handle 数量、依赖链、状态。
                // 只在状态变化（Loading -> Ready）时输出，不在每帧轮询中输出
                // （任务要求：避免每帧无状态变化时重复输出）。
                loadStopwatch.Stop();
                record.LoadDurationMs = loadStopwatch.ElapsedMilliseconds;
                Log.Info(
                    "[GameFUI] 包加载成功：pkg={0}, state={1}, refCount={2}, handles={3}, depLeases={4}, depChain=[{5}], loadMs={6}",
                    record.PackageName, record.State, record.ReferenceCount,
                    record.AssetHandles.Count, record.DependencyLeases.Count,
                    record.BuildDependencyChainText(), record.LoadDurationMs);

                // 向本次调用方返回独立 lease。后续合并等待方通过上方 await 分支获得各自 lease。
                return new PackageLease(record);
            }
            catch (Exception ex)
            {
                // 失败处理：账本反向原子回滚本次新增所有权、标记 Failed、清空 record 半就绪引用、
                // 向等待方传播异常（4.8，design.md 决策9，spec：失败和取消执行原子回滚）。
                OnAcquireFailed(record, ledger, ex);

                // 6.7 诊断日志：包加载失败——覆盖失败上下文、依赖链、handle 数量、状态。
                // 只在失败事件发生时输出一次，不重复输出。
                loadStopwatch.Stop();
                record.LoadDurationMs = loadStopwatch.ElapsedMilliseconds;
                Log.Error(
                    "[GameFUI] 包加载失败：pkg={0}, state={1}, refCount={2}, handles={3}, depChain=[{4}], loadMs={5}, failure={6}",
                    record.PackageName, record.State, record.ReferenceCount,
                    record.AssetHandles.Count, record.BuildDependencyChainText(),
                    record.LoadDurationMs, record.FailureContext ?? "<未知>");

                // 取消异常按 UniTask 语义传播（TrySetException 内部已将 OperationCanceledException 转为取消态）。
                throw;
            }
            finally
            {
                // 加载任务（无论成功或失败）结束后清空 SharedLoadTask，使后续重试或重新 Acquire 能创建新任务。
                // 并发等待方已通过上方 TrySetResult/TrySetException 捕获了任务引用，清空 record 字段不影响其 await。
                record.SharedLoadTask = null;
            }
        }

        /// <summary>
        /// 对指定包执行最终释放：前置检查满足卸载条件后，按固定顺序
        /// 停止新访问 → Dispose 包对象 → <see cref="UIPackage.RemovePackage"/> → Dispose 本包 handles → 释放依赖 leases。
        /// </summary>
        /// <param name="packageName">逻辑包名。</param>
        /// <param name="windowStateProvider">
        /// 窗口状态查询接口，由 FUIModule 提供，用于判断是否存在存活或缓存窗口、创建任务或上层依赖持有该包。
        /// 为 null 时回退到全局注册的 <see cref="WindowStateProvider"/>（6.5 实现，由 FUIModule.FreezeBindings 注册）；
        /// 两者均为 null 时视为无窗口约束（如未注册 FUIModule 的单包测试场景）。
        /// </param>
        /// <returns>true 表示执行了最终释放；false 表示前置检查未满足，未释放（调用方应保留包到 Shutdown）。</returns>
        /// <remarks>
        /// 设计依据：design.md 决策9“最终卸载必须同时满足引用为零、没有存活或缓存窗口、没有创建任务、
        /// 没有上层依赖、没有资源操作；顺序固定为停止新访问、Dispose 所有包对象、UIPackage.RemovePackage、
        /// Dispose 本包 handles、释放依赖 leases”，以及 spec“资源所有权唯一且释放有序——
        /// 释放 SHALL 先移除 FairyGUI 包，再释放本包持有的 handle，最后递减依赖租约”。
        ///
        /// 前置检查（design.md 决策9 卸载条件）：
        /// - 引用为零（ReferenceCount == 0）；
        /// - 没有存活或缓存窗口、创建任务或上层依赖：由 <see cref="IFUIWindowStateProvider.HasActiveOrCachedWindow"/>
        ///   查询（6.5 由 FUIModule 实现）；<paramref name="windowStateProvider"/> 为 null 时回退到全局注册的
        ///   <see cref="WindowStateProvider"/>；两者均为 null 时视为无窗口约束；
        /// - 没有创建任务：SharedLoadTask == null 且 State 不是 Loading；
        /// - 没有上层依赖：引用为零已隐含（上层依赖通过 PackageLease 持有，会贡献引用计数），
        ///   6.5 同时由 <see cref="IFUIWindowStateProvider.HasActiveOrCachedWindow"/> 显式检查作为双重保护。
        ///
        /// KeepUntilShutdown 策略（design.md 决策8）：本任务只实现该策略，引用为零后不立即卸载，
        /// 包保留到模块 Shutdown 时由 <see cref="UnloadAllForShutdown"/> 统一释放。
        /// 因此正常运行期通常不直接调用本方法；本方法作为最终释放的统一实现，
        /// 供 Shutdown 与未来 Delayed 策略（6.x 任务）复用。
        ///
        /// 边界约束：不 using 且不反向依赖 GameLogic/GamePlay/GameBattle。
        /// </remarks>
        /// <exception cref="ArgumentNullException">packageName 为 null 或空。</exception>
        public static bool UnloadPackage(string packageName, IFUIWindowStateProvider windowStateProvider = null)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                throw new ArgumentNullException(nameof(packageName));
            }

            PackageRecord record = FindRecord(packageName);
            if (record == null)
            {
                // 未加载的包无需释放。
                return false;
            }

            // 前置检查：不满足卸载条件时拒绝释放，保留包到 Shutdown（KeepUntilShutdown 策略）。
            if (!CanUnload(record, windowStateProvider))
            {
                return false;
            }

            // 执行固定顺序的最终释放。
            FinalUnload(record);
            return true;
        }

        /// <summary>
        /// 模块 Shutdown 统一释放：按 KeepUntilShutdown 策略，在 Shutdown 时强制释放全部已注册包。
        /// </summary>
        /// <param name="windowStateProvider">窗口状态查询接口（Shutdown 场景可传 null；6.5 起全局注册的 <see cref="WindowStateProvider"/> 在本强制路径中也不查询，保证 Shutdown 强制回收语义）。</param>
        /// <remarks>
        /// 设计依据：design.md 决策8“首个实现允许 KeepUntilShutdown 卸载策略”与
        /// spec“模块 Shutdown 回收全部包资源——无论运行期延迟卸载是否启用，模块 Shutdown SHALL
        /// 停止新的 Acquire，等待或失效化进行中的资源操作，移除所有由模块注册的 FairyGUI 包，
        /// 并释放全部持有的 handle”。
        ///
        /// Shutdown 语义与单包 <see cref="UnloadPackage"/> 的区别：
        /// - 单包释放受前置检查约束（引用为零、无窗口、无任务）；Shutdown 为强制回收，
        ///   不逐包检查引用计数与窗口，直接对每个已注册记录执行最终释放顺序。
        ///   这保证 KeepUntilShutdown 下“引用为零后保留到 Shutdown”的包也被回收
        ///   （spec：包常驻模式退出 → Shutdown 完成后所有包、依赖引用和 handle 回到启动前基线）。
        /// - 进行中加载任务：Shutdown 不等待，直接将其视为已失效；FinalUnload 会先移除包注册，
        ///   使任何迟到的加载结果不会写入已释放的记录。
        ///
        /// 6.5：全局注册的 <see cref="WindowStateProvider"/> 在本强制路径中不参与判定
        /// （与 <see cref="CanUnload"/> 的延迟卸载路径不同），保证 Shutdown 不被窗口状态拦截。
        /// <see cref="FUIModule.Shutdown"/> 会在调用本方法前后清空全局注册，避免跨模块残留。
        ///
        /// 边界约束：不 using 且不反向依赖 GameLogic/GamePlay/GameBattle。
        /// </remarks>
        public static void UnloadAllForShutdown(IFUIWindowStateProvider windowStateProvider = null)
        {
            // Shutdown 强制回收：不逐包检查引用计数与窗口，直接对每个记录执行最终释放顺序。
            // windowStateProvider 在强制 Shutdown 路径下不使用，保留参数为与 UnloadPackage 签名一致，
            // 便于未来 Delayed 策略（6.x 任务）复用本方法时按需传入。
            // 复制记录列表，避免释放过程中遍历被修改的字典。
            List<PackageRecord> records = new List<PackageRecord>(_records.Values);
            int count = records.Count;
            for (int i = 0; i < count; i++)
            {
                PackageRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                // 6.4：递增待卸载版本，使进行中的 Delayed 延迟卸载任务在到期时因版本不匹配而中止。
                // 这是优化路径：即使不递增，FinalUnload 的重入保护与 CanUnload 的 Unloading 状态检查
                // 仍会拦截延迟任务；递增版本使延迟任务在版本检查阶段就提前返回，避免不必要的引用/状态检查。
                record.PendingUnloadVersion++;

                FinalUnload(record);
            }

            // 释放完成后清空注册表，使 Shutdown 后基线回到启动前状态
            // （spec：Shutdown 完成后所有模块持有的包、依赖引用和 handle SHALL 回到启动前基线）。
            _records.Clear();
        }

        /// <summary>
        /// 判断指定包是否满足最终卸载的前置条件。
        /// </summary>
        /// <param name="record">包记录。</param>
        /// <param name="windowStateProvider">窗口状态查询接口；为 null 时回退到全局注册的 <see cref="WindowStateProvider"/>。</param>
        /// <returns>true 表示满足卸载条件。</returns>
        /// <remarks>
        /// design.md 决策9 卸载条件：引用为零、没有存活或缓存窗口、没有创建任务、没有上层依赖、没有资源操作。
        /// 引用为零已隐含“没有上层依赖”（上层依赖通过 PackageLease 持有并贡献引用计数）；
        /// 但 6.5 仍通过 <see cref="IFUIWindowStateProvider.HasActiveOrCachedWindow"/> 显式查询窗口状态
        /// （含存活/缓存窗口、创建任务与上层依赖），作为对引用计数的双重保护，避免任何时序竞态导致
        /// 仍在使用的包被误卸载（spec：包租约控制缓存和卸载——只有不存在存活或缓存对象、创建任务、
        /// 上层依赖和待完成资源操作时，包才可在延迟窗口结束后卸载）。
        ///
        /// 6.5 改动：当 <paramref name="windowStateProvider"/> 为 null 时，回退到全局注册的
        /// <see cref="WindowStateProvider"/>（由 <see cref="FUIModule.FreezeBindings"/> 注册）。
        /// 这使 <see cref="ScheduleDelayedUnload"/> 的 fire-and-forget 延迟卸载路径（无法接收参数）
        /// 也能正确查询窗口状态，补齐 6.4 留下的窗口约束缺口。
        /// 显式传入参数（如 <see cref="UnloadPackage"/> 调用方确信无窗口时传 null 并依赖回退）
        /// 与全局注册实例二选一：参数非空时优先参数，参数为 null 时使用全局注册实例。
        /// </remarks>
        private static bool CanUnload(PackageRecord record, IFUIWindowStateProvider windowStateProvider)
        {
            // 引用为零：所有 lease（窗口、创建任务、上层依赖）均已释放。
            if (record.ReferenceCount != 0)
            {
                return false;
            }

            // 没有创建任务：无进行中的加载任务且不在 Loading 状态。
            if (record.SharedLoadTask != null || record.State == PackageLoadState.Loading)
            {
                return false;
            }

            // 6.5：没有存活或缓存窗口、创建任务或上层依赖：由 FUIModule 通过 IFUIWindowStateProvider 查询。
            // 优先使用显式传入的 provider；为 null 时回退到全局注册的 WindowStateProvider
            // （由 FUIModule.FreezeBindings 注册），使 Delayed 延迟卸载路径也能查询窗口状态。
            // 两者均为 null 时视为无窗口约束（如 4.9 单包测试未注册 FUIModule 场景），保持向后兼容。
            IFUIWindowStateProvider provider = windowStateProvider ?? WindowStateProvider;
            if (provider != null && provider.HasActiveOrCachedWindow(record.PackageName))
            {
                return false;
            }

            // 已 Unloading/Disposed 的记录不重复释放。
            if (record.State == PackageLoadState.Unloading || record.State == PackageLoadState.Disposed)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 递增 <see cref="PackageRecord.PendingUnloadVersion"/>，使正在等待的延迟卸载任务过期（6.4 实现）。
        /// </summary>
        /// <param name="record">被重新 Acquire 的包记录。</param>
        /// <remarks>
        /// design.md 决策8：延迟卸载前重新 Acquire 通过递增待卸载版本取消旧卸载任务。
        /// spec“延迟卸载期间重新 Acquire → 卸载 SHALL 被取消并复用现有包记录，不得产生卸载与重载抖动”。
        ///
        /// 调用时机（<see cref="AcquireAsync"/> 内）：
        /// 1. 命中已 Ready 记录时（共享依赖被第二个上层包请求，或延迟期内重新打开窗口）；
        /// 2. 共享加载任务完成时（合并等待方各自获取 lease 前）。
        ///
        /// 机制：版本号只增不减。延迟卸载任务在 <see cref="ScheduleDelayedUnload"/> 时捕获当时的版本号，
        /// 到期后比对当前 <see cref="PackageRecord.PendingUnloadVersion"/>，不一致则放弃卸载。
        /// 即使版本检查因时序竞态漏过，<see cref="CanUnload"/> 的零引用检查与
        /// <see cref="FinalUnload"/> 的重入保护仍提供双重兜底。
        ///
        /// 边界约束：本方法只递增版本号，不执行任何卸载或加载操作，无副作用。
        /// </remarks>
        private static void CancelPendingUnload(PackageRecord record)
        {
            if (record == null)
            {
                return;
            }

            record.PendingUnloadVersion++;
        }

        /// <summary>
        /// 引用计数归零回调入口：根据当前卸载策略决定是否启动延迟卸载任务（6.4 实现）。
        /// </summary>
        /// <param name="record">引用计数刚归零的包记录。</param>
        /// <remarks>
        /// 本方法由 <see cref="PackageRecord.OnReferenceCountReachedZero"/> 静态回调触发，
        /// 在 <see cref="PackageRecord.TryReleaseReference"/> 内同步调用。
        ///
        /// 策略分流（design.md 决策8）：
        /// - <see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>：直接返回，包保留到模块 Shutdown
        ///   由 <see cref="UnloadAllForShutdown"/> 统一回收。与 4.9 已实现行为一致。
        /// - <see cref="FUIPackageUnloadPolicy.Delayed"/>：调用 <see cref="ScheduleDelayedUnload"/>
        ///   调度延迟卸载任务。任务到期后重新检查引用计数与版本，满足条件才执行最终释放。
        ///
        /// 防御性检查：非 Ready 状态（Loading/Failed/Unloading/Disposed）的记录不启动卸载，
        /// 避免在加载中或已释放的记录上触发误操作。
        ///
        /// 异步安全：本方法在 lease.Release 的同步路径上调用，不阻塞调用方；
        /// <see cref="ScheduleDelayedUnload"/> 使用 fire-and-forget UniTask，延迟任务在后台执行，
        /// 到期后的卸载检查与执行通过 <see cref="CanUnload"/> 与 <see cref="FinalUnload"/> 重入保护保证安全。
        /// </remarks>
        private static void HandleReferenceCountReachedZero(PackageRecord record)
        {
            if (record == null)
            {
                return;
            }

            // 防御性：非 Ready 状态不启动卸载（加载中/已失败/已卸载/正在卸载）。
            if (record.State != PackageLoadState.Ready)
            {
                return;
            }

            FUIPackageUnloadPolicy policy = UnloadPolicy;
            if (policy == FUIPackageUnloadPolicy.KeepUntilShutdown)
            {
                // KeepUntilShutdown：引用归零后保留到 Shutdown，不启动运行期卸载（4.9 已实现策略）。
                // 6.7 诊断：只在引用计数从非零变为零的边界输出一次，不重复输出。
                Log.Info(
                    "[GameFUI] 包引用计数归零（KeepUntilShutdown 保留）：pkg={0}, refCount={1}, handles={2}, depChain=[{3}]",
                    record.PackageName, record.ReferenceCount, record.AssetHandles.Count,
                    record.BuildDependencyChainText());
                return;
            }

            if (policy == FUIPackageUnloadPolicy.Delayed)
            {
                // Delayed：调度延迟卸载任务，到期后重新检查引用计数与版本（6.4 实现）。
                // 6.7 诊断：引用计数归零并启动延迟卸载，只在边界事件输出一次。
                Log.Info(
                    "[GameFUI] 包引用计数归零（Delayed 调度卸载）：pkg={0}, refCount={1}, handles={2}, depChain=[{3}], delayMs={4}, pendingVer={5}",
                    record.PackageName, record.ReferenceCount, record.AssetHandles.Count,
                    record.BuildDependencyChainText(), UnloadDelayMs, record.PendingUnloadVersion + 1);
                ScheduleDelayedUnload(record);
            }
        }

        /// <summary>
        /// 调度延迟卸载任务：递增版本号、等待 <see cref="UnloadDelayMs"/> 后执行零引用检查与最终释放（6.4 实现）。
        /// </summary>
        /// <param name="record">待延迟卸载的包记录（引用计数已归零）。</param>
        /// <remarks>
        /// design.md 决策8：延迟卸载前重新 Acquire 通过递增待卸载版本取消旧卸载任务。
        /// spec“延迟卸载期间重新 Acquire → 卸载 SHALL 被取消并复用现有包记录”。
        ///
        /// 流程：
        /// 1. 递增 <see cref="PackageRecord.PendingUnloadVersion"/> 并捕获本次任务版本号；
        /// 2. 等待 <see cref="UnloadDelayMs"/> 毫秒（<see cref="UniTask.Delay"/>）；
        /// 3. 到期后执行三重检查（零引用检查的核心）：
        ///    a. 版本一致性：record.PendingUnloadVersion == 捕获版本，否则说明期间有新 Acquire 取消了本次卸载；
        ///    b. 引用计数仍为零：期间若有新 Acquire，引用计数 > 0；
        ///    c. <see cref="CanUnload"/> 综合检查：状态仍为 Ready、无加载任务、无窗口（如提供 provider）；
        /// 4. 三重检查全部通过后调用 <see cref="FinalUnload"/> 执行最终释放。
        ///
        /// 异步安全（UniTask 上下文）：
        /// - fire-and-forget：本方法不返回 Task，调用方不等待；延迟任务在 UniTask 调度器上执行。
        /// - 延迟期间的状态变化由三重检查覆盖：
        ///   * 新 Acquire 会递增版本 → 检查 a 拦截；
        ///   * 新 lease 会使引用计数 > 0 → 检查 b 拦截；
        ///   * Shutdown 会将状态置为 Unloading → 检查 c（CanUnload）拦截，且 FinalUnload 重入保护兜底。
        /// - 不使用 CancellationToken：延迟任务不响应外部取消，统一通过版本/引用/状态检查自然中止。
        ///   这避免了 Shutdown 时需要追踪并取消每个延迟任务的复杂性；
        ///   <see cref="UnloadAllForShutdown"/> 的 FinalUnload 重入保护确保即使延迟任务晚于 Shutdown 执行也不会双重释放。
        /// - 异常保护：async void 的未观察异常会冒泡到 Unity 主循环导致崩溃，故方法体用 try-catch 包裹，
        ///   捕获并吞掉延迟卸载过程中的异常，仅在诊断上下文记录（FinalUnload 内部已有逐步防御性 try-catch）。
        ///
        /// 边界约束：不修改 FairyGUI 源码，不反向依赖 GameLogic/GamePlay/GameBattle。
        /// </remarks>
        private static async void ScheduleDelayedUnload(PackageRecord record)
        {
            try
            {
                // 1. 递增版本号并捕获本次任务版本。后续若有新 Acquire，会再次递增使本版本过期。
                record.PendingUnloadVersion++;
                int myVersion = record.PendingUnloadVersion;

                int delayMs = UnloadDelayMs;

                // 2. 等待延迟时间。UniTask.Delay 在 UniTask 调度器上执行，不阻塞调用线程。
                //    不传入 CancellationToken：延迟任务通过版本/引用/状态三重检查自然中止，
                //    避免 Shutdown 时追踪取消每个延迟任务的复杂性（FinalUnload 重入保护兜底）。
                await UniTask.Delay(delayMs);

                // 3. 到期后执行三重检查，任意一项不满足即放弃卸载。
                //
                // 3a. 版本一致性检查：期间若有新 Acquire（CancelPendingUnload 递增版本），本版本已过期。
                if (record.PendingUnloadVersion != myVersion)
                {
                    // 版本不匹配：延迟期内被重新 Acquire 取消，放弃卸载（spec：延迟卸载期间重新 Acquire）。
                    // 6.7 诊断：只在版本变化边界输出一次。
                    Log.Info(
                        "[GameFUI] 延迟卸载被取消（版本过期）：pkg={0}, myVer={1}, curVer={2}, refCount={3}",
                        record.PackageName, myVersion, record.PendingUnloadVersion, record.ReferenceCount);
                    return;
                }

                // 3b. 零引用检查：引用计数必须仍为零。期间若有新 lease 创建，引用计数 > 0。
                if (record.ReferenceCount != 0)
                {
                    // 引用计数非零：期间有新 Acquire 或并发 lease 创建，放弃卸载。
                    // 6.7 诊断：只在引用计数变化边界输出一次。
                    Log.Info(
                        "[GameFUI] 延迟卸载被取消（引用非零）：pkg={0}, refCount={1}, curVer={2}",
                        record.PackageName, record.ReferenceCount, record.PendingUnloadVersion);
                    return;
                }

                // 3c. 综合卸载条件检查：状态、加载任务、窗口约束（含存活/缓存窗口、创建任务、上层依赖）。
                // 6.5：CanUnload 在 windowStateProvider 参数为 null 时回退到全局注册的 WindowStateProvider
                // （由 FUIModule.FreezeBindings 注册）。这使得延迟卸载路径也能查询窗口状态，
                // 补齐 6.4 留下的窗口约束缺口。未注册 FUIModule 时（如 4.9 单包测试）回退到 null，
                // 视为无窗口约束，保持向后兼容（spec：包租约控制缓存和卸载）。
                if (!CanUnload(record, null))
                {
                    // 综合条件不满足：可能状态已变为 Unloading（Shutdown 抢先）、仍有加载任务，
                    // 或 6.5 新增的窗口状态检查发现仍有存活/缓存窗口、创建任务或上层依赖引用该包。
                    // 6.7 诊断：只在卸载前置检查失败边界输出一次，包含完整诊断字段。
                    Log.Info(
                        "[GameFUI] 延迟卸载被取消（前置检查未通过）：pkg={0}, state={1}, refCount={2}, handles={3}, depChain=[{4}], diag={5}",
                        record.PackageName, record.State, record.ReferenceCount,
                        record.AssetHandles.Count, record.BuildDependencyChainText(),
                        record.ToDiagnosticString());
                    return;
                }

                // 4. 三重检查全部通过，执行最终释放。
                // FinalUnload 内部有重入保护：若 Shutdown 已抢先将其置为 Unloading/Disposed，直接返回。
                // 6.7 诊断：执行最终释放，只在状态变化边界输出一次。
                Log.Info(
                    "[GameFUI] 延迟卸载执行最终释放：pkg={0}, state={1}, refCount={2}, handles={3}, depChain=[{4}], loadMs={5}",
                    record.PackageName, record.State, record.ReferenceCount,
                    record.AssetHandles.Count, record.BuildDependencyChainText(),
                    record.LoadDurationMs);
                FinalUnload(record);
            }
            catch
            {
                // async void 未观察异常会冒泡到 Unity 主循环导致崩溃，故捕获并吞掉。
                // FinalUnload 内部各步骤已有防御性 try-catch，此处的 catch 主要防御意外的 UniTask 调度异常。
                // 诊断由 FinalUnload 内部与 PackageRecord.ToString 提供，不在此处重复输出。
            }
        }

        /// <summary>
        /// 执行最终释放的固定顺序（design.md 决策9）：
        /// 停止新访问 → Dispose 包对象 → <see cref="UIPackage.RemovePackage"/> →
        /// Dispose 本包 handles → 释放依赖 leases。
        /// </summary>
        /// <param name="record">待释放的包记录。</param>
        /// <remarks>
        /// 本方法不检查前置条件，由调用方（<see cref="UnloadPackage"/> 或 <see cref="UnloadAllForShutdown"/>）
        /// 负责决定是否调用。方法内部对每一步做防御性保护，保证任意步骤异常不中断后续释放。
        ///
        /// 6.4 重入保护：方法起始处检查记录是否已处于 Unloading/Disposed 终态，若是则直接返回，
        /// 避免 Delayed 延迟卸载任务与 <see cref="UnloadAllForShutdown"/> 并发执行导致双重释放。
        /// 该保护对既有 KeepUntilShutdown 路径无副作用（正常路径进入时状态为 Ready）。
        ///
        /// 顺序依据（design.md 决策9 与 spec“资源所有权唯一且释放有序”）：
        /// a. 停止新访问：标记 Unloading，使 Acquire 拒绝创建新引用；
        /// b. Dispose 包对象：FairyGUI 对象由窗口管理，此处指包级资源清空 record.Package 引用
        ///    （UIPackage 的 Dispose 由 RemovePackage 内部触发，不在此单独调用）；
        /// c. UIPackage.RemovePackage(packageName)：移除 FairyGUI 包注册（RemovePackage 内部调用 pkg.Dispose）；
        /// d. Dispose 本包 handles：AssetHandles 表 + DescHandle；
        /// e. 释放依赖 leases：DependencyLeases 按反向顺序释放。
        ///
        /// spec 释放顺序“先移除 FairyGUI 包，再释放本包 handle，最后递减依赖租约”与本顺序一致：
        /// 步骤 c（RemovePackage）→ 步骤 d（handle Dispose）→ 步骤 e（依赖 lease 释放）。
        /// 重复释放租约被拒绝：PackageLease.Release 已有幂等保护，此处对 IsReleased 的 lease 跳过。
        /// </remarks>
        private static void FinalUnload(PackageRecord record)
        {
            // 6.4 重入保护：已进入 Unloading/Disposed 的记录直接返回，避免延迟卸载任务与 Shutdown
            // 并发执行导致双重释放（RemovePackage/handle Dispose/lease 释放均无幂等保证）。
            if (record.State == PackageLoadState.Unloading || record.State == PackageLoadState.Disposed)
            {
                return;
            }

            // a. 停止新访问：标记 Unloading，使后续 Acquire 拒绝创建新引用（design.md 决策9 首步）。
            // 6.7 诊断：包状态变化（Ready -> Unloading），只在状态变化边界输出一次。
            PackageLoadState stateBeforeUnload = record.State;
            Log.Info(
                "[GameFUI] 包状态变化：pkg={0}, {1} -> {2}, refCount={3}, handles={4}, depChain=[{5}], loadMs={6}",
                record.PackageName, stateBeforeUnload, PackageLoadState.Unloading,
                record.ReferenceCount, record.AssetHandles.Count,
                record.BuildDependencyChainText(), record.LoadDurationMs);
            record.State = PackageLoadState.Unloading;

            // b. Dispose 包对象：FairyGUI 对象由窗口管理，此处清空 record 对 UIPackage 的引用。
            //    UIPackage.Dispose 由步骤 c 的 RemovePackage 内部触发，不在此单独调用，
            //    避免重复 Dispose。保留 packageName 供 RemovePackage 使用。
            UIPackage package = record.Package;

            // c. UIPackage.RemovePackage(packageName)：移除 FairyGUI 包注册。
            //    RemovePackage 内部调用 pkg.Dispose 并从全局注册表移除（UIPackage.cs:383 签名已验证）。
            //    若包已移除或未注册，捕获异常不中断后续释放（防御性）。
            try
            {
                if (package != null)
                {
                    UIPackage.RemovePackage(record.PackageName);
                }
            }
            catch (Exception)
            {
                // 包已被移除或未注册时 RemovePackage 会抛出；释放目标已达成，继续后续步骤。
            }

            // d. Dispose 本包 handles：AssetHandles 表 + DescHandle。
            //    先 Dispose 外部资源 handle，再 Dispose 描述 handle，与加载顺序相反。
            //    handle Dispose 异常不中断后续依赖 lease 释放。
            DisposeHandles(record);

            // e. 释放依赖 leases：按反向顺序释放，使后获取的依赖先归还。
            //    PackageLease.Release 已有幂等保护：IsReleased 的 lease 会被跳过，
            //    重复释放被拒绝且不会使引用计数变为负数（spec：重复释放租约 SHALL 被拒绝）。
            ReleaseDependencyLeases(record);

            // 清空 record 持有的资源引用，标记 Disposed 终态。
            record.Package = null;
            record.DescHandle = null;
            record.AssetHandles.Clear();
            record.DependencyLeases.Clear();
            record.State = PackageLoadState.Disposed;

            // 6.7 诊断：包进入 Disposed 终态，只在状态变化边界输出一次。
            Log.Info(
                "[GameFUI] 包状态变化：pkg={0}, {1} -> {2}, refCount={3}, handles=0, loadMs={4}",
                record.PackageName, PackageLoadState.Unloading, PackageLoadState.Disposed,
                record.ReferenceCount, record.LoadDurationMs);
        }

        /// <summary>
        /// Dispose 包记录持有的全部 handle（AssetHandles 表 + DescHandle）。
        /// </summary>
        /// <param name="record">包记录。</param>
        /// <remarks>
        /// 先 Dispose 外部资源 handle，再 Dispose 描述 handle，与加载顺序相反。
        /// 单个 handle Dispose 异常不中断其余 handle 释放。
        /// </remarks>
        private static void DisposeHandles(PackageRecord record)
        {
            // Dispose 外部资源 handle（AssetHandles 表）。
            Dictionary<string, IFUIAssetHandle>.ValueCollection.Enumerator enumerator = record.AssetHandles.Values.GetEnumerator();
            while (enumerator.MoveNext())
            {
                IFUIAssetHandle handle = enumerator.Current;
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
                    // handle Dispose 异常不中断其余 handle 释放。
                }
            }

            // Dispose 描述 handle。
            IFUIAssetHandle descHandle = record.DescHandle;
            if (descHandle != null)
            {
                try
                {
                    descHandle.Dispose();
                }
                catch (Exception)
                {
                    // 描述 handle Dispose 异常不中断后续依赖 lease 释放。
                }
            }
        }

        /// <summary>
        /// 按反向顺序释放包记录持有的依赖租约。
        /// </summary>
        /// <param name="record">包记录。</param>
        /// <remarks>
        /// design.md 决策9“释放依赖 leases（反向顺序）”。
        /// PackageLease.Release 已有幂等保护：IsReleased 的 lease.Release 返回 false 且不改变引用计数，
        /// 故重复释放被拒绝，引用计数不会变为负数（spec：重复释放租约 SHALL 被拒绝）。
        /// 单个 lease 释放异常不中断其余 lease 释放。
        /// </remarks>
        private static void ReleaseDependencyLeases(PackageRecord record)
        {
            List<PackageLease> leases = record.DependencyLeases;
            int count = leases.Count;
            // 反向释放：后获取的依赖先归还。
            for (int i = count - 1; i >= 0; i--)
            {
                PackageLease lease = leases[i];
                if (lease == null || lease.IsReleased)
                {
                    // 已释放的 lease 跳过；PackageLease.Release 幂等保护确保引用计数不为负。
                    continue;
                }

                try
                {
                    lease.Release();
                }
                catch (Exception)
                {
                    // lease 释放异常不中断其余 lease 释放。
                }
            }
        }

        /// <summary>
        /// 查找或创建指定逻辑包名的 <see cref="PackageRecord"/>，并登记到注册表。
        /// </summary>
        /// <param name="packageName">逻辑包名。</param>
        /// <returns>已存在或新建的包记录。</returns>
        /// <remarks>
        /// 同一包名全模块只创建一个记录，是同包任务合并与共享依赖租约的基础
        /// （spec：两个包共享依赖 → 依赖只加载一次）。
        /// </remarks>
        private static PackageRecord GetOrCreateRecord(string packageName)
        {
            if (!_records.TryGetValue(packageName, out PackageRecord record) || record == null)
            {
                record = new PackageRecord(packageName);
                _records[packageName] = record;
            }

            return record;
        }

        /// <summary>
        /// 读取 <see cref="UIPackage.dependencies"/> 并对每个依赖包递归 Acquire，
        /// 将得到的依赖租约存入 <see cref="PackageRecord.DependencyLeases"/> 并登记到本次操作账本。
        /// </summary>
        /// <param name="record">当前包记录，其 <see cref="PackageRecord.Package"/> 的 dependencies 被遍历。</param>
        /// <param name="provider">资源 provider，透传给递归 Acquire。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="chain">当前 Acquire 链路有序列表（已含当前包名），用于依赖环诊断。</param>
        /// <param name="ledger">本次 Acquire 操作账本，依赖 lease 登记其中供失败时反向回滚。</param>
        /// <remarks>
        /// 依赖 API 依据 FairyGUI 源码（UIPackage.cs）：dependencies 为
        /// <c>Dictionary&lt;string, string&gt;[]</c>，每个字典含 "id" 与 "name" 键。
        /// 本方法以 "name" 作为依赖包逻辑名递归 Acquire，与包名作为逻辑身份的 spec 规则一致。
        ///
        /// 兄弟依赖隔离：每个兄弟依赖递归调用传入当前 chain 的副本，避免一个兄弟依赖把另一个兄弟
        /// 误判为环（兄弟之间不存在依赖关系，不应共享链路中的“正在加载”标记）。
        /// 若依赖之间存在真实环（如 A→B→C→A），环会在递归进入 A 时被 chain 命中检测。
        ///
        /// 共享依赖：多个上层包依赖同一包时，递归 Acquire 命中已 Ready 的记录，
        /// 直接返回独立 lease，依赖只加载一次（spec：两个包共享依赖）。
        ///
        /// 账本登记：每个依赖 lease 同时存入 record.DependencyLeases 与 ledger。
        /// 失败时账本 Rollback 反向释放本次新增 lease，并从 record.DependencyLeases 移除，
        /// 不触碰其他调用方此前已持有的共享依赖 lease（4.8，spec：失败和取消执行原子回滚）。
        /// </remarks>
        /// <exception cref="FUIException">依赖名缺失、依赖 Acquire 失败或检测到依赖环（由递归调用抛出）。</exception>
        private static async UniTask AcquireDependenciesAsync(PackageRecord record, IFUIResourceProvider provider, CancellationToken cancellationToken, List<string> chain, LoadOperationLedger ledger)
        {
            UIPackage package = record.Package;
            if (package == null)
            {
                // 防御性：LoadPackageAsync 成功后 Package 不应为 null。
                throw new FUIException($"[PackageLoader] 依赖遍历时包未注册：{record.PackageName}");
            }

            // FairyGUI dependencies API：Dictionary<string,string>[]，每项含 "id" 与 "name"。
            // 无依赖时为 null 或空数组。
            Dictionary<string, string>[] deps = package.dependencies;
            if (deps == null || deps.Length == 0)
            {
                return;
            }

            for (int i = 0; i < deps.Length; i++)
            {
                Dictionary<string, string> dep = deps[i];
                if (dep == null)
                {
                    continue;
                }

                // 以 "name" 作为依赖包逻辑名（spec：包名作为逻辑身份）。
                if (!dep.TryGetValue("name", out string depName) || string.IsNullOrEmpty(depName))
                {
                    throw new FUIException($"[PackageLoader] 包 {record.PackageName} 的第 {i} 个依赖缺少 name 字段");
                }

                // 兄弟依赖隔离：传入当前 chain 的有序副本，使兄弟依赖不互相误判为环。
                // 副本中已含当前包名及上游链路，递归进入依赖后会先把依赖名加入副本再检测。
                List<string> childChain = new List<string>(chain);

                // 递归 Acquire 依赖包。命中已 Ready 记录时直接返回独立 lease（共享依赖只加载一次）；
                // 命中加载中记录时合并等待同一次加载；检测到环时抛出 FUIException 含完整有序依赖链。
                PackageLease depLease = await AcquireAsync(depName, provider, cancellationToken, childChain);

                // 将依赖租约纳入当前包记录，由最终释放阶段按反向顺序释放（design.md 决策9）。
                record.DependencyLeases.Add(depLease);
                // 同时登记到本次操作账本，供失败时反向回滚本次新增的依赖引用。
                ledger.RecordDependencyLease(depLease);
            }
        }

        /// <summary>
        /// Acquire 失败时的统一处理：调用操作账本按反向顺序原子回滚本次新增的全部所有权、
        /// 清空 record 半就绪引用使重试可从干净状态开始、标记记录 Failed、向所有合并等待方传播异常。
        /// </summary>
        /// <param name="record">本次加载的包记录。</param>
        /// <param name="ledger">本次 Acquire 操作账本，记录了本次新增的全部所有权资源。</param>
        /// <param name="ex">失败异常。</param>
        /// <remarks>
        /// 反向原子回滚（4.8，design.md 决策9，spec：失败和取消执行原子回滚）：
        /// 调用 <see cref="LoadOperationLedger.Rollback"/> 按反向顺序释放本次新增所有权：
        /// 依赖 lease → 外部资源 handle → UIPackage.RemovePackage → 描述 handle。
        /// 账本只持有本次 Acquire 新增的资源引用，不触碰其他调用方此前已持有的共享依赖 lease、
        /// 共享 handle 或已就绪的 UIPackage（spec：已有共享依赖持有者继续正常使用）。
        ///
        /// Failed 记录半就绪状态清理（4.8，验收标准7）：
        /// 回滚后账本已 Dispose 本次新增的 handle 与移除 UIPackage 注册，需同步清空 record 中
        /// 对这些资源的引用（Package、DescHandle、AssetHandles、DependencyLeases 中本次新增项），
        /// 使 record 回到“未持有任何资源”的干净状态。重试 Acquire 时 GetOrCreateRecord 命中该 record，
        /// 因 State=Failed 且 Package=null，会进入加载执行者分支从干净状态重新加载，
        /// 不会误用已释放的 handle 或已移除的 UIPackage。
        ///
        /// 等待方传播：通过 SharedLoadTask.TrySetException 向所有合并等待同一次加载的调用方传播异常，
        /// 使并发调用方一并收到失败结果。OperationCanceledException 由 TrySetException 内部转为取消态。
        /// </remarks>
        private static void OnAcquireFailed(PackageRecord record, LoadOperationLedger ledger, Exception ex)
        {
            // 标记失败并记录上下文，供诊断与防止半就绪记录被使用。
            record.State = PackageLoadState.Failed;
            record.FailureContext = ex?.Message ?? ex?.GetType().Name ?? "未知错误";

            // 调用操作账本按反向顺序原子回滚本次新增的全部所有权（依赖 lease → 外部 handle →
            // RemovePackage → 描述 handle）。只释放本次新增所有权，不触碰其他调用方已持有的共享记录。
            if (ledger != null)
            {
                ledger.Rollback(ex);
            }

            // 清空 record 中本次新增的半就绪引用，使重试可从干净状态开始（验收标准7）。
            // 账本 Rollback 已 Dispose/移除这些资源，record 不再持有对已释放资源的引用。
            record.Package = null;
            record.DescHandle = null;
            record.AssetHandles.Clear();
            record.DependencyLeases.Clear();

            // 向所有合并等待方传播异常。TrySetException 对 OperationCanceledException 自动转为取消态。
            UniTaskCompletionSource<PackageRecord> task = record.SharedLoadTask;
            if (task != null)
            {
                task.TrySetException(ex);
            }
        }

        /// <summary>
        /// 构造依赖环诊断的完整有序依赖链描述字符串。
        /// </summary>
        /// <param name="chain">当前 Acquire 链路中已加载的包名有序列表，保持真实依赖路径顺序。</param>
        /// <param name="cycleEntry">形成环的重复包名。</param>
        /// <returns>形如“A -> B -> C -> A（环入口：A）”的依赖链描述。</returns>
        /// <remarks>
        /// 4.8 增强：链路由 HashSet 改为有序 List，使诊断输出真实依赖路径而非无序成员。
        /// chain 按递归进入顺序追加包名，故遍历即得到“A -> B -> C”形式的真实路径，
        /// 再追加 cycleEntry 形成完整环描述，便于定位环结构。
        /// </remarks>
        private static string BuildDependencyChainText(List<string> chain, string cycleEntry)
        {
            // 有序列表按进入顺序拼接，输出真实依赖路径。
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int count = chain.Count;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" -> ");
                }

                sb.Append(chain[i]);
            }

            sb.Append(" -> ").Append(cycleEntry).Append("（环入口：").Append(cycleEntry).Append("）");
            return sb.ToString();
        }

        /// <summary>
        /// 创建绑定到指定 <see cref="PackageRecord"/> 的同步资源解析器。
        /// </summary>
        /// <param name="record">包记录，其 <see cref="PackageRecord.AssetHandles"/> 作为同步资产来源。</param>
        /// <returns>FairyGUI <see cref="UIPackage.LoadResource"/> 回调。</returns>
        /// <remarks>
        /// resolver 行为契约（design.md 决策8）：
        /// - 只从已完成的 handle 表返回资产，不触发新的异步加载；
        /// - 设置 <see cref="DestroyMethod.None"/>，资产不由 FairyGUI 销毁，所有权由 PackageRecord 统一持有；
        /// - 资源未就绪（handle 表无此 key 或 handle 未完成/已释放）时返回 null，
        ///   FairyGUI 会输出警告并使用占位资产，但不阻塞构造流程。
        ///
        /// 资源名映射：FairyGUI 传入的 name 等价于 <c>Path.GetFileNameWithoutExtension(item.file)</c>
        /// （见 <see cref="UIPackage.LoadAtlas"/>/LoadSound/LoadBinary 的 fileName 计算），
        /// 与 spec“外部项按 Path.GetFileNameWithoutExtension(item.file) 映射规范 location”一致，
        /// 故直接以 name 作为 handle 表的查询 key。
        /// 为健壮性，若 name 含扩展名则再用 <see cref="Path.GetFileNameWithoutExtension"/> 归一化后查询一次。
        /// </remarks>
        private static UIPackage.LoadResource CreateSynchronousResolver(PackageRecord record)
        {
            return (string name, string extension, System.Type type, out DestroyMethod destroyMethod) =>
            {
                // 资产不由 FairyGUI 销毁；所有权由 PackageRecord.AssetHandles 统一持有，在最终释放时 Dispose。
                destroyMethod = DestroyMethod.None;

                // 同步解析：只从已加载 handle 表返回，绝不触发新的异步加载（design.md 决策8）。
                object asset = ResolveFromHandleTable(record, name);

                // 健壮性回退：若 name 意外带扩展名，按规范 location 规则归一化后再查一次。
                if (asset == null && !string.IsNullOrEmpty(extension) && name != null && name.EndsWith(extension, StringComparison.Ordinal))
                {
                    string normalized = Path.GetFileNameWithoutExtension(name);
                    if (!string.IsNullOrEmpty(normalized) && normalized != name)
                    {
                        asset = ResolveFromHandleTable(record, normalized);
                    }
                }

                return asset;
            };
        }

        /// <summary>
        /// 从 <see cref="PackageRecord.AssetHandles"/> 查询指定 location 的资产并按类型返回。
        /// </summary>
        /// <param name="record">包记录。</param>
        /// <param name="location">资源 location，等价于 <c>Path.GetFileNameWithoutExtension(item.file)</c>。</param>
        /// <returns>匹配的资产对象；handle 表无此 key、handle 未完成、已释放或类型不匹配时返回 null。</returns>
        /// <remarks>
        /// 只读查询，不触发任何加载。handle 未完成时返回 null 而非等待，
        /// 保证 resolver 始终同步返回（design.md 决策8：resolver 只从已完成的 handle 表返回资产）。
        /// </remarks>
        private static object ResolveFromHandleTable(PackageRecord record, string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return null;
            }

            Dictionary<string, IFUIAssetHandle> handles = record.AssetHandles;
            if (handles == null)
            {
                return null;
            }

            if (!handles.TryGetValue(location, out IFUIAssetHandle handle))
            {
                // handle 表尚无此资源（预载未完成或本任务阶段未填入），返回 null。
                return null;
            }

            if (handle == null)
            {
                return null;
            }

            // 仅从已完成的 handle 返回资产；未完成时返回 null，避免返回半就绪资产。
            if (!handle.IsDone)
            {
                return null;
            }

            // AssetObject 已涵盖类型转换语义：底层为 UnityEngine.Object，
            // FairyGUI 会按 type（Texture/AudioClip/TextAsset）做进一步 cast。
            UnityEngine.Object assetObject = handle.AssetObject;
            return assetObject;
        }

        /// <summary>
        /// 并发预载包内全部外部资源项（Atlas/Sound/Misc），并将 handle 写入 <see cref="PackageRecord.AssetHandles"/>。
        /// </summary>
        /// <param name="record">目标包记录，预载 handle 写入其 <see cref="PackageRecord.AssetHandles"/>。</param>
        /// <param name="package">已注册的 <see cref="UIPackage"/>，用于枚举外部资源项。</param>
        /// <param name="provider">资源 provider，用于异步加载外部资源。</param>
        /// <param name="ledger">本次 Acquire 操作账本，写表成功的 handle 登记其中供失败时反向回滚。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <remarks>
        /// 实现依据（design.md 决策8、spec“Acquire 成功代表资源已经可用于首屏构造”）：
        /// - 枚举 <see cref="UIPackage.GetItems"/>，筛选带外部文件的外部资源项（Atlas/Sound/Misc）；
        /// - 对每个外部项按 <c>Path.GetFileNameWithoutExtension(item.file)</c> 计算 location，
        ///   与 resolver 查询 key 完全对齐（spec 命名统一规则）；
        /// - 通过 <see cref="IFUIResourceProvider"/> 并发加载对应类型 handle，使用 <see cref="UniTask.WhenAll"/> 合并等待；
        /// - 全部 handle 写入 <see cref="PackageRecord.AssetHandles"/>，确保返回时均处于完成状态；
        /// - 每个写表成功的 handle 同步登记到 <paramref name="ledger"/>，失败时由账本 Rollback 反向 Dispose
        ///   （4.8，spec：失败和取消执行原子回滚）。
        ///
        /// 并发安全：handle 表在 PackageManager 单线程驱动下访问，预载阶段无并发写入，
        /// 可安全在 WhenAll 完成后统一写入。单个 handle 加载任务内部不写表，仅返回 handle 与 location，
        /// 由主流程在 WhenAll 后统一写表，避免并发字典写入。
        ///
        /// 失败回滚分工（4.8）：
        /// - 已写表并登记到账本的 handle：由账本 Rollback 在 Acquire 失败时统一 Dispose；
        /// - 已成功加载但尚未写表的 handle（loadedHandles 中、写表循环前或中途失败）：由本方法 catch
        ///   直接 Dispose，避免无所有者 handle，且不登记到账本（账本只记录已入表所有权）。
        /// </remarks>
        /// <exception cref="FUIException">任一外部资源加载失败或未完成时抛出，含失败 location 与原因。</exception>
        private static async UniTask PreloadExternalAssetsAsync(PackageRecord record, UIPackage package, IFUIResourceProvider provider, LoadOperationLedger ledger, CancellationToken cancellationToken)
        {
            // 枚举包内全部资源项。GetItems 返回 FairyGUI 内部 _items 列表引用，只读遍历不修改。
            List<PackageItem> items = package.GetItems();
            if (items == null || items.Count == 0)
            {
                // 无资源项（纯描述包）无需预载，直接返回。
                return;
            }

            // 收集需要预载的外部资源项及其 location 与目标类型。
            // 仅 Atlas/Sound/Misc 在 LoadPackage 中被赋予带包名前缀的外部 file，其余类型无独立外部文件。
            List<ExternalAssetEntry> entries = new List<ExternalAssetEntry>();
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                PackageItem item = items[i];
                if (item == null)
                {
                    continue;
                }

                // 仅处理带外部文件的外部资源项；file 为空或 null 表示该项无独立外部资源。
                if (string.IsNullOrEmpty(item.file))
                {
                    continue;
                }

                System.Type assetType = GetExternalAssetType(item.type);
                if (assetType == null)
                {
                    // 非 Atlas/Sound/Misc 类型不预载（Image 引用图集、Component 仅含 rawData 等）。
                    continue;
                }

                // location = Path.GetFileNameWithoutExtension(item.file)，与 resolver 查询 key 对齐。
                // item.file 在 LoadPackage 中已被拼为 {PackageName}_{原file}，故 location 唯一且符合命名规则。
                string location = Path.GetFileNameWithoutExtension(item.file);
                if (string.IsNullOrEmpty(location))
                {
                    // file 仅含扩展名等异常情况，跳过避免空 key 污染 handle 表。
                    continue;
                }

                entries.Add(new ExternalAssetEntry { Location = location, AssetType = assetType });
            }

            if (entries.Count == 0)
            {
                // 无外部资源项（如纯组件包）无需预载，直接返回。
                return;
            }

            // 并发加载全部外部资源 handle。每个子任务只返回加载结果，不写 handle 表，避免并发写入。
            UniTask<ExternalAssetLoadResult>[] loadTasks = new UniTask<ExternalAssetLoadResult>[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                ExternalAssetEntry entry = entries[i];
                loadTasks[i] = LoadSingleExternalAssetAsync(provider, entry, cancellationToken);
            }

            // 使用 UniTask.WhenAll 并发等待全部外部资源加载完成（design.md 决策8：并发加载 AssetHandle）。
            ExternalAssetLoadResult[] results = await UniTask.WhenAll(loadTasks);

            // WhenAll 完成后统一写表。若任一失败，先 Dispose 本次已成功加载但未写表的 handle，再抛出异常。
            // 收集本次成功加载的 handle，用于失败回滚未入表部分（spec：失败和取消执行原子回滚）。
            List<IFUIAssetHandle> loadedHandles = new List<IFUIAssetHandle>(results.Length);
            try
            {
                for (int i = 0; i < results.Length; i++)
                {
                    ExternalAssetLoadResult result = results[i];
                    IFUIAssetHandle handle = result.Handle;
                    if (!result.Success)
                    {
                        // 加载失败：构造失败上下文并抛出，触发下方 catch 回滚本次已成功 handle。
                        // 失败原因由 LoadSingleExternalAssetAsync 捕获异常时填入 FailureReason。
                        string reason = result.FailureReason;
                        throw new FUIException($"[PackageLoader] 外部资源预载失败：{record.PackageName}/{result.Location}" +
                            (string.IsNullOrEmpty(reason) ? string.Empty : $"，错误：{reason}"));
                    }

                    if (handle == null)
                    {
                        throw new FUIException($"[PackageLoader] 外部资源预载返回空句柄：{record.PackageName}/{result.Location}");
                    }

                    // 预载完成校验：handle 必须已完成，确保 Acquire 成功前全部进入 Ready
                    // （spec：资源仍未就绪时 Acquire SHALL 保持未完成状态）。
                    if (!handle.IsDone)
                    {
                        handle.Dispose();
                        throw new FUIException($"[PackageLoader] 外部资源预载未完成：{record.PackageName}/{result.Location}");
                    }

                    loadedHandles.Add(handle);
                }

                // 全部成功后统一写入 handle 表，并在每条写表成功后登记到操作账本。
                Dictionary<string, IFUIAssetHandle> handles = record.AssetHandles;
                for (int i = 0; i < results.Length; i++)
                {
                    ExternalAssetLoadResult result = results[i];
                    if (!result.Success || result.Handle == null)
                    {
                        continue;
                    }

                    // 幂等写入：若同 key 已存在旧 handle（理论上首载不应发生），先 Dispose 旧的避免泄漏。
                    if (handles.TryGetValue(result.Location, out IFUIAssetHandle existing) && existing != null && existing != result.Handle)
                    {
                        existing.Dispose();
                    }
                    handles[result.Location] = result.Handle;

                    // 写表成功后登记到本次操作账本，使失败时账本 Rollback 按 Reverse 顺序 Dispose 该 handle（4.8）。
                    // 已登记的 handle 不再由本方法 catch 处理，避免重复 Dispose。
                    ledger.RecordExternalHandle(result.Handle);

                    // 已登记到账本的 handle 从 loadedHandles 移除，确保 catch 只 Dispose 未入表所有权。
                    loadedHandles.Remove(result.Handle);
                }
            }
            catch
            {
                // 预载失败或取消：Dispose 本次已成功加载但尚未写表/登记的 handle，避免无所有者 handle。
                // 已写表并登记到账本的 handle 不在此处 Dispose，由账本 Rollback 在 Acquire 失败时统一处理
                // （design.md 决策9：回滚只释放本次新增所有权，不触碰其他调用方已持有的共享记录）。
                // loadedHandles 此时只保留未入表的 handle（入表的已在写表循环中移除）。
                for (int i = 0; i < loadedHandles.Count; i++)
                {
                    IFUIAssetHandle h = loadedHandles[i];
                    if (h != null)
                    {
                        h.Dispose();
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// 异步加载单个外部资源 handle，返回加载结果（不写 handle 表）。
        /// </summary>
        /// <param name="provider">资源 provider。</param>
        /// <param name="entry">外部资源项信息（location 与目标类型）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>加载结果，含 handle 与成功标志；provider 抛出异常时捕获为失败结果，便于 WhenAll 统一处理。</returns>
        /// <remarks>
        /// 将 provider 抛出的异常捕获为失败结果，避免单个资源失败导致 WhenAll 提前抛出而无法
        /// Dispose 并发中已成功的 handle。捕获后由 <see cref="PreloadExternalAssetsAsync"/> 统一回滚。
        /// 取消异常（<see cref="OperationCanceledException"/>）同样捕获为失败，由上层按取消语义处理。
        /// </remarks>
        private static async UniTask<ExternalAssetLoadResult> LoadSingleExternalAssetAsync(IFUIResourceProvider provider, ExternalAssetEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                // 按目标类型调用 provider 加载 handle。provider 内部响应取消与失败语义。
                IFUIAssetHandle handle;
                if (entry.AssetType == typeof(Texture))
                {
                    handle = await provider.LoadAssetAsyncHandle<Texture>(entry.Location, cancellationToken);
                }
                else if (entry.AssetType == typeof(AudioClip))
                {
                    handle = await provider.LoadAssetAsyncHandle<AudioClip>(entry.Location, cancellationToken);
                }
                else
                {
                    // Misc 及其他二进制资源按 TextAsset 加载（FairyGUI LoadBinary 使用 TextAsset）。
                    handle = await provider.LoadAssetAsyncHandle<TextAsset>(entry.Location, cancellationToken);
                }

                return new ExternalAssetLoadResult { Location = entry.Location, Handle = handle, Success = true };
            }
            catch (Exception ex)
            {
                // 捕获异常为失败结果，保留 location 供上层诊断。handle 为 null 表示未取得句柄。
                string reason = ex is OperationCanceledException ? "已取消" : ex.Message;
                return new ExternalAssetLoadResult { Location = entry.Location, Handle = null, Success = false, FailureReason = reason };
            }
        }

        /// <summary>
        /// 根据 FairyGUI <see cref="PackageItemType"/> 返回对应的外部资源加载类型。
        /// </summary>
        /// <param name="type">资源项类型。</param>
        /// <returns>对应 Unity 资源类型；非外部资源类型返回 null。</returns>
        /// <remarks>
        /// 映射依据 FairyGUI 源码：
        /// - <see cref="PackageItemType.Atlas"/> → <see cref="Texture"/>（LoadAtlas 使用 typeof(Texture)）；
        /// - <see cref="PackageItemType.Sound"/> → <see cref="AudioClip"/>（LoadSound 使用 typeof(AudioClip)）；
        /// - <see cref="PackageItemType.Misc"/> → <see cref="TextAsset"/>（LoadBinary 使用 typeof(TextAsset)）。
        /// </remarks>
        private static System.Type GetExternalAssetType(PackageItemType type)
        {
            switch (type)
            {
                case PackageItemType.Atlas:
                    return typeof(Texture);
                case PackageItemType.Sound:
                    return typeof(AudioClip);
                case PackageItemType.Misc:
                    return typeof(TextAsset);
                default:
                    // Image/MovieClip/Font/Component/Swf/Spine/DragoneBones/Unknown 不在本版预载范围。
                    return null;
            }
        }

        /// <summary>
        /// 外部资源预载项信息（location 与目标类型）。
        /// </summary>
        private struct ExternalAssetEntry
        {
            /// <summary>资源 location，等价于 <c>Path.GetFileNameWithoutExtension(item.file)</c>。</summary>
            public string Location;

            /// <summary>目标 Unity 资源类型（Texture/AudioClip/TextAsset）。</summary>
            public System.Type AssetType;
        }

        /// <summary>
        /// 单个外部资源加载结果。
        /// </summary>
        private struct ExternalAssetLoadResult
        {
            /// <summary>资源 location。</summary>
            public string Location;

            /// <summary>加载得到的 handle；失败时为 null。</summary>
            public IFUIAssetHandle Handle;

            /// <summary>是否加载成功。</summary>
            public bool Success;

            /// <summary>失败原因（成功时为空）。</summary>
            public string FailureReason;
        }
    }
}
