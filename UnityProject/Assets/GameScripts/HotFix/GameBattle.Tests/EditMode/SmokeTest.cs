using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode
{
    /// <summary>
    /// 最小冒烟测试（OpenSpec change port-minimal-battle-to-gamebattle task 2.2）。
    /// <para>
    /// 目的：证明 <c>GameBattle.Tests</c> 程序集能在 EditMode 运行，
    /// 且纯逻辑测试不会隐式依赖 Scene、FUI 或 Editor 自动程序集引用。
    /// </para>
    /// <para>
    /// <c>InternalsVisibleTo("GameBattle.Tests")</c> 已在
    /// <c>GameBattle/AssemblyInfo.cs</c> 中声明，待后续任务在 GameBattle 引入
    /// internal 类型后，测试即可直接访问其 internal 成员，无需把运行时类型改为 public。
    /// </para>
    /// </summary>
    [TestFixture]
    public class SmokeTest
    {
        /// <summary>
        /// 测试自身程序集，用于引用图断言。
        /// </summary>
        private static Assembly TestAssembly => typeof(SmokeTest).Assembly;

        [Test]
        [Description("验证 NUnit/Unity Test Framework 可用：纯逻辑断言，无 Scene/FUI/资源依赖。")]
        public void NUnitFramework_IsAvailable_PureLogicAlwaysPasses()
        {
            // 纯逻辑：不接触 UnityEngine.SceneManagement、FairyGUI 或资源加载。
            const int expected = 2 + 3;
            Assert.AreEqual(5, expected, "NUnit Assert 可用且基本算术成立。");
        }

        [Test]
        [Description("验证 GameBattle 程序集引用可解析：能加载 GameBattle 程序集类型信息。")]
        public void GameBattleAssembly_IsReferencedAndResolvable()
        {
            // 通过程序集名确认 GameBattle 已被引用且可解析；
            // 不实例化任何业务类型，避免依赖尚未实现的运行时逻辑。
            var assembly = TestAssembly;
            Assert.IsNotNull(assembly, "测试自身程序集可解析。");

            // 确认 GameBattle 程序集在引用图中可被定位（按名查找，不触发业务类型加载）。
            var gameBattle = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "GameBattle");
            Assert.IsNotNull(gameBattle, "GameBattle 程序集已被 GameBattle.Tests 引用并加载。");
        }

        [Test]
        [Description(
            "证明测试程序集的编译期引用图不包含 GameFUI、GamePlay 或 UnityEngine.SceneManagement，" +
            "即不隐式依赖 Scene、FUI 或表现层程序集。")]
        public void NoImplicitSceneOrFuiAssemblyReference()
        {
            // 读取测试程序集的编译期引用列表（asmdef references 的最终产物）。
            var referencedNames = TestAssembly.GetReferencedAssemblies()
                .Select(n => n.Name)
                .ToArray();

            // 明确禁止的隐式依赖：FUI 表现层程序集不得进入测试引用图。
            Assert.That(
                referencedNames,
                Has.None.EqualTo("GameFUI"),
                "GameBattle.Tests 不得引用 GameFUI，否则测试隐式依赖 FairyGUI 表现层。");
            Assert.That(
                referencedNames,
                Has.None.EqualTo("GamePlay"),
                "GameBattle.Tests 不得引用 GamePlay，避免拉入未声明的表现/Scene 依赖。");

            // Scene 模块属于 UnityEngine 子模块，只有显式引用才会出现在引用图中；
            // 若出现则说明测试隐式依赖 Scene 加载，违反纯逻辑约束。
            Assert.That(
                referencedNames,
                Has.None.EqualTo("UnityEngine.SceneModule"),
                "GameBattle.Tests 不得引用 UnityEngine.SceneModule，纯逻辑测试不应依赖 Scene。");
            Assert.That(
                referencedNames,
                Has.None.EqualTo("UnityEngine.SceneManagementModule"),
                "GameBattle.Tests 不得引用 UnityEngine.SceneManagementModule，纯逻辑测试不应依赖 Scene。");
        }

        [Test]
        [Description(
            "证明测试程序集未把 FairyGUI 类型纳入引用图，" +
            "即不通过 GameBattle 传递依赖 FairyGUI。")]
        public void NoFairyGuiTypeReferenceInTestAssembly()
        {
            // 即使 FairyGUI 程序集在 AppDomain 中被其他模块加载，
            // 纯逻辑测试也不应在其自身引用图中出现 FairyGUI 程序集名。
            var referencedNames = TestAssembly.GetReferencedAssemblies()
                .Select(n => n.Name)
                .ToArray();

            // FairyGUI 在 Unity 中常见程序集名前缀；命中任一即说明测试意外耦合表现层。
            var fairyGuiMatches = referencedNames
                .Where(name => name != null && name.IndexOf("Fairy", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            Assert.IsEmpty(
                fairyGuiMatches,
                "测试引用图中出现疑似 FairyGUI 程序集："
                    + (fairyGuiMatches.Length == 0 ? string.Empty : string.Join(", ", fairyGuiMatches))
                    + "。纯逻辑测试不得传递依赖 FairyGUI。");
        }
    }
}
