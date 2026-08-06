using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Projectile
{
    /// <summary>
    /// ProjectileMath 纯数学函数单元测试（task 5.6）。
    /// </summary>
    /// <remarks>
    /// <para>验证 spec battle-parity-verification "Differences use explicit comparison rules"：
    /// 离散值精确比较；浮点字段使用字段级显式声明容差（非默认 epsilon）。</para>
    /// <para>对照源：Origin/reconstructed-project/src/projectiles/ProjectileMath.js（五个导出函数）。</para>
    /// <para>JS 样本来源：
    /// <list type="bullet">
    /// <item>ProjectileMovement.test.js:65-74 —— TargetPositionBezierMovement t=0.5 → (50, 25)。</item>
    /// <item>SimpleDynamicArrowTrajectory.test.js:9-25 —— 直接调用 quadraticBezier 并断言非线性。</item>
    /// <item>SimpleDynamicArrowTrajectory.test.js:51-67 —— 距离缩放飞行时长 near=640ms / far=720ms。</item>
    /// </list>
    /// </para>
    /// <para>容差分级：
    /// <list type="bullet">
    /// <item>Distance / Bezier 位置：<c>1e-10</c>（C#/JS double 同为 IEEE 754，相同运算顺序应逐位等价；
    ///     给极小容差吸收跨平台 Math.Sqrt/Atan2 实现差异）。</item>
    /// <item>角度：<c>1e-9</c> 度（Atan2 结果经弧度→度换算，放大数值误差感知）。</item>
    /// </list>
    /// </para>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class ProjectileMathTests
    {
        // ====================================================================
        // 字段级显式容差（spec "Differences use explicit comparison rules"）
        // ====================================================================

        /// <summary>
        /// 距离/位置字段容差。C# 与 JS 同为 IEEE 754 double 且运算顺序一致，
        /// 理论上逐位等价；给极小容差吸收跨平台 Sqrt 实现差异。
        /// </summary>
        private const double PositionTolerance = 1e-10;

        /// <summary>
        /// 角度字段容差（度）。Atan2 经弧度→度换算放大误差感知。
        /// </summary>
        private const double AngleTolerance = 1e-9;

        // ====================================================================
        // 断言辅助
        // ====================================================================

        /// <summary>
        /// 断言两个 double 在字段级显式容差内相等（非默认 epsilon）。
        /// </summary>
        private static void AssertNear(double actual, double expected, double tolerance, string message)
        {
            double diff = Math.Abs(actual - expected);
            Assert.IsTrue(
                diff <= tolerance,
                $"{message}: actual={actual:R}, expected={expected:R}, diff={diff:R}, tolerance={tolerance:R}");
        }

        // ====================================================================
        // Distance —— 对应 JS distance(a, b)
        // ====================================================================

        [Test]
        [Description("Distance 对正交两点返回精确欧氏距离（3-4-5 直角三角形）。")]
        public void Distance_RightTriangle_ReturnsHypotenuse()
        {
            // JS: Math.sqrt(3*3 + 4*4) = 5
            AssertNear(ProjectileMath.Distance(0, 0, 3, 4), 5.0, PositionTolerance, "3-4-5 距离");
        }

        [Test]
        [Description("Distance 对同点返回 0。")]
        public void Distance_SamePoint_ReturnsZero()
        {
            AssertNear(ProjectileMath.Distance(7, 9, 7, 9), 0.0, PositionTolerance, "同点距离");
        }

        [Test]
        [Description("Distance 对称性：dist(a,b) == dist(b,a)，与 JS 减法对称一致。")]
        public void Distance_Symmetric()
        {
            double ab = ProjectileMath.Distance(10, 20, 30, 40);
            double ba = ProjectileMath.Distance(30, 40, 10, 20);
            AssertNear(ab, ba, PositionTolerance, "距离对称");
        }

        [Test]
        [Description("Distance 对负坐标正确（对应 JS 无符号约束的 Number 运算）。")]
        public void Distance_NegativeCoordinates()
        {
            AssertNear(ProjectileMath.Distance(-3, -4, 0, 0), 5.0, PositionTolerance, "负坐标距离");
        }

        // ====================================================================
        // DistanceSquared —— 对应 JS distanceSquared(a, b)
        // ====================================================================

        [Test]
        [Description("DistanceSquared 对 3-4-5 直角三角形返回 25，与 JS distanceSquared 一致。")]
        public void DistanceSquared_RightTriangle_Returns25()
        {
            AssertNear(ProjectileMath.DistanceSquared(0, 0, 3, 4), 25.0, PositionTolerance, "距离平方");
        }

        [Test]
        [Description("DistanceSquared == Distance^2，验证两者一致（与 JS 同公式）。")]
        public void DistanceSquared_EqualsDistanceSquared()
        {
            double sq = ProjectileMath.DistanceSquared(1, 2, 6, 14);
            double d = ProjectileMath.Distance(1, 2, 6, 14);
            AssertNear(sq, d * d, PositionTolerance, "平方与开方一致");
        }

        // ====================================================================
        // DisplayAngle —— 对应 JS displayAngle(from, to) / np.angle
        // 0° 朝上，90° 朝右
        // ====================================================================

        [Test]
        [Description("DisplayAngle 朝上（from 在 to 正下方，Y 轴向下为正）返回 0°。")]
        public void DisplayAngle_PointingUp_Returns0()
        {
            // JS: dx=0, invertedDy=fromY-toY=10-0=10 >= 0 → return 0
            AssertNear(ProjectileMath.DisplayAngle(0, 10, 0, 0), 0.0, AngleTolerance, "朝上 0°");
        }

        [Test]
        [Description("DisplayAngle 朝下返回 180°。")]
        public void DisplayAngle_PointingDown_Returns180()
        {
            // JS: dx=0, invertedDy=0-10=-10 < 0 → return 180
            AssertNear(ProjectileMath.DisplayAngle(0, 0, 0, 10), 180.0, AngleTolerance, "朝下 180°");
        }

        [Test]
        [Description("DisplayAngle 朝右返回 90°。")]
        public void DisplayAngle_PointingRight_Returns90()
        {
            // JS: dx=10>0, invertedDy=0 → return 90
            AssertNear(ProjectileMath.DisplayAngle(0, 0, 10, 0), 90.0, AngleTolerance, "朝右 90°");
        }

        [Test]
        [Description("DisplayAngle 朝左返回 270°。")]
        public void DisplayAngle_PointingLeft_Returns270()
        {
            // JS: dx=-10<0, invertedDy=0 → return 270
            AssertNear(ProjectileMath.DisplayAngle(0, 0, -10, 0), 270.0, AngleTolerance, "朝左 270°");
        }

        [Test]
        [Description("DisplayAngle 对角线（右上 45°）使用 atan2(dx, invertedDy) 返回 45°。")]
        public void DisplayAngle_DiagonalUpRight_Returns45()
        {
            // JS: dx=10, invertedDy=10 → atan2(10, 10) * 180/PI = 45
            AssertNear(ProjectileMath.DisplayAngle(0, 10, 10, 0), 45.0, AngleTolerance, "右上 45°");
        }

        [Test]
        [Description("DisplayAngle 对角线（右下）返回 135°，验证 atan2 第一参数为 dx 的非常规顺序。")]
        public void DisplayAngle_DiagonalDownRight_Returns135()
        {
            // JS: dx=10, invertedDy=0-10=-10 → atan2(10, -10) * 180/PI = 135
            AssertNear(ProjectileMath.DisplayAngle(0, 0, 10, 10), 135.0, AngleTolerance, "右下 135°");
        }

        // ====================================================================
        // QuadraticTangentDegrees —— 对应 JS quadraticTangentDegrees / np.Rs
        // 标准数学切线角 atan2(dy, dx)
        // ====================================================================

        [Test]
        [Description("QuadraticTangentDegrees 在 t=0 对水平右行曲线返回 0°（dx>0, dy=0）。")]
        public void QuadraticTangentDegrees_AtStart_HorizontalRight_Returns0()
        {
            // start=(0,0), control=(50,0), end=(100,0): dx=2*1*50=100, dy=0 → atan2(0,100)=0
            AssertNear(
                ProjectileMath.QuadraticTangentDegrees(0, 0, 50, 0, 100, 0, 0.0),
                0.0,
                AngleTolerance,
                "起点水平切线 0°");
        }

        [Test]
        [Description("对称抛物线（p0=(0,0), c=(50,50), e=(100,0)）在 t=0 切线角为 45°。")]
        public void QuadraticTangentDegrees_SymmetricParabola_AtStart_Returns45()
        {
            // t=0: dx=2*(50-0)=100, dy=2*(50-0)=100 → atan2(100,100)=45°
            AssertNear(
                ProjectileMath.QuadraticTangentDegrees(0, 0, 50, 50, 100, 0, 0.0),
                45.0,
                AngleTolerance,
                "对称抛物线起点 45°");
        }

        [Test]
        [Description("对称抛物线在 t=0.5 切线角为 0°（顶点水平）。")]
        public void QuadraticTangentDegrees_SymmetricParabola_AtMidpoint_Returns0()
        {
            // t=0.5: dx=2*0.5*(50-0)+2*0.5*(100-50)=50+50=100
            //        dy=2*0.5*(50-0)+2*0.5*(0-50)=50-50=0 → atan2(0,100)=0
            AssertNear(
                ProjectileMath.QuadraticTangentDegrees(0, 0, 50, 50, 100, 0, 0.5),
                0.0,
                AngleTolerance,
                "对称抛物线中点 0°");
        }

        [Test]
        [Description("对称抛物线在 t=1 切线角为 -45°（下降段）。")]
        public void QuadraticTangentDegrees_SymmetricParabola_AtEnd_ReturnsMinus45()
        {
            // t=1: dx=2*0*(50-0)+2*1*(100-50)=100, dy=2*0*(50-0)+2*1*(0-50)=-100 → atan2(-100,100)=-45°
            AssertNear(
                ProjectileMath.QuadraticTangentDegrees(0, 0, 50, 50, 100, 0, 1.0),
                -45.0,
                AngleTolerance,
                "对称抛物线终点 -45°");
        }

        // ====================================================================
        // QuadraticBezier —— 对应 JS quadraticBezier / np.Us（de Casteljau）
        // ====================================================================

        [Test]
        [Description("QuadraticBezier 在 t=0 返回起点，且返回 false（progress < 1）。")]
        public void QuadraticBezier_AtStart_ReturnsStartPointAndFalse()
        {
            bool done = ProjectileMath.QuadraticBezier(0, 0, 50, 50, 100, 0, 0.0, out double x, out double y);
            Assert.IsFalse(done, "t=0 应返回 false（progress < 1）。");
            AssertNear(x, 0.0, PositionTolerance, "t=0 x=起点");
            AssertNear(y, 0.0, PositionTolerance, "t=0 y=起点");
        }

        [Test]
        [Description("QuadraticBezier 在 t=1 返回终点，且返回 true（!(progress < 1)）。")]
        public void QuadraticBezier_AtEnd_ReturnsEndPointAndTrue()
        {
            bool done = ProjectileMath.QuadraticBezier(0, 0, 50, 50, 100, 0, 1.0, out double x, out double y);
            Assert.IsTrue(done, "t=1 应返回 true（!(progress < 1)）。");
            AssertNear(x, 100.0, PositionTolerance, "t=1 x=终点");
            AssertNear(y, 0.0, PositionTolerance, "t=1 y=终点");
        }

        [Test]
        [Description(
            "QuadraticBezier 在 t=0.5 返回中点 (50, 25)，与 JS 样本 " +
            "ProjectileMovement.test.js:65-74 TargetPositionBezierMovement 验证值一致。")]
        public void QuadraticBezier_AtHalf_ReturnsMidpoint_MatchesJsSample()
        {
            // JS 样本（ProjectileMovement.test.js:65-74）：
            //   p0=(0,0), p1=(50,50), p2=(100,0), t=0.5
            //   de Casteljau: first=(25,25), second=(75,25), out=(25+(75-25)*0.5, 25+(25-25)*0.5)=(50, 25)
            //   注释明确：t=0.5: 0.25*0 + 0.5*50 + 0.25*100 = 50; y: 0.25*0 + 0.5*50 + 0.25*0 = 25
            bool done = ProjectileMath.QuadraticBezier(0, 0, 50, 50, 100, 0, 0.5, out double x, out double y);
            AssertNear(x, 50.0, PositionTolerance, "t=0.5 x=50（JS 样本）");
            AssertNear(y, 25.0, PositionTolerance, "t=0.5 y=25（JS 样本）");
            Assert.IsFalse(done, "t=0.5 应返回 false。");
        }

        [Test]
        [Description(
            "QuadraticBezier 在 progress=0.16（对应 JS 样本 80/500）返回非线性的贝塞尔位置，" +
            "与 JS 样本 SimpleDynamicArrowTrajectory.test.js:9-25 断言 y != linearY 一致；" +
            "并验证 C# de Casteljau 手工复核值。")]
        public void QuadraticBezier_AtProgress0p16_ReturnsNonLinear_MatchesJsSample()
        {
            // JS 样本（SimpleDynamicArrowTrajectory.test.js:9-25）的不变量：
            //   start={x:40, y:520}, progress = 80/500 = 0.16,
            //   control = { 中点 x, 中点 y - curveHeight(120) },
            //   断言 quadraticBezier 输出 y != start.y + (target.y - start.y) * 0.16（非线性）。
            // JS 样本的 target 来自测试环境（mob.visual + 40），不在此复现环境依赖；
            // 改用自洽点集验证同一不变量：start=(40,520), end=(240,560), curveHeight=120。
            double startX = 40, startY = 520;
            double targetX = 240, targetY = 560;
            double curveHeight = 120;
            double controlX = (startX + targetX) / 2.0; // 140
            double controlY = (startY + targetY) / 2.0 - curveHeight; // 540 - 120 = 420
            double progress = 80.0 / 500.0; // 0.16

            bool done = ProjectileMath.QuadraticBezier(
                startX, startY, controlX, controlY, targetX, targetY, progress,
                out double x, out double y);

            // 线性插值 y 对照（与 JS linearY 公式一致）。
            double linearY = startY + (targetY - startY) * progress; // 520 + 40*0.16 = 526.4

            Assert.IsFalse(done, "progress=0.16 应返回 false。");
            Assert.AreNotEqual(
                y,
                linearY,
                $"贝塞尔 y={y:R} 应不等于线性 y={linearY:R}（JS 样本断言非线性）。");

            // de Casteljau 手工复核：
            //   firstX  = 40 + (140-40)*0.16 = 40 + 16 = 56
            //   secondX = 140 + (240-140)*0.16 = 140 + 16 = 156
            //   outX    = 56 + (156-56)*0.16 = 56 + 16 = 72
            //   firstY  = 520 + (420-520)*0.16 = 520 - 16 = 504
            //   secondY = 420 + (560-420)*0.16 = 420 + 22.4 = 442.4
            //   outY    = 504 + (442.4 - 504)*0.16 = 504 + (-9.856) = 494.144
            AssertNear(x, 72.0, PositionTolerance, "t=0.16 x=72（手工 de Casteljau）");
            AssertNear(y, 494.144, PositionTolerance, "t=0.16 y=494.144（手工 de Casteljau）");
        }

        [Test]
        [Description("QuadraticBezier 超过 t=1（如 1.2）仍外推计算且返回 true，与 JS !(progress<1) 一致。")]
        public void QuadraticBezier_BeyondOne_ReturnsTrueAndExtrapolates()
        {
            // JS: !(1.2 < 1) === true，位置仍按 de Casteljau 外推。
            bool done = ProjectileMath.QuadraticBezier(0, 0, 50, 50, 100, 0, 1.2, out double x, out double y);
            Assert.IsTrue(done, "progress=1.2 应返回 true。");
            // 外推：first=(60,60), second=(110,-10), out=60+(110-60)*1.2=60+60=120, y=60+(-10-60)*1.2=60-84=-24
            AssertNear(x, 120.0, PositionTolerance, "t=1.2 外推 x=120");
            AssertNear(y, -24.0, PositionTolerance, "t=1.2 外推 y=-24");
        }

        // ====================================================================
        // 跨函数一致性验证
        // ====================================================================

        [Test]
        [Description("Distance 与 DistanceSquared 对随机点一致：sqrt(sq) == dist。")]
        public void Distance_AndDistanceSquared_ConsistentAcrossPoints()
        {
            // 多组点验证两者一致（与 JS 同公式族）。
            double[][] cases =
            {
                new[] { 0.0, 0.0, 3.0, 4.0 },
                new[] { -5.0, 2.0, 10.0, -7.0 },
                new[] { 100.0, 200.0, 100.0, 200.0 },
                new[] { 1.5, 2.5, 3.5, 4.5 },
            };

            foreach (double[] c in cases)
            {
                double d = ProjectileMath.Distance(c[0], c[1], c[2], c[3]);
                double sq = ProjectileMath.DistanceSquared(c[0], c[1], c[2], c[3]);
                AssertNear(d * d, sq, PositionTolerance, $"平方一致 [{c[0]},{c[1]}]-[{c[2]},{c[3]}]");
            }
        }

        [Test]
        [Description("QuadraticBezier de Casteljau 与 Bernstein 形式数学等价（同一点集 t=0..1 采样）。")]
        public void QuadraticBezier_DeCasteljau_EqualsBernsteinForm()
        {
            // de Casteljau（本实现）与 Bernstein (1-t)²S + 2(1-t)tC + t²E 数学等价，
            // 但运算顺序不同会产生微小浮点差异；在字段级容差内验证等价。
            double sx = 10, sy = 20, cx = 50, cy = 80, ex = 90, ey = 30;
            for (int i = 0; i <= 10; i++)
            {
                double t = i / 10.0;
                double u = 1.0 - t;
                double expectedX = u * u * sx + 2.0 * u * t * cx + t * t * ex;
                double expectedY = u * u * sy + 2.0 * u * t * cy + t * t * ey;

                ProjectileMath.QuadraticBezier(sx, sy, cx, cy, ex, ey, t, out double actualX, out double actualY);
                AssertNear(actualX, expectedX, PositionTolerance, $"Bernstein 等价 x t={t}");
                AssertNear(actualY, expectedY, PositionTolerance, $"Bernstein 等价 y t={t}");
            }
        }
    }
}
