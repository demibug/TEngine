using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using EventAnalyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace GameEventAnalyzer.Tests
{
    /// <summary>
    /// CodeFix 单测：手动驱动 CodeFixProvider（不依赖第三方测试框架包），验证 EVENT001/EVENT002 的修复结果。
    /// 覆盖：重载解析失败时的符号回退路径、回调方法参数修复、关键字参数名 @ 转义、RemoveEventListener 检查与 Delegate 重载防误报。
    /// </summary>
    public sealed class GameEventCodeFixTests
    {
        private static IEnumerable<MetadataReference> BaseReferences()
        {
            return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
            };
        }

        private static Document CreateDocument(string source)
        {
            var workspace = new AdhocWorkspace();
            var project = workspace.CurrentSolution
                .AddProject("ProbeProject", "ProbeProject", LanguageNames.CSharp)
                .AddMetadataReferences(BaseReferences());
            return project.AddDocument("Probe.cs", source);
        }

        /// <summary>
        /// 对文档运行分析器，断言产生了指定诊断并返回它。
        /// </summary>
        private static async System.Threading.Tasks.Task<Diagnostic> GetEventDiagnosticAsync(Document document,
            string diagnosticId)
        {
            var compilation = (await document.Project.GetCompilationAsync())!;
            var diagnostics = await compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new EventAnalyzer.GameEventAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();

            var diagnostic = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
            Assert.NotNull(diagnostic);
            return diagnostic!;
        }

        /// <summary>
        /// 驱动 CodeFixProvider 应用第一个修复操作，返回修复后的源码文本。
        /// </summary>
        private static async System.Threading.Tasks.Task<string> ApplyCodeFixAsync(Document document,
            Diagnostic diagnostic, CodeFixProvider provider)
        {
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic,
                (action, _) => actions.Add(action), CancellationToken.None);
            await provider.RegisterCodeFixesAsync(context);

            var action = Assert.Single(actions);
            var operations = await action.GetOperationsAsync(CancellationToken.None);

            var solution = document.Project.Solution;
            foreach (var operation in operations)
            {
                if (operation is ApplyChangesOperation applyChanges)
                {
                    solution = applyChanges.ChangedSolution;
                }
            }

            Assert.NotSame(document.Project.Solution, solution);
            var fixedDocument = solution.GetDocument(document.Id)!;
            return (await fixedDocument.GetTextAsync()).ToString();
        }

        /// <summary>
        /// 去除空白后比较，避免修复产物的空格格式差异影响断言。
        /// </summary>
        private static string Normalize(string text)
        {
            return text.Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        [Fact]
        public async System.Threading.Tasks.Task Event002_ParamCountMissing_FixesGenericAndCallback()
        {
            // 泛型参数数量不足（1 个 vs 接口 2 参），且回调签名与任何可用重载不匹配（重载解析失败 → 走回退路径）。
            var source = @"
namespace GameLogic
{
    public interface IProbe { void OnData(int code, string msg); }
    public static class IProbe_Event { public const int OnData = 1; }
    internal static class Impl
    {
        private static void AddEventListener<T1>(int eventId, System.Action<T1> handler) { }
        private static void OnDataHandler(int code) { }
        public static void Register()
        {
            AddEventListener<int>(IProbe_Event.OnData, OnDataHandler);
        }
    }
}
";
            var document = CreateDocument(source);
            var diagnostic = await GetEventDiagnosticAsync(document, "EVENT002");
            var fixedText = await ApplyCodeFixAsync(document, diagnostic, new GameEventParamCountCodeFixProvider());

            var normalized = Normalize(fixedText);
            // 泛型参数补齐为接口方法的参数类型列表，回调签名同步替换。
            Assert.Contains("AddEventListener<int,string>(IProbe_Event.OnData,OnDataHandler)", normalized);
            Assert.Contains("OnDataHandler(intcode,stringmsg)", normalized);
        }

        [Fact]
        public async System.Threading.Tasks.Task Event001_TypeMismatch_FixesGenericAndCallback()
        {
            // 泛型参数数量正确但类型错误（第 1 参应为 int），调用可正常重载解析。
            var source = @"
namespace GameLogic
{
    public interface IProbe { void OnData(int code, string msg); }
    public static class IProbe_Event { public const int OnData = 1; }
    internal static class Impl
    {
        private static void AddEventListener<T1, T2>(int eventId, System.Action<T1, T2> handler) { }
        private static void OnDataHandler(string code, string msg) { }
        public static void Register()
        {
            AddEventListener<string, string>(IProbe_Event.OnData, OnDataHandler);
        }
    }
}
";
            var document = CreateDocument(source);
            var diagnostic = await GetEventDiagnosticAsync(document, "EVENT001");
            var fixedText = await ApplyCodeFixAsync(document, diagnostic, new GameEventTypeCodeFixProvider());

            var normalized = Normalize(fixedText);
            Assert.Contains("AddEventListener<int,string>(IProbe_Event.OnData,OnDataHandler)", normalized);
            Assert.Contains("OnDataHandler(intcode,stringmsg)", normalized);
        }

        [Fact]
        public async System.Threading.Tasks.Task KeywordParameterName_EscapedInFixedCallback()
        {
            // 接口方法参数名是 C# 关键字（event），修复后的回调签名必须输出 @event 才是合法代码。
            var source = @"
namespace GameLogic
{
    public interface IProbe { void OnData(int code, string @event); }
    public static class IProbe_Event { public const int OnData = 1; }
    internal static class Impl
    {
        private static void AddEventListener<T1>(int eventId, System.Action<T1> handler) { }
        private static void OnDataHandler(int code) { }
        public static void Register()
        {
            AddEventListener<int>(IProbe_Event.OnData, OnDataHandler);
        }
    }
}
";
            var document = CreateDocument(source);
            var diagnostic = await GetEventDiagnosticAsync(document, "EVENT002");
            var fixedText = await ApplyCodeFixAsync(document, diagnostic, new GameEventParamCountCodeFixProvider());

            Assert.Contains("string@event)", Normalize(fixedText));
        }

        [Fact]
        public async System.Threading.Tasks.Task RemoveEventListener_ParamCount_ReportedAndFixed()
        {
            // Remove 与 Add 同构检查：泛型数量错误同样诊断并可修复，防止静默移除失败。
            var source = @"
namespace GameLogic
{
    public interface IProbe { void OnData(int code, string msg); }
    public static class IProbe_Event { public const int OnData = 1; }
    internal static class Impl
    {
        private static void RemoveEventListener<T1>(int eventId, System.Action<T1> handler) { }
        private static void OnDataHandler(int code) { }
        public static void Unregister()
        {
            RemoveEventListener<int>(IProbe_Event.OnData, OnDataHandler);
        }
    }
}
";
            var document = CreateDocument(source);
            var diagnostic = await GetEventDiagnosticAsync(document, "EVENT002");
            var fixedText = await ApplyCodeFixAsync(document, diagnostic, new GameEventParamCountCodeFixProvider());

            var normalized = Normalize(fixedText);
            Assert.Contains("RemoveEventListener<int,string>(IProbe_Event.OnData,OnDataHandler)", normalized);
            Assert.Contains("OnDataHandler(intcode,stringmsg)", normalized);
        }

        [Fact]
        public async System.Threading.Tasks.Task RemoveEventListener_DelegateOverload_NotReported()
        {
            // handler 静态类型为 System.Delegate 的非泛型重载没有泛型参数可校验，不应误报。
            var source = @"
namespace GameLogic
{
    public interface IProbe { void OnData(int code, string msg); }
    public static class IProbe_Event { public const int OnData = 1; }
    internal static class Impl
    {
        private static void RemoveEventListener<T1>(int eventId, System.Action<T1> handler) { }
        private static void RemoveEventListener(int eventId, System.Delegate handler) { }
        private static System.Delegate _handler;
        public static void Unregister()
        {
            RemoveEventListener(IProbe_Event.OnData, _handler);
        }
    }
}
";
            var document = CreateDocument(source);
            var compilation = await document.Project.GetCompilationAsync();
            var diagnostics = await compilation!
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new EventAnalyzer.GameEventAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();

            Assert.DoesNotContain(diagnostics, d => d.Id is "EVENT001" or "EVENT002");
        }
    }
}
