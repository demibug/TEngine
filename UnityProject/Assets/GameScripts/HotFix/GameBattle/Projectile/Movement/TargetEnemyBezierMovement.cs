namespace GameBattle
{
    // ============================================================================
    // 任务 5.7：TargetEnemyBezierMovement —— 追踪目标的贝塞尔位移及目标死亡处置
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 202 行 / Projectile/Movement/TargetEnemyBezierMovement.cs）：
    //   实现追踪目标的贝塞尔位移及目标死亡处置，使用 ProjectileMath 的纯函数。
    //   本期为内部具体类，不创建 IProjectileMovement 接口（task 5.7 约束）。
    //
    // 来源证据（还原工程 TargetEnemyBezierMovement.js:1-176）：
    //   - 原始符号 pP → on，重建状态 COMPLETE_FOR_SIMPLE_DYNAMIC_ARROW
    //   - 原始池键：TargetEnemyBezierMovement
    //   - configure({ enemyManager, gameData })：注入依赖
    //   - reset(curveHeight, distanceScaling, smoothRotation, hitRadiusEnabled)：重置参数
    //   - setTargetId(targetId)：设置目标 ID 并刷新目标位置
    //   - attach(projectile)：绑定投射物，计算命中半径；目标缺失则请求移除
    //   - onFire()：progress=0，记录起始位置，计算控制点，设置初始旋转
    //   - update(deltaMs, speed)：
    //       * progressDelta = deltaMs * movementRate * speed / 500
    //       * distanceScaling：progressDelta *= sqrt(max(0.1, currentDist/originalDist))
    //       * 若到目标距离平方 < hitRadiusSquared 或 progress >= 1 → requestRemove
    //       * 否则 quadraticBezier 计算新位置
    //       * 旋转：displayAngle(lastPosition, currentPosition)；smoothRotation 可选
    //       * hitEnabled = progress >= 0.8
    //   - _refreshTargetPosition()：从 enemyManager 查目标；目标丢失保留最后终点
    //   - recover()：重置并入池
    //
    // 决策依据：
    //   - design.md 第 9 行：纯逻辑，不持有 Unity GameObject。
    //   - ProjectileMath（task 5.6）提供 distance/distanceSquared/displayAngle/
    //     quadraticTangentDegrees/quadraticBezier 纯函数，本类直接复用。
    //   - design.md 决策 4：目标查询使用直接调用 EnemyManager.GetById。
    //   - spec battle-simulation "Simulation is reproducible"：不依赖无序集合遍历。
    //   - projectile-pool-reset-contract.md：recover 清除全部状态。
    //
    // 不变量：
    //   1. 纯逻辑：通过 ProjectileBase.SetPosition/SetRotation 写入位置/旋转。
    //   2. 使用 ProjectileMath 纯函数，不自行实现贝塞尔数学。
    //   3. 目标死亡后保留最后终点，最终按旧 ID 尝试命中并安全失效。
    //   4. 使用逻辑时间 stepMs，不依赖 Time.deltaTime。
    // ============================================================================

    /// <summary>
    /// 追踪目标的贝塞尔位移策略及目标死亡处置。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 202 行）：</b>实现追踪目标的贝塞尔位移及目标死亡处置。
    /// 替代还原工程 <c>TargetEnemyBezierMovement.js</c>（原始符号 pP → on，
    /// 重建状态 COMPLETE_FOR_SIMPLE_DYNAMIC_ARROW）。</para>
    ///
    /// <para><b>使用 ProjectileMath 纯函数（task 5.6 复用）：</b>
    /// 距离 <see cref="ProjectileMath.Distance"/>、距离平方 <see cref="ProjectileMath.DistanceSquared"/>、
    /// 显示角度 <see cref="ProjectileMath.DisplayAngle"/>、贝塞尔位置 <see cref="ProjectileMath.QuadraticBezier"/>、
    /// 切线角度 <see cref="ProjectileMath.QuadraticTangentDegrees"/>。</para>
    ///
    /// <para><b>目标死亡处置（TargetEnemyBezierMovement.js:151-162）：</b>
    /// 飞行中丢失目标后保留最后终点（targetMissing=true），最终按旧 ID 尝试命中并安全失效。
    /// 目标位置在 <see cref="OnFire"/> 时锁定，每次 <see cref="Update"/> 刷新（若目标仍存活）。</para>
    ///
    /// <para><b>本期为内部具体类（task 5.7 约束）：</b>不创建 IProjectileMovement 接口。
    /// 出现第二个获准投射物移动策略时再提取接口。</para>
    /// </remarks>
    internal sealed class TargetEnemyBezierMovement
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>进度归一化分母（对应 JS deltaMs * movementRate * speed / 500）。</summary>
        private const double ProgressDenominator = 500.0;

        /// <summary>distanceScaling 的最小距离比钳制（对应 JS Math.max(0.1, ...)）。</summary>
        private const double MinDistanceRatio = 0.1;

        /// <summary>hitEnabled 触发的进度阈值（对应 JS progress >= 0.8）。</summary>
        private const double HitEnabledProgressThreshold = 0.8;

        // ====================================================================
        // 可变状态字段（对应 TargetEnemyBezierMovement.js:19-35 constructor）
        // ====================================================================

        /// <summary>绑定的投射物（对应 projectile）。</summary>
        private ProjectileBase _projectile;

        /// <summary>敌人管理器（对应 enemyManager）。供目标查询。</summary>
        private EnemyManager _enemyManager;

        /// <summary>格子尺寸（px，对应 gameData.map.gridWidth=80）。目标中心点计算用。</summary>
        private float _cellSize;

        /// <summary>目标敌人运行时 ID（对应 targetId）。</summary>
        private int _targetId;

        /// <summary>贝塞尔弧高（对应 curveHeight，默认 50）。</summary>
        private float _curveHeight;

        /// <summary>是否启用距离缩放（对应 distanceScaling，默认 true）。</summary>
        private bool _distanceScaling;

        /// <summary>是否平滑旋转（对应 smoothRotation，默认 false）。</summary>
        private bool _smoothRotation;

        /// <summary>是否启用命中半径检测（对应 hitRadiusEnabled，默认 true）。</summary>
        private bool _hitRadiusEnabled;

        /// <summary>移动速率倍率（对应 movementRate，默认 1）。</summary>
        private double _movementRate;

        /// <summary>归一化进度 [0, 1+]（对应 progress）。</summary>
        private double _progress;

        /// <summary>目标是否缺失（对应 targetMissing）。</summary>
        private bool _targetMissing;

        /// <summary>命中半径平方（对应 hitRadiusSquared）。</summary>
        private double _hitRadiusSquared;

        /// <summary>起始位置 X（对应 startPosition.x）。</summary>
        private double _startX;

        /// <summary>起始位置 Y（对应 startPosition.y）。</summary>
        private double _startY;

        /// <summary>上次位置 X（对应 lastPosition.x）。</summary>
        private double _lastX;

        /// <summary>上次位置 Y（对应 lastPosition.y）。</summary>
        private double _lastY;

        /// <summary>控制点 X（对应 controlPoint.x）。</summary>
        private double _controlX;

        /// <summary>控制点 Y（对应 controlPoint.y）。</summary>
        private double _controlY;

        /// <summary>目标位置 X（对应 targetPosition.x）。</summary>
        private double _targetX;

        /// <summary>目标位置 Y（对应 targetPosition.y）。</summary>
        private double _targetY;

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>归一化进度 [0, 1+]（对应 progress）。</summary>
        internal double Progress => _progress;

        /// <summary>目标是否缺失（对应 targetMissing）。</summary>
        internal bool TargetMissing => _targetMissing;

        /// <summary>目标敌人运行时 ID。</summary>
        internal int TargetId => _targetId;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造一个追踪目标贝塞尔移动策略。字段初始化为默认值。
        /// </summary>
        internal TargetEnemyBezierMovement()
        {
            ResetParameters(50f, true, false, true);
        }

        // ====================================================================
        // Configure —— 注入依赖（对应 TargetEnemyBezierMovement.js:38-43 configure）
        // ====================================================================

        /// <summary>
        /// 注入敌人管理器与格子尺寸。
        /// </summary>
        /// <param name="enemyManager">敌人管理器。不可为 null。</param>
        /// <param name="cellSize">格子尺寸（px，对应 map.gridWidth=80）。目标中心点计算用。</param>
        internal void Configure(EnemyManager enemyManager, float cellSize)
        {
            _enemyManager = enemyManager ?? throw new System.ArgumentNullException(nameof(enemyManager));
            _cellSize = cellSize > 0 ? cellSize : 80f;
        }

        // ====================================================================
        // ResetParameters —— 重置贝塞尔参数（对应 TargetEnemyBezierMovement.js:45-61 reset）
        // ====================================================================

        /// <summary>
        /// 重置贝塞尔曲线参数到默认值。
        /// </summary>
        /// <param name="curveHeight">贝塞尔弧高（默认 50）。</param>
        /// <param name="distanceScaling">是否启用距离缩放（默认 true）。</param>
        /// <param name="smoothRotation">是否平滑旋转（默认 false）。</param>
        /// <param name="hitRadiusEnabled">是否启用命中半径检测（默认 true）。</param>
        internal void ResetParameters(
            float curveHeight,
            bool distanceScaling,
            bool smoothRotation,
            bool hitRadiusEnabled)
        {
            _curveHeight = curveHeight;
            _distanceScaling = distanceScaling;
            _smoothRotation = smoothRotation;
            _hitRadiusEnabled = hitRadiusEnabled;
            _movementRate = 1.0;
            _progress = 0.0;
            _targetMissing = true;
            _hitRadiusSquared = 0.0;
            _targetId = -1;
            _projectile = null;
            _startX = 0.0;
            _startY = 0.0;
            _lastX = 0.0;
            _lastY = 0.0;
            _controlX = 0.0;
            _controlY = 0.0;
            _targetX = 0.0;
            _targetY = 0.0;
        }

        // ====================================================================
        // SetTargetId —— 设置目标并刷新位置（对应 TargetEnemyBezierMovement.js:63-67）
        // ====================================================================

        /// <summary>
        /// 设置目标敌人 ID 并刷新目标位置。
        /// </summary>
        /// <param name="targetId">目标敌人运行时 ID。</param>
        internal void SetTargetId(int targetId)
        {
            _targetId = targetId;
            RefreshTargetPosition();
        }

        // ====================================================================
        // Attach —— 绑定投射物（对应 TargetEnemyBezierMovement.js:69-77 attach）
        // ====================================================================

        /// <summary>
        /// 绑定投射物，计算命中半径。目标缺失则请求移除。
        /// </summary>
        /// <param name="projectile">要绑定的投射物。</param>
        /// <param name="projectileHeight">投射物逻辑高度（用于命中半径计算，对应 renderNode.height）。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>attach(projectile)</c>（TargetEnemyBezierMovement.js:69-77）：
        /// <c>radius = hitRadiusEnabled ? renderNode.height / 1.5 : 0</c>；
        /// 目标缺失时 <c>projectile.requestRemove(true); projectile.hide()</c>。</para>
        /// </remarks>
        internal void Attach(ProjectileBase projectile, float projectileHeight)
        {
            _projectile = projectile;

            double radius = _hitRadiusEnabled ? projectileHeight / 1.5 : 0.0;
            _hitRadiusSquared = radius * radius;

            if (_targetMissing)
            {
                projectile.RequestRemove(true);
            }
        }

        // ====================================================================
        // OnFire —— 发射时初始化（对应 TargetEnemyBezierMovement.js:79-97 onFire）
        // ====================================================================

        /// <summary>
        /// 发射时初始化进度、起始位置、控制点和初始旋转。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>onFire()</c>（TargetEnemyBezierMovement.js:79-97）：</para>
        /// <list type="bullet">
        /// <item>progress = 0</item>
        /// <item>目标缺失则直接返回</item>
        /// <item>lastPosition = startPosition = projectile 位置</item>
        /// <item>刷新目标位置</item>
        /// <item>控制点 = 起终中点向上偏移 curveHeight</item>
        /// <item>旋转 = quadraticTangentDegrees(start, control, target, 0) + 90</item>
        /// </list></para>
        /// </remarks>
        internal void OnFire()
        {
            _progress = 0.0;
            if (_targetMissing)
            {
                return;
            }

            _lastX = _projectile.X;
            _lastY = _projectile.Y;
            _startX = _projectile.X;
            _startY = _projectile.Y;

            RefreshTargetPosition();

            // 控制点 = 起终中点向上偏移 curveHeight（对应 JS controlPoint 计算）。
            _controlX = _startX + (_targetX - _startX) / 2.0;
            _controlY = _startY + (_targetY - _startY) / 2.0 - _curveHeight;

            if (_projectile.RotationEnabled)
            {
                // 初始旋转 = 切线角度 + 90（对应 JS quadraticTangentDegrees(..., 0) + 90）。
                double angle = ProjectileMath.QuadraticTangentDegrees(
                    _startX, _startY, _controlX, _controlY, _targetX, _targetY, 0.0);
                _projectile.SetRotation((float)(angle + 90.0));
            }
        }

        // ====================================================================
        // Update —— 推进一帧（对应 TargetEnemyBezierMovement.js:103-132 update）
        // ====================================================================

        /// <summary>
        /// 推进贝塞尔位移一帧。
        /// </summary>
        /// <param name="stepMs">子步时长（毫秒）。</param>
        /// <param name="speedScale">投射物速度缩放（对应 projectileSpeedScale）。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>update(deltaMs, speed)</c>（TargetEnemyBezierMovement.js:103-132）：</para>
        /// <list type="number">
        /// <item>progressDelta = deltaMs * movementRate * speed / 500</item>
        /// <item>目标仍存活时刷新目标位置</item>
        /// <item>distanceScaling：progressDelta *= sqrt(max(0.1, currentDist/originalDist))</item>
        /// <item>progress += progressDelta</item>
        /// <item>若到目标距离平方 &lt; hitRadiusSquared 或 progress &gt;= 1 → requestRemove</item>
        /// <item>否则 quadraticBezier 计算新位置，更新旋转</item>
        /// <item>hitEnabled = progress &gt;= 0.8</item>
        /// </list></para>
        /// </remarks>
        internal void Update(long stepMs, float speedScale)
        {
            if (_projectile == null)
            {
                return;
            }

            // 进度增量（对应 JS deltaMs * movementRate * speed / 500）。
            double progressDelta = stepMs * _movementRate * speedScale / ProgressDenominator;

            // 目标仍存活时刷新目标位置（对应 JS if (!targetMissing) _refreshTargetPosition）。
            if (!_targetMissing)
            {
                RefreshTargetPosition();
            }

            // 距离缩放（对应 JS distanceScaling 分支）。
            if (_distanceScaling)
            {
                double originalDistance = ProjectileMath.Distance(_startX, _startY, _targetX, _targetY);
                double currentDistance = ProjectileMath.Distance(_projectile.X, _projectile.Y, _targetX, _targetY);
                if (originalDistance > 0.0)
                {
                    double ratio = currentDistance / originalDistance;
                    if (ratio < MinDistanceRatio)
                    {
                        ratio = MinDistanceRatio;
                    }
                    progressDelta *= System.Math.Sqrt(ratio);
                }
            }

            _progress += progressDelta;

            // 判断是否到达目标（对应 JS distanceSquared(targetPosition, renderNode) < hitRadiusSquared）。
            double distSq = ProjectileMath.DistanceSquared(_targetX, _targetY, _projectile.X, _projectile.Y);
            bool reached = distSq < _hitRadiusSquared;

            if (!reached && _progress < 1.0)
            {
                // 沿贝塞尔曲线移动（对应 JS quadraticBezier(start, control, target, renderNode, progress)）。
                bool done = ProjectileMath.QuadraticBezier(
                    _startX, _startY, _controlX, _controlY, _targetX, _targetY,
                    _progress, out double outX, out double outY);
                _projectile.SetPosition((float)outX, (float)outY);

                if (_projectile.RotationEnabled)
                {
                    // 旋转 = 显示角度（对应 JS displayAngle(lastPosition, renderNode)）。
                    double nextAngle = ProjectileMath.DisplayAngle(_lastX, _lastY, outX, outY);
                    if (_smoothRotation)
                    {
                        // 平滑旋转（对应 JS smoothRotation 分支）。
                        float currentRotation = _projectile.Rotation;
                        double difference = currentRotation - nextAngle;
                        bool largeDifference = difference > 10.0;
                        double amount = largeDifference ? stepMs / (1.5 * difference) : 1.0;
                        _projectile.SetRotation((float)(currentRotation + (nextAngle - currentRotation) * amount));
                    }
                    else
                    {
                        _projectile.SetRotation((float)nextAngle);
                    }
                }

                _lastX = outX;
                _lastY = outY;
            }
            else
            {
                // 已到达目标或进度已满：请求移除（对应 JS projectile.requestRemove()）。
                _projectile.RequestRemove(false);
            }

            // 命中启用（对应 JS hitEnabled = progress >= 0.8）。
            _projectile.HitEnabled = _progress >= HitEnabledProgressThreshold;
        }

        // ====================================================================
        // Recover —— 回收（对应 TargetEnemyBezierMovement.js:144-149 recover）
        // ====================================================================

        /// <summary>
        /// 回收移动策略到待重置状态。清除全部引用与进度。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>recover()</c>（TargetEnemyBezierMovement.js:144-149）：
        /// reset() → enemyManager=null → gameData=null → 入池。</para>
        /// <para>C# 移植由 ProjectileFactory.ResetState 统一调用，不自行入池。</para>
        /// </remarks>
        internal void Recover()
        {
            ResetParameters(50f, true, false, true);
            _enemyManager = null;
            _cellSize = 0f;
        }

        // ====================================================================
        // RefreshTargetPosition —— 刷新目标位置（对应 TargetEnemyBezierMovement.js:151-162）
        // ====================================================================

        /// <summary>
        /// 从 EnemyManager 查询目标并刷新目标位置。目标丢失则保留最后终点。
        /// </summary>
        /// <returns>true=目标存活；false=目标丢失（targetMissing=true）。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>_refreshTargetPosition()</c>（TargetEnemyBezierMovement.js:151-162）：
        /// 从 enemyManager.enemies.get(targetId) 查找敌人，目标不存在则 targetMissing=true 并返回 false。
        /// 目标存在则 targetPosition = enemy.visual.x + gridWidth/2, enemy.visual.y + gridHeight/2。</para>
        /// <para>C# 移植使用 <see cref="EnemyManager.GetById"/> 查询，
        /// 目标位置 = enemy.X + cellSize/2, enemy.Y + cellSize/2（逻辑位置替代 visual）。</para>
        /// </remarks>
        private bool RefreshTargetPosition()
        {
            if (_enemyManager == null || _targetId <= 0)
            {
                _targetMissing = true;
                return false;
            }

            IEnemyEntity enemy = _enemyManager.GetById(_targetId);
            if (enemy == null)
            {
                // 飞行中丢失目标后保留最后终点（对应 JS CONFIRMED 注释）。
                _targetMissing = true;
                return false;
            }

            _targetMissing = false;
            _targetX = enemy.X + _cellSize / 2.0;
            _targetY = enemy.Y + _cellSize / 2.0;
            return true;
        }
    }
}
