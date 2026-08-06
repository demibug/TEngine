using System;
using FairyGUI;
using UnityEngine;

namespace GameFUI
{
    /// <summary>
    /// 在 <c>GRoot</c> 下建立 GameFUI 自有的固定层级容器集合，并在每层内提供 Full/Safe 两个子容器。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策7（GameFUI 使用独立固定层容器）。
    /// <para>
    /// GameFUI 定义自己的 <see cref="FUILayer"/>：Background、Normal、Popup、Guide、Tips、System。
    /// 每层在 <c>GRoot</c> 下有一个稳定容器（<see cref="GComponent"/>），窗口只在所属容器内调整 child index，
    /// 不在 <c>GRoot</c> 顶层与其他系统对象混合排序。GameFUI 不引用位于 GameLogic 的 <c>UILayer</c>。
    /// </para>
    /// <para>
    /// 本类负责：
    /// <list type="bullet">
    /// <item>按 <see cref="FUILayer"/> 枚举顺序在 <c>GRoot</c> 下创建六个固定层级容器；</item>
    /// <item>每个层级容器铺满 <c>GRoot</c>（通过 <see cref="GObject.AddRelation"/> 绑定 Size 关系），
    /// 并设置为不透明 <c>opaque=false</c>、不裁剪，使其只作为排序分层容器而不影响渲染；</item>
    /// <item>在每个层级容器下建立 Full 和 Safe 两个子容器（任务 5.3）：
    /// Full 子容器铺满层级容器，不受安全区影响；Safe 子容器根据 <c>Screen.safeArea</c> 调整大小和位置，
    /// 受安全区约束。需要安全区的窗口进入 Safe 子容器，其余进入 Full 子容器。
    /// GRoot 本身不缩成安全区（design.md 决策7）；</item>
    /// <item>监听 <see cref="Stage.onStageResized"/>，在分辨率、方向或 safeArea 变化时重新计算所有 Safe 子容器的
    /// 位置和大小，使 Safe 子容器始终对齐当前 <c>Screen.safeArea</c>（在 GRoot 内容坐标系下）；</item>
    /// <item>提供按 <see cref="FUILayer"/> + <see cref="FUISafeAreaMode"/> 获取子容器的查询接口，
    /// 供后续任务（5.4 窗口挂载、5.11 层内排序与全屏遮挡）使用。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 边界约束：本类型只依赖 <c>FairyGUI</c> 与 <c>UnityEngine</c> 命名空间，不 using 且不反向依赖
    /// GameLogic/GamePlay/GameBattle。<c>Screen.safeArea</c> 在 Editor 模式下也可用（返回屏幕全尺寸 Rect），
    /// 因此本实现不区分 Editor 与 Player 平台。
    /// </para>
    /// <para>
    /// 生命周期：由 <see cref="FUIModule"/> 在初始化时创建（<see cref="Create"/>），并在模块 Shutdown 时通过
    /// <see cref="Dispose"/> 从 <c>GRoot</c> 移除并释放。重复 <see cref="Create"/> 在已创建状态下抛
    /// <see cref="FUIException"/>；<see cref="Dispose"/> 后本实例不可再用。
    /// </para>
    /// </remarks>
    internal sealed class FUILayerContainer : IDisposable
    {
        /// <summary>
        /// 固定层级数量，与 <see cref="FUILayer"/> 枚举值个数一致。
        /// </summary>
        private const int LayerCount = 6;

        /// <summary>
        /// 每层内子容器数量（Full 与 Safe 两个）。
        /// </summary>
        private const int SubContainerCount = 2;

        /// <summary>
        /// 六个固定层级容器，索引对应 <see cref="FUILayer"/> 枚举值（0=Background ... 5=System）。
        /// <para>每个容器为铺满 <c>GRoot</c> 的 <see cref="GComponent"/>，作为该层窗口的统一父节点。</para>
        /// </summary>
        private readonly GComponent[] _layerRoots = new GComponent[LayerCount];

