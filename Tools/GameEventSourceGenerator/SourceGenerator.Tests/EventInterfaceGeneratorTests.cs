using System;
using System.Collections.Generic;
using System.Linq;
using EventContract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SourceGenerator.Tests
{
    /// <summary>
    /// 事件接口 Source Generator 单测（CSharpGeneratorDriver 驱动，生成文本断言）。
    /// 契约违规接口的 EVENT003 诊断由 GameEventAnalyzer.Tests 覆盖，此处只断言生成器零产出。
    /// </summary>
    public sealed class EventInterfaceGeneratorTests
    {
        /// <summary>
        /// TEngine/UnityEngine 桩：供生成器做语义匹配与生成代码引用解析。
        /// </summary>
        private const string StubSource = @"
namespace TEngine
{
    public class EventInterfaceAttribute : System.Attribute
    {
        public EventInterfaceAttribute(int group) { }
    }
    public class EventDispatcher
    {
        public void Send(int eventType) { }
        public void Send<T1>(int eventType, T1 a1) { }
        public void Send<T1, T2>(int eventType, T1 a1, T2 a2) { }
        public void Send<T1, T2, T3>(int eventType, T1 a1, T2 a2, T3 a3) { }
        public void Send<T1, T2, T3, T4>(int eventType, T1 a1, T2 a2, T3 a3, T4 a4) { }
        public void Send<T1, T2, T3, T4, T5>(int eventType, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5) { }
    }
    public static class RuntimeId
    {
        public static int ToRuntimeId(string value) => 0;
    }
    public class EventMgr
    {
        public EventDispatcher GetDispatcher() => null;
        public void RegWrapInterface<T>(T callerWrap) { }
    }
    public static class GameEvent
    {
        public static EventMgr EventMgr => null;
    }
    public class EventAssemblyRegistrarAttribute : System.Attribute
    {
        public EventAssemblyRegistrarAttribute(System.Type registrarType) { }
    }
    public interface IEventAssemblyRegistrar
    {
        void Register(EventDispatcher dispatcher);
    }
}
namespace UnityEngine.Scripting
{
    public class PreserveAttribute : System.Attribute { }
}
";

        private static CSharpCompilation CreateCompilation(string userSource, string assemblyName = "TestAssembly")
        {
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
            };

            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(StubSource, new CSharpParseOptions(LanguageVersion.CSharp9)),
                CSharpSyntaxTree.ParseText(userSource, new CSharpParseOptions(LanguageVersion.CSharp9)),
            };

            return CSharpCompilation.Create(assemblyName, syntaxTrees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static string[] RunGenerator(string userSource, string assemblyName = "TestAssembly")
        {
            var compilation = CreateCompilation(userSource, assemblyName);
            var driver = CSharpGeneratorDriver.Create(new[] { new EventInterfaceGenerator() });
            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
            var result = driver.GetRunResult();
            return result.Results.SelectMany(r => r.GeneratedSources)
                .Select(s => s.HintName).OrderBy(n => n).ToArray();
        }

        private static string RunGeneratorAndGetSource(string userSource, string hintContains)
        {
            var compilation = CreateCompilation(userSource);
            var driver = CSharpGeneratorDriver.Create(new[] { new EventInterfaceGenerator() });
            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
            var result = driver.GetRunResult();
            var matches = result.Results.SelectMany(r => r.GeneratedSources)
                .Where(s => s.HintName.Contains(hintContains)).ToArray();
            Assert.Single(matches);
            return matches[0].SourceText.ToString();
        }

        [Fact]
        public void ValidInterface_GeneratesEventGenAndRegistrar()
        {
            var user = @"
using TEngine;
namespace GameLogic
{
    [EventInterface(0)]
    public interface ILoginUI
    {
        void ShowLoginUI();
        void CloseLoginUI(int id);
    }
}
";
            var hints = RunGenerator(user);
            Assert.Equal(3, hints.Length); // 1 接口: EventIds + Gen + Registrar
            Assert.Contains(hints, h => h.Contains("EventIds_"));
            Assert.Contains(hints, h => h.Contains("Gen_"));
            Assert.Contains(hints, h => h.Contains("EventAssemblyRegistrar_"));

            var gen = RunGeneratorAndGetSource(user, "Gen_");
            Assert.Contains("public partial class ILoginUI_Gen : global::GameLogic.ILoginUI", gen);
            Assert.Contains("RegWrapInterface<global::GameLogic.ILoginUI>(this)", gen);
            Assert.Contains("void ShowLoginUI()", gen);
            Assert.Contains("void CloseLoginUI(int id)", gen);
            Assert.Contains("[global::UnityEngine.Scripting.Preserve]", gen);
        }

        [Fact]
        public void EventKey_UsesLengthPrefix_NoAmbiguityAcrossSegments()
        {
            var key1 = EventInterfaceContract.GetEventKey("A.B", "C.IFoo", "M");
            var key2 = EventInterfaceContract.GetEventKey("A", "B.C.IFoo", "M");
            Assert.NotEqual(key1, key2);
            Assert.Equal(key1, EventInterfaceContract.GetEventKey("A.B", "C.IFoo", "M"));
        }

        [Fact]
        public void SameInterfaceName_DifferentNamespaces_GeneratesUniqueHintNames()
        {
            var user = @"
using TEngine;
namespace Feature.A
{
    [EventInterface(0)]
    public interface IPlayer { void A(); }
}
namespace Feature.B
{
    [EventInterface(0)]
    public interface IPlayer { void B(); }
}
";
            var hints = RunGenerator(user);
            Assert.Equal(5, hints.Length); // 2 接口 * 2 + 1 Registrar
            Assert.Equal(hints.Length, hints.Distinct().Count());
        }

        [Fact]
        public void PartialInterface_MembersAcrossDeclarations_AllGenerated()
        {
            var user = @"
using TEngine;
namespace GameLogic
{
    [EventInterface(0)]
    public partial interface IFoo { void A(); }
    public partial interface IFoo { void B(); }
}
";
            var gen = RunGeneratorAndGetSource(user, "Gen_");
            Assert.Contains("void A()", gen);
            Assert.Contains("void B()", gen);
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
        public void ContractViolations_ProduceNoOutput(string member)
        {
            var user = $"using TEngine;\nnamespace GameLogic\n{{\n    [EventInterface(0)]\n    public interface IBad {{ {member} }}\n}}\n";
            var hints = RunGenerator(user);
            Assert.Empty(hints);
        }

        [Fact]
        public void InternalInterface_ProduceNoOutput()
        {
            var user = @"
using TEngine;
namespace GameLogic
{
    [EventInterface(0)]
    internal interface IInternal { void A(); }
}
";
            Assert.Empty(RunGenerator(user));
        }

        [Fact]
        public void NestedInterface_ProduceNoOutput()
        {
            var user = @"
using TEngine;
namespace GameLogic
{
    public class Outer
    {
        [EventInterface(0)]
        public interface INested { void A(); }
    }
}
";
            Assert.Empty(RunGenerator(user));
        }

        [Fact]
        public void InheritedInterface_ProduceNoOutput()
        {
            var user = @"
using TEngine;
namespace GameLogic
{
    public interface IBase { void BaseMethod(); }
    [EventInterface(0)]
    public interface IDerived : IBase { void A(); }
}
";
            Assert.Empty(RunGenerator(user));
        }

        [Fact]
        public void OtherLibrarySameNamedAttribute_ProduceNoOutput()
        {
            // 使用其他库同名的 EventInterfaceAttribute（非 TEngine 的语义符号），生成器不应触发。
            var user = @"
namespace Other.Library
{
    public class EventInterfaceAttribute : System.Attribute { }
}
namespace Other
{
    [Other.Library.EventInterface]
    public interface INotTEngine { void A(); }
}
";
            Assert.Empty(RunGenerator(user));
        }

        [Fact]
        public void Registrar_AssemblyAttributeBeforeNamespace_AndPublicCtor()
        {
            var user = @"
using TEngine;
namespace GameLogic
{
    [EventInterface(0)]
    public interface ILoginUI { void ShowLoginUI(); }
}
";
            var registrar = RunGeneratorAndGetSource(user, "EventAssemblyRegistrar_");
            var assemblyAttrIndex = registrar.IndexOf("[assembly: global::TEngine.EventAssemblyRegistrarAttribute", StringComparison.Ordinal);
            var namespaceIndex = registrar.IndexOf("namespace TEngine", StringComparison.Ordinal);
            Assert.True(assemblyAttrIndex >= 0 && namespaceIndex >= 0, "assembly 属性与 namespace 都要存在");
            Assert.True(assemblyAttrIndex < namespaceIndex, "程序集级特性必须位于 namespace 之前");
            Assert.Contains("public sealed class GeneratedEventAssemblyRegistrar_", registrar);
            Assert.Contains("IEventAssemblyRegistrar", registrar);
            Assert.Contains("new global::GameLogic.ILoginUI_Gen(dispatcher);", registrar);
        }

        [Fact]
        public void GeneratedOutput_CompilesWithoutError()
        {
            var user = @"
using TEngine;
namespace GameLogic
{
    [EventInterface(0)]
    public interface ILoginUI
    {
        void ShowLoginUI();
        void CloseLoginUI(int id);
    }
}
";
            var compilation = CreateCompilation(user, "TestAssembly");
            var driver = CSharpGeneratorDriver.Create(new[] { new EventInterfaceGenerator() });
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            var errors = outputCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToArray();
            Assert.True(errors.Length == 0, string.Join("\n---\n", errors));
        }

        [Fact]
        public void GlobalNamespaceInterface_GeneratesWithoutNamespaceWrap()
        {
            var user = @"
using TEngine;
[EventInterface(0)]
public interface IGlobal { void A(); }
";
            var hints = RunGenerator(user);
            Assert.Equal(3, hints.Length);
            var gen = RunGeneratorAndGetSource(user, "Gen_");
            Assert.DoesNotContain("namespace ", gen);
        }
    }
}