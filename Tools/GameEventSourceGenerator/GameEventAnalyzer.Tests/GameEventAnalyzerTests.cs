using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EventAnalyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace GameEventAnalyzer.Tests
{
    /// <summary>
    /// 事件分析器单测：EVENT003（接口形状）/ EVENT004（手写注册标记）/ EVENT001·EVENT002（跨程序集参数检查）。
    /// </summary>
    public sealed class GameEventAnalyzerTests
    {
        private const string StubSource = @"
namespace TEngine
{
    public class EventInterfaceAttribute : System.Attribute
    {
        public EventInterfaceAttribute(int group) { }
    }
    public class EventDispatcher { }
    public static class RuntimeId
    {
        public static int ToRuntimeId(string value) => 0;
    }
    public static class GameEvent
    {
        public static EventMgr EventMgr => null;
    }
    public class EventMgr
    {
        public EventDispatcher GetDispatcher() => null;
    }
    public class EventAssemblyRegistrarAttribute : System.Attribute
    {
        public EventAssemblyRegistrarAttribute(System.Type registrarType) { }
    }
}
namespace UnityEngine.Scripting
{
    public class PreserveAttribute : System.Attribute { }
}
";

        private static IEnumerable<MetadataReference> BaseReferences()
        {
            return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
            };
        }

        private static CSharpCompilation CreateCompilation(string source, string assemblyName = "TestAssembly")
        {
            var trees = new[]
            {
                CSharpSyntaxTree.ParseText(StubSource, new CSharpParseOptions(LanguageVersion.CSharp9)),
                CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9)),
            };
            return CSharpCompilation.Create(assemblyName, trees, BaseReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(CSharpCompilation compilation)
        {
            var analyzer = new EventAnalyzer.GameEventAnalyzer();
            return await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
                .GetAnalyzerDiagnosticsAsync();
        }

        [Theory]
        [InlineData("int NotVoid();")]
        [InlineData("void SixParams(int a, int b, int c, int d, int e, int f);")]
        [InlineData("void Overload(); void Overload(int a);")]
        [InlineData("void RefParam(ref int a);")]
        [InlineData("void OutParam(out int a);")]
        [InlineData("void DefaultParam(int a = 1);")]
        [InlineData("void ParamsParam(params int[] a);")]
        [InlineData("void GenericMethod<T>();")]
        public async Task ContractViolations_ReportEvent003(string member)
        {
            var source = $"using TEngine;\nnamespace GameLogic\n{{\n    [EventInterface(0)]\n    public interface IBad {{ {member} }}\n}}\n";
            var diagnostics = await RunAnalyzerAsync(CreateCompilation(source));
            Assert.Contains(diagnostics, d => d.Id == "EVENT003");
        }

        [Fact]
        public async Task InternalInterface_ReportEvent003()
        {
            var source = "using TEngine;\nnamespace GameLogic\n{\n    [EventInterface(0)]\n    internal interface IInternal { void A(); }\n}\n";
            var diagnostics = await RunAnalyzerAsync(CreateCompilation(source));
            Assert.Contains(diagnostics, d => d.Id == "EVENT003");
        }

        [Fact]
        public async Task NestedInterface_ReportEvent003()
        {
            var source = "using TEngine;\nnamespace GameLogic\n{\n    public class Outer\n    {\n        [EventInterface(0)]\n        public interface INested { void A(); }\n    }\n}\n";
            var diagnostics = await RunAnalyzerAsync(CreateCompilation(source));
            Assert.Contains(diagnostics, d => d.Id == "EVENT003");
        }

        [Fact]
        public async Task InheritedInterface_ReportEvent003()
        {
            var source = "using TEngine;\nnamespace GameLogic\n{\n    public interface IBase { void BaseMethod(); }\n    [EventInterface(0)]\n    public interface IDerived : IBase { void A(); }\n}\n";
            var diagnostics = await RunAnalyzerAsync(CreateCompilation(source));
            Assert.Contains(diagnostics, d => d.Id == "EVENT003");
        }

        [Fact]
        public async Task ManualAssemblyRegistrarAttribute_ReportEvent004()
        {
            var source = @"
using TEngine;
[assembly: TEngine.EventAssemblyRegistrarAttribute(typeof(System.Object))]
namespace GameLogic
{
    public static class Dummy { }
}
";
            var diagnostics = await RunAnalyzerAsync(CreateCompilation(source));
            Assert.Contains(diagnostics, d => d.Id == "EVENT004");
        }

        [Fact]
        public async Task CrossAssemblyEventId_TypeMismatch_ReportEvent001()
        {
            // 引用程序集：事件接口 + 模拟生成物 _Event 类。
            var refSource = @"
using TEngine;
namespace OtherNs
{
    [EventInterface(0)]
    public interface ITest { void Fire(int hp); }
    public static class ITest_Event
    {
        public static readonly int Fire = TEngine.RuntimeId.ToRuntimeId(""x"");
    }
}
";
            var refCompilation = CreateCompilation(refSource, "RefAssembly");
            using var stream = new MemoryStream();
            var emitResult = refCompilation.Emit(stream);
            Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics));
            stream.Position = 0;
            var refReference = MetadataReference.CreateFromImage(stream.ToArray());

            // 消费程序集：通过外部 _Event 类监听，泛型参数传错类型（lambda 保证调用可正常绑定）。
            var consumerSource = @"