        /// <summary>
        /// 每层下的两个子容器，第一维索引对应 <see cref="FUILayer"/>，第二维索引对应 <see cref="FUISafeAreaMode"/>
        /// （0=Full，1=Safe）。
        /// <para>
        /// Full 子容器铺满层级容器，不受安全区影响；Safe 子容器根据 <c>Screen.safeArea</c> 调整大小和位置。
        /// 窗口按其描述中的 <see cref="FUISafeAreaMode"/> 挂载到对应子容器。
        /// </para>
        /// </summary>
        private readonly GComponent[,] _subContainers = new GComponent[LayerCount, SubContainerCount];

        /// <summary>
        /// 本实例是否已创建层级容器。重复创建在已创建状态下抛 <see cref="FUIException"/>。
        /// </summary>
        private bool _created;

        /// <summary>
        /// 本实例是否已 Dispose。Dispose 后任何操作抛 <see cref="FUIException"/>。
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// <see cref="Stage.onStageResized"/> 事件回调句柄，用于 Dispose 时反注册，避免泄漏。
        /// </summary>
        private EventCallback1 _onStageResizedCallback;

        /// <summary>
        /// 延迟一帧重算用的 Timers 回调句柄，用于在多次 onStageResized 合并到一帧后取消未触发的旧调度。
        /// </summary>
        private TimerCallback _deferredApplyCallback;

        /// <summary>
        /// 在 <c>GRoot</c> 下按 <see cref="FUILayer"/> 枚举顺序创建六个固定层级容器，并在每层下建立 Full/Safe 子容器。
        /// </summary>
        /// <remarks>
        /// 创建顺序固定为 Background -> Normal -> Popup -> Guide -> Tips -> System，
        /// 调用 <c>GRoot.inst.AddChild</c> 依次追加，使 <c>GRoot</c> 的 child index 自然按层级递增。
        /// 每个层级容器：
        /// <list type="bullet">
        /// <item>命名设为 <c>FUI_Layer_{层级名}</c>，便于 Editor 层级面板诊断；</item>
        /// <item><c>opaque=false</c>，避免层级容器本身参与渲染遮盖；</item>
        /// <item>通过 <see cref="GObject.AddRelation"/> 绑定到 <c>GRoot.inst</c> 的
        /// <see cref="RelationType.Size"/>，使层级容器随 <c>GRoot</c> 尺寸变化自适应铺满；</item>
        /// <item>初始尺寸设为 <c>GRoot.inst</c> 当前宽高，保证绑定前的首帧正确。</item>
        /// </list>
        /// <para>
        /// 每个层级容器下建立 Full 与 Safe 两个 <see cref="GComponent"/> 子容器：
        /// <list type="bullet">
        /// <item>Full 子容器命名 <c>FUI_Layer_{层级名}_Full</c>，<c>opaque=false</c>，通过
        /// <see cref="RelationType.Size"/> 绑定到层级容器，铺满层级容器，不受安全区影响；</item>
        /// <item>Safe 子容器命名 <c>FUI_Layer_{层级名}_Safe</c>，<c>opaque=false</c>，不绑定 Size 关系
        /// （因为其大小由安全区驱动，需要在重算时显式设置），由 <see cref="ApplySafeAreaToAll"/> 初始化位置和大小。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 创建完成后注册 <see cref="Stage.onStageResized"/> 回调，在分辨率、方向或 safeArea 变化时重算 Safe 子容器。
        /// 安全区重算在创建阶段同步执行一次，保证 Safe 子容器在首个窗口挂载前已对齐当前 <c>Screen.safeArea</c>。
        /// </para>
        /// <para>
        /// 本方法幂等：已创建状态下重复调用抛 <see cref="FUIException"/>，避免重复挂载导致 child index 混乱。
        /// </para>
        /// </remarks>
        /// <exception cref="FUIException">本实例已创建、已 Dispose 或 <c>GRoot.inst</c> 不可用。</exception>
        public void Create()
        {
            ThrowIfDisposed();

            if (_created)
            {
                throw new FUIException("FUILayerContainer 已创建，禁止重复创建。");
            }

            GRoot root = GRoot.inst;

            // 按 FUILayer 枚举顺序（0..5）依次创建并追加到 GRoot，使 child index 与层级顺序一致。
            for (int i = 0; i < LayerCount; i++)
            {
                FUILayer layer = (FUILayer)i;
                GComponent layerRoot = new GComponent
                {
                    name = "FUI_Layer_" + layer,
                    opaque = false,
                };

                // 初始尺寸铺满 GRoot，再通过 Size 关系保持自适应（分辨率/方向变化时由 GRoot 驱动）。
                layerRoot.SetSize(root.width, root.height);
                layerRoot.AddRelation(root, RelationType.Size);

                root.AddChild(layerRoot);
                _layerRoots[i] = layerRoot;

                // 在该层级容器下建立 Full 与 Safe 两个子容器。
                CreateSubContainers(layerRoot, layer);
            }

            // 注册 Stage.onStageResized 回调：分辨率、方向或 safeArea 变化时重算 Safe 子容器。
            // onStageResized 在 Stage.HandleScreenSizeChanged 中派发，覆盖 Screen 宽高与 unitsPerPixel 变化，
            // 包含分辨率变化、设备方向变化以及由 Screen.safeArea 改变触发的 Stage 重建（参见 StageCamera）。
            _onStageResizedCallback = OnStageResized;
            Stage.inst.onStageResized.Add(_onStageResizedCallback);

            // 先标记已创建，再执行首次安全区重算：ApplySafeAreaToAll 内有 _created 守卫，
            // 必须在置位后调用才能实际计算 Safe 子容器位置与大小，使首帧即对齐 Screen.safeArea。
            _created = true;

            // 创建阶段同步执行一次安全区重算，保证 Safe 子容器在首帧前已对齐当前 Screen.safeArea。
            ApplySafeAreaToAll();
        }

