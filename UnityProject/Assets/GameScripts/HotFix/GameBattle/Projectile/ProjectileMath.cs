namespace GameBattle
{
    // ============================================================================
    // 任务 5.6：ProjectileMath —— 投射物纯数学函数模块
    // ----------------------------------------------------------------------------
    // 职责（design.md:194 / specs/battle-parity-verification/spec.md）：
    //   提供距离、二次贝塞尔位置和切线角度纯数学函数，供 TargetEnemyBezierMovement
    //   等投射物移动策略复用。本模块是纯计算，不依赖 UnityEngine、不持有状态、
    //   不产生副作用，可在 EditMode 纯逻辑测试中直接验证。
    //
    // 还原工程来源：Origin/reconstructed-project/src/projectiles/ProjectileMath.js
    //   原始符号：np.bs（distance）/ np.Ms（distanceSquared）/ np.angle（displayAngle）
    //             np.Rs（quadraticTangentDegrees）/ np.Us（quadraticBezier）
    //   重建状态：COMPLETE_FOR_SIMPLE_DYNAMIC_ARROW
    //
    // 等价性策略（spec "Differences use explicit comparison rules"）：
    //   1. JS Number 为 IEEE 754 double，C# double 同为 IEEE 754 double；
    //      相同输入与相同运算顺序下逐位等价，浮点字段使用显式声明容差比较。
    //   2. 严格复制 JS 的分支判断与运算顺序，避免引入 C# 侧“优化”造成行为偏差。
    //   3. 不引入点/向量类型，采用裸 double 入参，保证模块零依赖且不与后续
    //      ProjectileBase/TargetEnemyBezierMovement（task 5.7）的类型决策耦合。
    //
    // 不变量：
    //   1. 全部成员为 internal static 纯函数，无字段、无状态。
    //   2. 角度单位为度：DisplayAngle 0° 朝上、90° 朝右（与 JS np.angle 一致）；
    //      QuadraticTangentDegrees 为标准数学切线角（与 JS np.Rs 一致）。
    //   3. QuadraticBezier 返回值语义与 JS 一致：progress >= 1 时返回 true。
    // ============================================================================

    /// <summary>
    /// 投射物纯数学函数集合。提供距离、二次贝塞尔位置与切线角度计算，
    /// 对应还原工程 <c>ProjectileMath.js</c> 的五个导出函数。
    /// </summary>
    /// <remarks>
    /// <para>本模块无任何依赖，仅使用 <see cref="System.Math"/>。所有入参为裸 <see cref="double"/>，
    /// 避免引入点/向量类型与后续投射物类型（task 5.7）耦合。</para>
    /// <para>浮点行为与 JS 逐位等价：JS Number 与 C# double 同为 IEEE 754 double，
    /// 相同输入和运算顺序下结果一致；离散与浮点字段级容差由测试显式声明（spec
    /// "Differences use explicit comparison rules"）。</para>
    /// </remarks>
    internal static class ProjectileMath
    {
        // ====================================================================
        // 距离
        // ====================================================================

        /// <summary>
        /// 计算两点间欧氏距离，对应 JS <c>distance(a, b)</c>。
        /// </summary>
        /// <param name="ax">起点 X。</param>
        /// <param name="ay">起点 Y。</param>
        /// <param name="bx">终点 X。</param>
        /// <param name="by">终点 Y。</param>
        /// <returns>两点间距离 <c>sqrt(dx*dx + dy*dy)</c>。</returns>
        /// <remarks>运算顺序与 JS 一致：<c>dx = ax - bx; dy = ay - by; sqrt(dx*dx + dy*dy)</c>。</remarks>
        internal static double Distance(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx;
            double dy = ay - by;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 计算两点间距离平方，对应 JS <c>distanceSquared(a, b)</c>。
        /// </summary>
        /// <param name="ax">起点 X。</param>
        /// <param name="ay">起点 Y。</param>
        /// <param name="bx">终点 X。</param>
        /// <param name="by">终点 Y。</param>
        /// <returns>距离平方 <c>dx*dx + dy*dy</c>，避免开方开销用于比较。</returns>
        /// <remarks>运算顺序与 JS 一致。</remarks>
        internal static double DistanceSquared(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx;
            double dy = ay - by;
            return dx * dx + dy * dy;
        }

        // ====================================================================
        // 显示角度（0° 朝上，90° 朝右）
        // ====================================================================

        /// <summary>
        /// 计算从 <paramref name="fromX"/>/<paramref name="fromY"/> 指向
        /// <paramref name="toX"/>/<paramref name="toY"/> 的显示角度，
        /// 对应 JS <c>displayAngle(from, to)</c>（原始符号 np.angle）。
        /// </summary>
        /// <param name="fromX">起点 X。</param>
        /// <param name="fromY">起点 Y。</param>
        /// <param name="toX">终点 X。</param>
        /// <param name="toY">终点 Y。</param>
        /// <returns>
        /// 显示角度（度）：0° 朝上，90° 朝右，180° 朝下，270° 朝左。
        /// </returns>
        /// <remarks>
        /// <para>语义与 JS 完全一致：Y 轴向下为正（屏幕坐标），故使用
        /// <c>invertedDy = fromY - toY</c> 翻转 Y 轴使“朝上”为 0°。</para>
        /// <para>分支严格复制 JS：
        /// <list type="bullet">
        /// <item><c>dx == 0</c>：朝上（invertedDy ≥ 0）返回 0，朝下返回 180。</item>
        /// <item><c>invertedDy == 0</c>：朝右（dx &gt; 0）返回 90，朝左返回 270。</item>
        /// <item>否则返回 <c>atan2(dx, invertedDy) * 180 / PI</c>。</item>
        /// </list>
        /// 注意 JS <c>Math.atan2(dx, invertedDy)</c> 第一参数为 dx，C# <see cref="System.Math.Atan2"/>
        /// 第一参数为 y 坐标；此处按 JS 顺序传入 <c>Atan2(dx, invertedDy)</c> 保持等价。</para>
        /// <para>对精确 0.0 的入参使用 <c>== 0.0</c> 比较，与 JS <c>=== 0</c> 行为一致。</para>
        /// </remarks>
        internal static double DisplayAngle(double fromX, double fromY, double toX, double toY)
        {
            double dx = toX - fromX;
            double invertedDy = fromY - toY;
            if (dx == 0.0)
            {
                return invertedDy >= 0.0 ? 0.0 : 180.0;
            }

            if (invertedDy == 0.0)
            {
                return dx > 0.0 ? 90.0 : 270.0;
            }

            return System.Math.Atan2(dx, invertedDy) * 180.0 / System.Math.PI;
        }

        // ====================================================================
        // 二次贝塞尔切线角度
        // ====================================================================

        /// <summary>
        /// 计算二次贝塞尔曲线在指定进度处的切线标准角度（度），
        /// 对应 JS <c>quadraticTangentDegrees(start, control, end, progress)</c>（原始符号 np.Rs）。
        /// </summary>
        /// <param name="startX">起点 X。</param>
        /// <param name="startY">起点 Y。</param>
        /// <param name="controlX">控制点 X。</param>
        /// <param name="controlY">控制点 Y。</param>
        /// <param name="endX">终点 X。</param>
        /// <param name="endY">终点 Y。</param>
        /// <param name="progress">进度 t，取值 [0, 1]。</param>
        /// <returns>切线角度（度），标准数学坐标：<c>atan2(dy, dx) * 180 / PI</c>。</returns>
        /// <remarks>
        /// <para>切线导数为 <c>B'(t) = 2(1-t)(C-S) + 2t(E-C)</c>，与 JS 公式逐项一致。</para>
        /// <para>JS <c>Math.atan2(dy, dx)</c> 与 C# <see cref="System.Math.Atan2(dy, dx)"/> 参数顺序一致。</para>
        /// <para>注意：此角度为标准数学切线角，与 <see cref="DisplayAngle"/> 的“0° 朝上”显示角度不同；
        /// 还原工程在 <c>TargetEnemyBezierMovement</c> 中将其 +90 转换为显示旋转。</para>
        /// </remarks>
        internal static double QuadraticTangentDegrees(
            double startX, double startY,
            double controlX, double controlY,
            double endX, double endY,
            double progress)
        {
            double dx = 2.0 * (1.0 - progress) * (controlX - startX)
                        + 2.0 * progress * (endX - controlX);
            double dy = 2.0 * (1.0 - progress) * (controlY - startY)
                        + 2.0 * progress * (endY - controlY);
            return System.Math.Atan2(dy, dx) * 180.0 / System.Math.PI;
        }

        // ====================================================================
        // 二次贝塞尔位置（de Casteljau 插值）
        // ====================================================================

        /// <summary>
        /// 使用 de Casteljau 算法计算二次贝塞尔曲线在指定进度处的位置，
        /// 对应 JS <c>quadraticBezier(start, control, end, output, progress)</c>（原始符号 np.Us）。
        /// </summary>
        /// <param name="startX">起点 X。</param>
        /// <param name="startY">起点 Y。</param>
        /// <param name="controlX">控制点 X。</param>
        /// <param name="controlY">控制点 Y。</param>
        /// <param name="endX">终点 X。</param>
        /// <param name="endY">终点 Y。</param>
        /// <param name="progress">进度 t，取值 [0, 1]。</param>
        /// <param name="outX">输出位置 X（对应 JS <c>output.x</c>）。</param>
        /// <param name="outY">输出位置 Y（对应 JS <c>output.y</c>）。</param>
        /// <returns>
        /// <c>true</c> 表示进度已达到或超过 1（对应 JS 返回值 <c>!(progress &lt; 1)</c>）。
        /// </returns>
        /// <remarks>
        /// <para>de Casteljau 两段线性插值：
        /// <c>first = S + (C - S) * t; second = C + (E - C) * t; out = first + (second - first) * t</c>，
        /// 与 JS 逐行一致。该公式与 Bernstein 形式 <c>(1-t)²S + 2(1-t)tC + t²E</c> 数学等价，
        /// 但运算顺序不同；为保持与 JS 逐位等价，此处采用 de Casteljau 形式。</para>
        /// <para>返回值语义严格复制 JS <c>!(progress &lt; 1)</c>，而非 <c>progress &gt;= 1</c>，
        /// 以在 NaN 输入时保持与 JS 相同行为（NaN 不小于 1，故返回 true）。</para>
        /// </remarks>
        internal static bool QuadraticBezier(
            double startX, double startY,
            double controlX, double controlY,
            double endX, double endY,
            double progress,
            out double outX, out double outY)
        {
            double firstX = startX + (controlX - startX) * progress;
            double firstY = startY + (controlY - startY) * progress;
            double secondX = controlX + (endX - controlX) * progress;
            double secondY = controlY + (endY - controlY) * progress;
            outX = firstX + (secondX - firstX) * progress;
            outY = firstY + (secondY - firstY) * progress;
            return !(progress < 1.0);
        }
    }
}