using OtherNs;
namespace Consumer
{
    internal static class Impl
    {
        private static void AddUIEvent<T>(int eventId, System.Action<T> handler) { }

        public static void Register()
        {
            AddUIEvent<string>(ITest_Event.Fire, (string s) => { });
        }
    }
}
";
            var consumerTrees = new[]
            {
                CSharpSyntaxTree.ParseText(consumerSource, new CSharpParseOptions(LanguageVersion.CSharp9)),
            };
            var consumerCompilation = CSharpCompilation.Create("ConsumerAssembly", consumerTrees,
                BaseReferences().Concat(new[] { refReference }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var diagnostics = await RunAnalyzerAsync(consumerCompilation);
            Assert.Contains(diagnostics, d => d.Id == "EVENT001");
        }

        [Fact]
        public async Task CrossAssemblyEventId_ParamCount_ReportEvent002()
        {
            var refSource = @"
using TEngine;
namespace OtherNs
{
    [EventInterface(0)]
    public interface ITest { void Fire(int hp); }
    public static class ITest_Event
    {
        public static readonly int Fire = TEngine.RuntimeId.ToRuntimeId(""x"");
    }
}
";
            var refCompilation = CreateCompilation(refSource, "RefAssembly");
            using var stream = new MemoryStream();
            var emitResult = refCompilation.Emit(stream);
            Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics));
            stream.Position = 0;
            var refReference = MetadataReference.CreateFromImage(stream.ToArray());

            var consumerSource = @"
using OtherNs;
namespace Consumer
{
    internal static class Impl
    {
        private static void AddUIEvent<T1, T2>(int eventId, System.Action<T1, T2> handler) { }

        public static void Register()
        {
            AddUIEvent<int, int>(ITest_Event.Fire, (int a, int b) => { });
        }
    }
}
";
            var consumerTrees = new[]
            {
                CSharpSyntaxTree.ParseText(consumerSource, new CSharpParseOptions(LanguageVersion.CSharp9)),
            };
            var consumerCompilation = CSharpCompilation.Create("ConsumerAssembly", consumerTrees,
                BaseReferences().Concat(new[] { refReference }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var diagnostics = await RunAnalyzerAsync(consumerCompilation);
            Assert.Contains(diagnostics, d => d.Id == "EVENT002");
        }

        [Fact]
        public async Task SameInterfaceName_TwoAssemblies_ResolvesFromEventTypeAssembly()
        {
            // 两个引用程序集各有 OtherNs.ITest，但事件类来自 A 程序集：应定位到 A 的 ITest（参数 int）。
            var refASource = @"
using TEngine;
namespace OtherNs
{
    [EventInterface(0)]
    public interface ITest { void Fire(int hp); }
    public static class ITest_Event
    {
        public static readonly int Fire = TEngine.RuntimeId.ToRuntimeId(""x"");
    }
}
";
            var refBSource = @"
using TEngine;
namespace OtherNs
{
    [EventInterface(0)]
    public interface ITest { void Fire(string name); }
}
";

            var compA = CreateCompilation(refASource, "AssemblyA");
            using var streamA = new MemoryStream();
            Assert.True(compA.Emit(streamA).Success);
            streamA.Position = 0;

            var compB = CreateCompilation(refBSource, "AssemblyB");
            using var streamB = new MemoryStream();
            Assert.True(compB.Emit(streamB).Success);
            streamB.Position = 0;

            var consumerSource = @"
namespace Consumer
{
    internal static class Impl
    {
        private static void AddUIEvent<T>(int eventId, System.Action<T> handler) { }
        private static void OnFire(int hp) { }

        public static void Register()
        {
            AddUIEvent<int>(global::OtherNs.ITest_Event.Fire, OnFire);
        }
    }
}
";
            var consumerCompilation = CSharpCompilation.Create("ConsumerAssembly",
                new[] { CSharpSyntaxTree.ParseText(consumerSource, new CSharpParseOptions(LanguageVersion.CSharp9)) },
                BaseReferences().Concat(new[]
                {
                    MetadataReference.CreateFromImage(streamA.ToArray()),
                    MetadataReference.CreateFromImage(streamB.ToArray()),
                }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // A 的 Fire(int) 与监听不匹配? 实际 A.Fire(int) + AddUIEvent<int> 匹配 → 无 EVENT001；B 的签名不会被查到。
            var diagnostics = await RunAnalyzerAsync(consumerCompilation);
            Assert.DoesNotContain(diagnostics, d => d.Id == "EVENT001");
        }
    }
}