        /// <summary>
        /// 获取指定层级的固定容器。
        /// </summary>
        /// <param name="layer">目标层级。</param>
        /// <returns>该层级的 <see cref="GComponent"/> 容器。</returns>
        /// <exception cref="FUIException">本实例未创建、已 Dispose 或 <paramref name="layer"/> 非法。</exception>
        /// <remarks>
        /// 供后续任务使用：5.4 窗口通过 <see cref="GetSubContainer"/> 取得子容器后挂载；5.11 在层级容器内做层内排序与全屏遮挡。
        /// </remarks>
        public GComponent GetLayer(FUILayer layer)
        {
            ThrowIfDisposed();
            ThrowIfNotCreated();

            int index = (int)layer;
            if (index < 0 || index >= LayerCount)
            {
                throw new FUIException("非法的 FUILayer 值：" + layer);
            }

            return _layerRoots[index];
        }

        /// <summary>
        /// 获取指定层级与安全区模式对应的子容器。
        /// </summary>
        /// <param name="layer">目标层级。</param>
        /// <param name="safeAreaMode">安全区模式：<see cref="FUISafeAreaMode.Full"/> 返回铺满层级容器的 Full 子容器，
        /// <see cref="FUISafeAreaMode.Safe"/> 返回受 <c>Screen.safeArea</c> 约束的 Safe 子容器。</param>
        /// <returns>对应子容器。</returns>
        /// <exception cref="FUIException">本实例未创建、已 Dispose 或参数非法。</exception>
        /// <remarks>
        /// 窗口按其 <see cref="FUIDescriptor.SafeAreaMode"/> 选择对应子容器挂载，由 5.4/5.5 任务调用。
        /// </remarks>
        public GComponent GetSubContainer(FUILayer layer, FUISafeAreaMode safeAreaMode)
        {
            ThrowIfDisposed();
            ThrowIfNotCreated();

            int layerIndex = (int)layer;
            if (layerIndex < 0 || layerIndex >= LayerCount)
            {
                throw new FUIException("非法的 FUILayer 值：" + layer);
            }

            int modeIndex = (int)safeAreaMode;
            if (modeIndex < 0 || modeIndex >= SubContainerCount)
            {
                throw new FUIException("非法的 FUISafeAreaMode 值：" + safeAreaMode);
            }

            return _subContainers[layerIndex, modeIndex];
        }

