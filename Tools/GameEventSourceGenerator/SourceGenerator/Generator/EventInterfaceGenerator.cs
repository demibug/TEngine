using EventContract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

[Generator]
public class EventInterfaceGenerator : ISourceGenerator
{
    /// <summary>
    /// 事件接口元数据（按符号去重后收集）。
    /// </summary>
    private sealed class EventInterfaceInfo
    {
        public INamedTypeSymbol Symbol { get; set; } = null!;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// C# 源码全名（生成代码引用，global:: 形式）。
        /// </summary>
        public string CSharpFullName { get; set; } = string.Empty;

        /// <summary>
        /// 接口所在命名空间（全局命名空间时为空）。
        /// </summary>
        public string Namespace { get; set; } = string.Empty;

        /// <summary>
        /// 元数据全名（命名空间.接口名，分析器查找用）。
        /// </summary>
        public string MetadataName { get; set; } = string.Empty;

        /// <summary>
        /// 程序集完整身份键。
        /// </summary>
        public string AssemblyIdentityKey { get; set; } = string.Empty;

        public List<IMethodSymbol> Methods { get; } = new List<IMethodSymbol>();
    }

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // 语义匹配 TEngine.EventInterfaceAttribute：未引用 TEngine 或引用同名其他特性的程序集不产生任何输出。
        var attributeType = context.Compilation.GetTypeByMetadataName(Definition.EventInterface);
        if (attributeType == null)
        {
            return;
        }