        /// <summary>
        /// 把指定窗口移到其所属子容器的最顶层（最后渲染、最前显示），实现层内排序。
        /// </summary>
        /// <param name="window">要置顶的窗口实例。</param>
        /// <param name="layer">窗口所属层级。</param>
        /// <param name="safeAreaMode">窗口安全区模式。</param>
        /// <remarks>
        /// 实现依据：design.md 决策7——窗口只在所属容器内调整 child index。
        /// <para>
        /// 同一层级内的窗口按显示顺序排序：后打开/重新显示的窗口移到子容器的末尾（最顶层），
        /// 使其渲染在同级其他窗口之上。使用 FairyGUI 的 <see cref="GComponent.SetChildIndex"/>
        /// 把窗口移到 <c>numChildren - 1</c> 位置。
        /// </para>
        /// <para>
        /// 本方法只在窗口确实位于该子容器时调整；若窗口不在该子容器（如已被移除或尚未挂载），直接返回。
        /// 避免抛 <see cref="SetChildIndex"/> 的 "Not a child" 异常。
        /// </para>
        /// <para>
        /// 幂等：重复调用同一窗口保持在最顶层。
        /// </para>
        /// </remarks>
        /// <exception cref="FUIException">本实例已 Dispose 或参数非法。</exception>
        public void BringToTopInContainer(FUIWindow window, FUILayer layer, FUISafeAreaMode safeAreaMode)
        {
            ThrowIfDisposed();
            ThrowIfNotCreated();

            if (window == null)
            {
                throw new FUIException("BringToTopInContainer 失败：window 不能为空。");
            }

            GComponent subContainer = GetSubContainer(layer, safeAreaMode);
            if (window.parent != subContainer)
            {
                // 窗口不在该子容器中（可能尚未挂载或已被移除），不调整顺序，直接返回。
                return;
            }

            int currentIndex = subContainer.GetChildIndex(window);
            int lastIndex = subContainer.numChildren - 1;
            if (currentIndex >= 0 && currentIndex != lastIndex)
            {
                subContainer.SetChildIndex(window, lastIndex);
            }
        }

        /// <summary>
        /// 重新计算所有 Safe 子容器的位置和大小，使其对齐当前 <c>Screen.safeArea</c>。
        /// </summary>
        /// <remarks>
        /// 安全区坐标系转换：
        /// <list type="bullet">
        /// <item><c>Screen.safeArea</c> 以 Unity 像素为单位，原点在屏幕左下角（y 向上）；</item>
        /// <item>GRoot 内容坐标系以 <see cref="GRoot.contentScaleFactor"/> 缩放后的内容单位为基准，原点在左上角（y 向下）；</item>
        /// <item>将 safeArea 像素坐标除以 <see cref="GRoot.contentScaleFactor"/> 转为 GRoot 内容坐标，
        /// 并把 y 轴从“左下角向上”翻转为“左上角向下”：<c>safeY = GRoot.height - safeArea.yMax / scaleFactor</c>。</item>
        /// </list>
        /// <para>
        /// 当 <c>Screen.safeArea</c> 与屏幕全尺寸一致（无刘海/挖孔，如 Editor 默认）时，Safe 子容器与 Full 子容器重合，
        /// 不影响窗口布局。当设备存在刘海/底部 Home 条时，Safe 子容器自动避开非安全区域。
        /// </para>
        /// <para>
        /// 本方法对每个 Safe 子容器显式 <see cref="GObject.SetSize"/> 与 <see cref="GObject.SetXY"/>，
        /// 不使用 <see cref="GObject.AddRelation"/>，因为安全区不是简单的 Size 关系，而是位置+大小同时受 rect 约束。
        /// Full 子容器由 Size 关系自动跟随层级容器，不需要本方法处理。
        /// </para>
        /// <para>
        /// 调用时机：Create 阶段同步调用一次；<see cref="Stage.onStageResized"/> 触发时调用；
        /// 后续 5.x 任务如需在窗口挂载前显式刷新也可直接调用。本方法幂等，重复调用只是重算覆盖。
        /// </para>
        /// </remarks>
        /// <exception cref="FUIException">本实例已 Dispose。</exception>
        public void ApplySafeAreaToAll()
        {
            ThrowIfDisposed();
            if (!_created)
            {
                // 尚未创建时无需重算，Create 流程会执行首次重算。
                return;
            }

            GRoot root = GRoot.inst;
            float scaleFactor = GRoot.contentScaleFactor;
            // scaleFactor 在 UIContentScaler.ApplyChange 中计算并保证 >0；这里做保护避免除零。
            if (scaleFactor <= 0f)
            {
                scaleFactor = 1f;
            }

            // 计算 Safe 子容器在 GRoot 内容坐标系下的位置与大小。
            Rect safeArea = Screen.safeArea;
            float safeX = safeArea.x / scaleFactor;
            float safeYFromTop = root.height - safeArea.yMax / scaleFactor;
            float safeWidth = safeArea.width / scaleFactor;
            float safeHeight = safeArea.height / scaleFactor;

            // 保护：safeArea 为 0（极端情况）时退回到铺满，避免 Safe 子容器变成 0 尺寸导致窗口不可见。
            if (safeWidth <= 0f || safeHeight <= 0f)
            {
                safeX = 0f;
                safeYFromTop = 0f;
                safeWidth = root.width;
                safeHeight = root.height;
            }

            for (int i = 0; i < LayerCount; i++)
            {
                GComponent safeContainer = _subContainers[i, (int)FUISafeAreaMode.Safe];
                if (safeContainer == null || safeContainer.isDisposed)
                {
                    continue;
                }

                // 显式设置位置和大小，使其对齐当前安全区。
                safeContainer.SetXY(safeX, safeYFromTop);
                safeContainer.SetSize(safeWidth, safeHeight);
            }
        }

        /// <summary>
        /// 根据当前各层级是否有全屏窗口处于 Open 状态，重新计算并应用全屏遮挡。
        /// </summary>
        /// <param name="fullScreenOpenByLayer">
        /// 布尔数组，索引对应 <see cref="FUILayer"/> 枚举值（0=Background ... 5=System）；
        /// true 表示该层有全屏窗口处于 Open 状态。为 null 或长度不足时按无全屏窗口处理。
        /// </param>
        /// <remarks>
        /// 实现依据：design.md 决策7 与 spec "独立层级和全屏遮挡"。
        /// <para>
        /// 遮挡规则：对每一层 L，若存在更高层级（index 更大）的全屏窗口处于 Open 状态，
        /// 则层 L 的容器 <c>visible=false</c>、<c>touchable=false</c>，否则恢复 <c>visible=true</c>、<c>touchable=true</c>。
        /// </para>
        /// <para>
        /// 关键约束（spec: 打开全屏窗口——不得通过反复移出和加入 Stage 模拟遮挡）：
        /// 只切换层级容器的 visible/touchable，不从 GRoot 移除，保留 Stage 归属。
        /// 窗口实例也不从子容器移除，仅受层级容器 visible=false 间接隐藏。
        /// </para>
        /// <para>
        /// 恢复语义（spec: 关闭顶部全屏窗口——SHALL 重新计算窗口栈，并恢复不再被其他全屏窗口遮挡的窗口）：
        /// 遮挡解除后（全屏窗口关闭或隐藏），层级容器恢复 visible=true、touchable=true。
        /// 层级容器是结构性容器，正常状态始终为 visible=true、touchable=true；
        /// 各窗口自身的 visible/touchable 由 Hide/Show 独立管理，不受层级容器恢复影响
        /// （FairyGUI 中子对象渲染受父对象 visible 约束，但子对象自身 visible 属性不变）。
        /// </para>
        /// <para>
        /// 幂等：重复调用以最新状态为准，不累积副作用。
        /// </para>
        /// </remarks>
        /// <exception cref="FUIException">本实例已 Dispose。</exception>
        public void ApplyFullScreenOcclusion(bool[] fullScreenOpenByLayer)
        {
            ThrowIfDisposed();
            if (!_created)
            {
                return;
            }

            // 计算最高全屏窗口层级（-1 表示无全屏窗口处于 Open 状态）。
            // 遮挡范围：所有 index 小于该最高层的层都被遮挡。
            int highestFullScreenLayer = -1;
            if (fullScreenOpenByLayer != null)
            {
                for (int i = LayerCount - 1; i >= 0; i--)
                {
                    if (i < fullScreenOpenByLayer.Length && fullScreenOpenByLayer[i])
                    {
                        highestFullScreenLayer = i;
                        break;
                    }
                }
            }

            // 对每一层应用遮挡：index < highestFullScreenLayer 的层被遮挡。
            for (int i = 0; i < LayerCount; i++)
            {
                GComponent layerRoot = _layerRoots[i];
                if (layerRoot == null || layerRoot.isDisposed)
                {
                    continue;
                }

                bool occluded = (i < highestFullScreenLayer);

                // 只切换层级容器的 visible/touchable，不从 GRoot 移除，保留 Stage 归属（spec: 不得通过反复移出和加入 Stage 模拟遮挡）。
                layerRoot.visible = !occluded;
                layerRoot.touchable = !occluded;
            }
        }