        // 按语义符号去重：同一接口的多个 partial 声明只处理一次。
        var collected = new Dictionary<INamedTypeSymbol, EventInterfaceInfo>(SymbolEqualityComparer.Default);

        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var semanticModel = context.Compilation.GetSemanticModel(tree);
            foreach (var interfaceNode in tree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(interfaceNode) as INamedTypeSymbol;
                if (symbol == null)
                {
                    continue;
                }

                if (!HasEventInterfaceAttribute(symbol, attributeType))
                {
                    continue;
                }

                if (collected.ContainsKey(symbol))
                {
                    continue;
                }

                collected[symbol] = CollectInfo(symbol);
            }
        }

        if (collected.Count == 0)
        {
            return;
        }

        // 契约校验：不符合的形状不产出任何文件（由 GameEventAnalyzer EVENT003 编译期报错）。
        var legalInterfaces = new List<EventInterfaceInfo>();
        foreach (var info in collected.Values)
        {
            if (!EventInterfaceContract.TryValidate(info.Symbol, out _))
            {
                continue;
            }

            legalInterfaces.Add(info);
        }

        if (legalInterfaces.Count == 0)
        {
            return;
        }

        foreach (var info in legalInterfaces)
        {
            context.AddSource($"EventIds_{SanitizeFileName(info.MetadataName)}_{StableHash16(info.MetadataName)}.g.cs", GenerateEventClass(info));
            context.AddSource($"Gen_{SanitizeFileName(info.MetadataName)}_{StableHash16(info.MetadataName)}.g.cs", GenerateImplementationClass(info));
        }

        // 每个编译单元（程序集）至多生成一个 Registrar。
        context.AddSource($"EventAssemblyRegistrar_{StableHash16(GetAssemblySeed(context.Compilation.Assembly))}.g.cs", GenerateEventAssemblyRegistrar(context, legalInterfaces));
    }

    #region 语义收集

    /// <summary>
    /// 是否带 TEngine.EventInterfaceAttribute（语义匹配，不匹配其他库同名特性）。
    /// </summary>
    private static bool HasEventInterfaceAttribute(INamedTypeSymbol symbol, INamedTypeSymbol attributeType)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass != null &&
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从语义符号收集生成所需信息（成员来自完整符号，覆盖 partial 多声明）。
    /// </summary>
    private static EventInterfaceInfo CollectInfo(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString();
        var metadataName = string.IsNullOrEmpty(ns) ? symbol.Name : $"{ns}.{symbol.Name}";

        var info = new EventInterfaceInfo
        {
            Symbol = symbol,
            Name = symbol.Name,
            CSharpFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace = ns,
            MetadataName = metadataName,
            AssemblyIdentityKey = EventInterfaceContract.GetAssemblyIdentityKey(symbol.ContainingAssembly),
        };

        foreach (var member in symbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
            {
                info.Methods.Add(method);
            }
        }

        return info;
    }

    #endregion

    #region 生成：事件ID类

    /// <summary>
    /// 生成 {接口}_Event：事件 ID 静态字段，值为长度前缀编码的运行时事件键。
    /// </summary>
    private static string GenerateEventClass(EventInterfaceInfo info)
    {
        var builder = new StringBuilder();
        AppendGeneratedHeader(builder);
        AppendNamespaceOpen(builder, info.Namespace);
        builder.AppendLine($"    /// <summary>");
        builder.AppendLine($"    /// {info.Name} 事件ID（Source Generator 生成，勿手改）。");
        builder.AppendLine($"    /// </summary>");
        builder.AppendLine($"    [global::UnityEngine.Scripting.Preserve]");
        builder.AppendLine($"    public partial class {info.Name}_Event");
        builder.AppendLine("    {");

        foreach (var method in info.Methods)
        {
            var memberName = EventInterfaceContract.EscapeIdentifier(method.Name);
            var eventKey = EventInterfaceContract.GetEventKey(info.AssemblyIdentityKey, info.MetadataName, method.Name);
            builder.AppendLine($"        public static readonly int {memberName} = global::TEngine.RuntimeId.ToRuntimeId(\"{eventKey}\");");
        }

        builder.AppendLine("    }");
        AppendNamespaceClose(builder, info.Namespace);
        return builder.ToString();
    }

    #endregion

    #region 生成：接口实现类

    /// <summary>
    /// 生成 {接口}_Gen：实现事件接口，构造时注册，方法体向分发器发送事件。
    /// </summary>
    private static string GenerateImplementationClass(EventInterfaceInfo info)
    {
        var builder = new StringBuilder();
        AppendGeneratedHeader(builder);
        AppendNamespaceOpen(builder, info.Namespace);
        builder.AppendLine($"    /// <summary>");
        builder.AppendLine($"    /// {info.Name} 事件实现（Source Generator 生成，勿手改）。");
        builder.AppendLine($"    /// </summary>");
        builder.AppendLine($"    [global::UnityEngine.Scripting.Preserve]");
        builder.AppendLine($"    public partial class {info.Name}_Gen : {info.CSharpFullName}");
        builder.AppendLine("    {");
        builder.AppendLine("        private global::TEngine.EventDispatcher _dispatcher;");
        builder.AppendLine();
        builder.AppendLine($"        public {info.Name}_Gen(global::TEngine.EventDispatcher dispatcher)");
        builder.AppendLine("        {");
        builder.AppendLine("            _dispatcher = dispatcher;");
        builder.AppendLine($"            global::TEngine.GameEvent.EventMgr.RegWrapInterface<{info.CSharpFullName}>(this);");
        builder.AppendLine("        }");
        builder.AppendLine();

        for (var i = 0; i < info.Methods.Count; i++)
        {
            var method = info.Methods[i];
            var memberName = EventInterfaceContract.EscapeIdentifier(method.Name);
            var parameters = GenerateParameters(method);
            builder.AppendLine($"        public void {memberName}({parameters})");
            builder.AppendLine("        {");

            var arguments = string.Join(", ", method.Parameters.Select(p => EventInterfaceContract.EscapeIdentifier(p.Name)));
            if (method.Parameters.Length == 0)
            {
                builder.AppendLine($"            _dispatcher.Send({info.Name}_Event.{memberName});");
            }
            else
            {
                builder.AppendLine($"            _dispatcher.Send({info.Name}_Event.{memberName}, {arguments});");
            }

            builder.AppendLine("        }");

            if (i < info.Methods.Count - 1)
            {
                builder.AppendLine();
            }
        }

        builder.AppendLine("    }");
        AppendNamespaceClose(builder, info.Namespace);
        return builder.ToString();
    }

    /// <summary>
    /// 生成方法参数（类型用全局限定名，参数名按需转义）。
    /// </summary>
    private static string GenerateParameters(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p =>
            $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {EventInterfaceContract.EscapeIdentifier(p.Name)}"));
    }

    #endregion

    #region 生成：程序集注册器

    /// <summary>
    /// 生成程序集级事件注册器（public + 程序集唯一名 + 显式构造，供 Activator.CreateInstance 实例化）。
    /// [assembly:] 特性置于文件顶部（using 之后、namespace 之前）。
    /// </summary>
    private static string GenerateEventAssemblyRegistrar(GeneratorExecutionContext context, List<EventInterfaceInfo> legalInterfaces)
    {
        var assemblySeed = GetAssemblySeed(context.Compilation.Assembly);
        var registrarClassName = $"GeneratedEventAssemblyRegistrar_{SanitizeIdentifier(context.Compilation.AssemblyName ?? "UNKNOWN", 16)}_{StableHash16(assemblySeed)}";

        var builder = new StringBuilder();
        AppendGeneratedHeader(builder);
        builder.AppendLine($"[assembly: global::TEngine.EventAssemblyRegistrarAttribute(typeof(global::TEngine.{registrarClassName}))]");
        builder.AppendLine();
        builder.AppendLine($"namespace {Definition.FrameworkNameSpace}");
        builder.AppendLine("{");
        builder.AppendLine($"    /// <summary>");
        builder.AppendLine($"    /// {context.Compilation.AssemblyName} 程序集的事件接口注册器（Source Generator 生成，勿手改）。");
        builder.AppendLine($"    /// </summary>");
        builder.AppendLine($"    [global::UnityEngine.Scripting.Preserve]");
        builder.AppendLine($"    public sealed class {registrarClassName} : global::TEngine.IEventAssemblyRegistrar");
        builder.AppendLine("    {");
        builder.AppendLine();
        builder.AppendLine($"        public {registrarClassName}()");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public void Register(global::TEngine.EventDispatcher dispatcher)");
        builder.AppendLine("        {");

        foreach (var info in legalInterfaces)
        {
            var referencedName = string.IsNullOrEmpty(info.Namespace)
                ? $"global::{info.Name}_Gen"
                : $"global::{info.Namespace}.{info.Name}_Gen";
            builder.AppendLine($"            new {referencedName}(dispatcher);");
        }

        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    #endregion

    #region 辅助

    /// <summary>
    /// 程序集标识种子（完整身份，含名称/版本/公钥令牌）。
    /// </summary>
    private static string GetAssemblySeed(IAssemblySymbol assembly)
    {
        return EventInterfaceContract.GetAssemblyIdentityKey(assembly);
    }

    /// <summary>
    /// 稳定哈希：MD5 前 16 位 hex 大写（工程规模下碰撞概率可忽略；不宣称数学绝对唯一）。
    /// </summary>
    private static string StableHash16(string seed)
    {
        using (var md5 = MD5.Create())
        {
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
            return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToUpperInvariant();
        }
    }

    /// <summary>
    /// 清洗为文件名字符（仅字母数字与下划线，其余替换为下划线）。
    /// </summary>
    private static string SanitizeFileName(string text)
    {
        return new string(text.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_').ToArray());
    }

    /// <summary>
    /// 清洗为合法 C# 标识符，截断长度，规避数字开头。
    /// </summary>
    private static string SanitizeIdentifier(string text, int maxLength)
    {
        var cleaned = new string(text.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned.Substring(0, maxLength);
        }

        if (cleaned.Length == 0)
        {
            cleaned = "UNKNOWN";
        }

        if (char.IsDigit(cleaned[0]))
        {
            cleaned = "G_" + cleaned;
        }

        return cleaned;
    }

    /// <summary>
    /// 生成文件头（auto-generated 标记使分析器跳过生成代码）。
    /// </summary>
    private static void AppendGeneratedHeader(StringBuilder builder)
    {
        builder.AppendLine("//------------------------------------------------------------------------------");
        builder.AppendLine("// <auto-generated>");
        builder.AppendLine("//     This code was generated by TEngine GameEventSourceGenerator.");
        builder.AppendLine("//     Changes to this file may cause incorrect behavior and will be lost if");
        builder.AppendLine("//     the code is regenerated.");
        builder.AppendLine("// </auto-generated>");
        builder.AppendLine("//------------------------------------------------------------------------------");
        builder.AppendLine();
    }

    /// <summary>
    /// 命名空间开始（全局命名空间时省略）。
    /// </summary>
    private static void AppendNamespaceOpen(StringBuilder builder, string ns)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return;
        }

        builder.AppendLine($"namespace {ns}");
        builder.AppendLine("{");
    }

    /// <summary>
    /// 命名空间结束（全局命名空间时省略）。
    /// </summary>
    private static void AppendNamespaceClose(StringBuilder builder, string ns)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return;
        }

        builder.AppendLine("}");
    }

    #endregion
}