        /// <summary>
        /// 从 <c>GRoot</c> 移除并释放全部层级容器与子容器，使显示树回到创建前基线。
        /// </summary>
        /// <remarks>
        /// 释放顺序与创建顺序相反（System -> Background），从 <c>GRoot</c> 移除后调用
        /// <see cref="GObject.Dispose"/>，避免层级容器残留。子容器作为层级容器的 child，
        /// 随层级容器一起 Dispose，不需要单独释放。本方法幂等，重复调用直接返回。
        /// <para>
        /// 释放前先反注册 <see cref="Stage.onStageResized"/> 回调，避免 Stage 在 Dispose 后回调到已释放实例。
        /// </para>
        /// <para>
        /// 注意：本方法只释放层级容器与子容器本身，不释放容器内窗口（窗口由 <see cref="FUIModule"/> Shutdown
        /// 流程在调用本方法前完成释放）。
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // 先反注册 Stage.onStageResized 回调，避免 Dispose 后回调到已释放对象。
            if (_onStageResizedCallback != null)
            {
                // Stage.inst 在 Dispose 时可能已不可用，做保护性判断。
                if (Stage.inst != null)
                {
                    Stage.inst.onStageResized.Remove(_onStageResizedCallback);
                }
                _onStageResizedCallback = null;
            }

            // 取消尚未触发的延迟重算调度，避免 Dispose 后 Timers 仍在下一帧回调到本实例。
            if (_deferredApplyCallback != null)
            {
                Timers.inst.Remove(_deferredApplyCallback);
                _deferredApplyCallback = null;
            }

            GRoot root = GRoot.inst;
            for (int i = LayerCount - 1; i >= 0; i--)
            {
                // 清空子容器引用（子容器随 layerRoot 一起 Dispose，不需要单独释放）。
                for (int j = 0; j < SubContainerCount; j++)
                {
                    _subContainers[i, j] = null;
                }

                GComponent layerRoot = _layerRoots[i];
                if (layerRoot == null)
                {
                    continue;
                }

                // 从 GRoot 移除并释放；不调用 UIObjectFactory.Clear()，避免影响其他 FairyGUI 扩展。
                if (layerRoot.parent == root)
                {
                    root.RemoveChild(layerRoot, true);
                }
                else if (!layerRoot.isDisposed)
                {
                    // 容器已被外部移出 GRoot 但未释放时，直接 Dispose 避免泄漏。
                    layerRoot.Dispose();
                }

                _layerRoots[i] = null;
            }

            _created = false;
        }

        /// <summary>
        /// 在指定层级容器下创建 Full 与 Safe 两个子容器。
        /// </summary>
        /// <param name="layerRoot">层级容器，作为子容器的父节点。</param>
        /// <param name="layer">层级枚举，用于命名。</param>
        /// <remarks>
        /// Full 子容器通过 <see cref="RelationType.Size"/> 绑定到层级容器，铺满层级容器，不受安全区影响。
        /// Safe 子容器不绑定 Size 关系，其位置和大小由 <see cref="ApplySafeAreaToAll"/> 显式设置。
        /// 两者 <c>opaque=false</c>，避免参与渲染遮盖。
        /// </remarks>
        private void CreateSubContainers(GComponent layerRoot, FUILayer layer)
        {
            // Full 子容器：铺满层级容器，通过 Size 关系自适应。
            GComponent fullContainer = new GComponent
            {
                name = "FUI_Layer_" + layer + "_Full",
                opaque = false,
            };
            fullContainer.SetSize(layerRoot.width, layerRoot.height);
            fullContainer.AddRelation(layerRoot, RelationType.Size);
            layerRoot.AddChild(fullContainer);
            _subContainers[(int)layer, (int)FUISafeAreaMode.Full] = fullContainer;

            // Safe 子容器：不绑定 Size 关系，位置和大小由 ApplySafeAreaToAll 显式设置。
            // 初始尺寸先铺满层级容器，避免首次 ApplySafeAreaToAll 前出现 0 尺寸窗口。
            GComponent safeContainer = new GComponent
            {
                name = "FUI_Layer_" + layer + "_Safe",
                opaque = false,
            };
            safeContainer.SetSize(layerRoot.width, layerRoot.height);
            layerRoot.AddChild(safeContainer);
            _subContainers[(int)layer, (int)FUISafeAreaMode.Safe] = safeContainer;
        }

        /// <summary>
        /// <see cref="Stage.onStageResized"/> 回调：分辨率、方向或 safeArea 变化时重算 Safe 子容器。
        /// </summary>
        /// <param name="context">事件上下文（未使用）。</param>
        /// <remarks>
        /// <para>
        /// Stage 在 <c>HandleScreenSizeChanged</c> 中先 <c>SetSize</c> 与 <c>localScale</c>，
        /// 再派发 <c>onStageResized</c>，最后才在事件未被 preventDefault 时执行
        /// <c>UIContentScaler.ApplyChange()</c> 与 <c>GRoot.ApplyContentScaleFactor()</c>。
        /// 因此本回调同步执行时，<c>GRoot.height</c> 与 <c>GRoot.contentScaleFactor</c> 仍是旧值，
        /// 而 <c>Screen.safeArea</c> 已是新值，直接调用 <see cref="ApplySafeAreaToAll"/> 会得到错误结果且不会自纠正。
        /// </para>
        /// <para>
        /// 修复方案：本回调只把真正的重算延迟一帧（通过 <c>Timers.inst.CallLater</c>）。
        /// <c>CallLater</c> 在下一帧 <c>Timers.Update</c> 中触发，此时
        /// <c>UIContentScaler.ApplyChange</c> 与 <c>GRoot.ApplyContentScaleFactor</c> 已经在当前帧
        /// 的 <c>onStageResized</c> 派发完成后执行完毕，<c>GRoot.height</c> 与 <c>contentScaleFactor</c>
        /// 已更新为新值，重算结果正确。
        /// </para>
        /// <para>
        /// 多次 onStageResized 在同一帧合并：若已有延迟调度未触发，则先取消旧调度再重新注册，
        /// 避免重复重算；最终只按最后一次 onStageResized 的状态重算一次。
        /// </para>
        /// <para>
        /// Full 子容器由 Size 关系自动跟随层级容器，不需要本回调处理。
        /// </para>
        /// </remarks>
        private void OnStageResized(EventContext context)
        {
            // Dispose 后 Stage 可能仍有迟到回调，做保护性判断避免操作已释放对象。
            if (_disposed || !_created)
            {
                return;
            }

            // 延迟一帧重算：等 UIContentScaler.ApplyChange 与 GRoot.ApplyContentScaleFactor 在本帧内完成。
            // 若已有未触发的旧调度，先取消，避免同一帧多次 onStageResized 产生重复重算。
            if (_deferredApplyCallback == null)
            {
                _deferredApplyCallback = OnDeferredApplySafeArea;
            }
            else
            {
                Timers.inst.Remove(_deferredApplyCallback);
            }

            Timers.inst.CallLater(_deferredApplyCallback);
        }

        /// <summary>
        /// <see cref="Timers.inst.CallLater"/> 回调：在下一帧执行真正的 Safe 子容器重算。
        /// </summary>
        /// <param name="param">未使用。</param>
        /// <remarks>
        /// 此时 <c>GRoot.height</c> 与 <c>GRoot.contentScaleFactor</c> 已由上一帧的
        /// <c>UIContentScaler.ApplyChange</c> 与 <c>GRoot.ApplyContentScaleFactor</c> 更新，
        /// <c>Screen.safeArea</c> 也已是新值，<see cref="ApplySafeAreaToAll"/> 计算结果正确。
        /// </remarks>
        private void OnDeferredApplySafeArea(object param)
        {
            // 延迟回调触发前可能已 Dispose，做保护性判断。
            if (_disposed || !_created)
            {
                return;
            }

            ApplySafeAreaToAll();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new FUIException("FUILayerContainer 已 Dispose，禁止后续操作。");
            }
        }

        private void ThrowIfNotCreated()
        {
            if (!_created)
            {
                throw new FUIException("FUILayerContainer 尚未创建，请先调用 Create。");
            }
        }
    }
}
