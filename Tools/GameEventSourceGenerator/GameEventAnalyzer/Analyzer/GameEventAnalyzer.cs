using System.Collections.Immutable;
using System.Linq;
using EventContract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
namespace EventAnalyzer;

/// <summary>
/// 游戏事件分析器
/// <remarks>用于在编译时检测 事件监听方法 调用的泛型参数</remarks>
/// <remarks>是否与对应接口方法的参数类型一致</remarks>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GameEventAnalyzer : DiagnosticAnalyzer
{
    #region 诊断规则定义

    /// <summary>
    /// 参数类型不匹配规则
    /// <remarks>当泛型参数类型与接口方法参数类型不一致时触发</remarks>
    /// </summary>
    private static readonly DiagnosticDescriptor m_ruleTypeMismatch = new DiagnosticDescriptor(
        Definition.DiagnosticId_TypeMatch, // 诊断ID（唯一标识符）
        Definition.TitleTypeMatch, // 标题（简短描述）
        Definition.MessageFormatTypeMatch, // 错误消息模板（支持格式化参数）
        Definition.Category, // 类别（用于分组）
        DiagnosticSeverity.Error, // 严重级别
        isEnabledByDefault: true, // 是否默认启用
        description: Definition.DescriptionTypeMatch); // 详细说明（可选）

    /// <summary>
    /// 参数数量不匹配规则
    /// <remarks>当泛型参数数量与接口方法参数数量不一致时触发</remarks>
    /// </summary>
    private static readonly DiagnosticDescriptor m_ruleParamCountMismatch = new DiagnosticDescriptor(
        Definition.DiagnosticId_ParamCount,
        Definition.TitleParamCount,
        Definition.MessageFormatParamCount,
        Definition.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Definition.DescriptionParamCount);

    #endregion

    /// <summary>
    /// 返回此分析器支持的所有诊断规则
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(m_ruleTypeMismatch, m_ruleParamCountMismatch,
            EventInterfaceContract.RuleUnsupportedShape, EventInterfaceContract.RuleManualRegistrarAttribute);

    /// <summary>
    /// 初始化分析器，注册语法节点分析回调
    /// </summary>
    /// <param name="context">分析上下文</param>
    public override void Initialize(AnalysisContext context)
    {
        // 不分析自动生成的代码
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        // 启用并发执行以提高性能
        context.EnableConcurrentExecution();
        // 注册方法调用表达式的分析回调
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        // 注册事件接口形状检查（EVENT003）
        context.RegisterSyntaxNodeAction(AnalyzeInterfaceDeclaration, SyntaxKind.InterfaceDeclaration);
        // 注册程序集级事件注册标记检查（EVENT004，仅作用于手写代码）
        context.RegisterSyntaxNodeAction(AnalyzeRegistrarAttribute, SyntaxKind.Attribute);
    }

    /// <summary>
    /// 分析方法调用表达式
    /// </summary>
    /// <remarks>检测 事件监听方法 调用的泛型参数是否与接口方法参数匹配</remarks>
    /// <param name="context">语法节点分析上下文</param>
    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // 解析方法符号：参数类型匹配失败时重载解析会失败（GetSymbolInfo(invocation) 返回 null），
        // 而 EVENT001/EVENT002 恰恰要检测这种错误，因此经共用回退逻辑按方法名符号绑定。
        var methodSymbol = AnalyzerHelper.ResolveInvokedMethodSymbol(context.SemanticModel, invocation);
        if (methodSymbol == null)
        {
            return;
        }

        // 检查是否是 Definition 包含的事件监听调用方法
        if (!Definition.CheckMethodNameList.Contains(methodSymbol.Name))
        {
            return;
        }

        // Delegate 非泛型重载（handler 静态类型为 System.Delegate）没有泛型参数可校验，跳过避免误报。
        if (methodSymbol.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_Delegate))
        {
            return;
        }

        // 获取泛型参数
        var typeArguments = methodSymbol.TypeArguments;

        // 获取第一个参数（事件ID）
        var arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count == 0)
        {
            return;
        }

        var firstArg = arguments[0].Expression;

        // 解析事件ID参数，获取接口名和方法名
        if (!AnalyzerHelper.TryParseEventId(firstArg, context.SemanticModel, out var eventIdInfo))
        {
            return;
        }

        // 查找对应的接口
        var interfaceSymbol = AnalyzerHelper.FindInterface(context.Compilation, eventIdInfo!);

        if (interfaceSymbol == null)
        {
            return;
        }

        // 查找对应的方法
        var interfaceMethod = interfaceSymbol.GetMembers(eventIdInfo!.MethodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault();

        if (interfaceMethod == null)
        {
            return;
        }

        // 获取接口方法的参数类型
        var parameterTypes = interfaceMethod.Parameters.Select(p => p.Type).ToList();

        // 检查参数数量是否匹配
        if (typeArguments.Length != parameterTypes.Count)
        {
            var diagnostic = Diagnostic.Create(
                m_ruleParamCountMismatch, // 诊断规则描述符
                invocation.GetLocation(), // 错误位置
                typeArguments.Length, // 调用的参数数量
                eventIdInfo.InterfaceShortName, // "ILoginUI"
                eventIdInfo.MethodName, // "Test"
                parameterTypes.Count); // 原始方法参数数量

            context.ReportDiagnostic(diagnostic);
            return;
        }

        // 逐个比较参数类型
        for (int i = 0; i < typeArguments.Length; i++)
        {
            var actualType = typeArguments[i];
            var expectedType = parameterTypes[i];

            if (!SymbolEqualityComparer.Default.Equals(actualType, expectedType))
            {
                var diagnostic = Diagnostic.Create(
                    m_ruleTypeMismatch, // 诊断规则描述符
                    invocation.GetLocation(), // 错误位置
                    AnalyzerHelper.GetTypeName(actualType), // 实际的参数类型
                    eventIdInfo.InterfaceShortName, // "ILoginUI"
                    eventIdInfo.MethodName, // "Test"
                    AnalyzerHelper.GetTypeName(expectedType), // 期望的参数类型
                    i + 1); // 参数位置（从1开始）

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    /// <summary>
    /// 事件接口形状检查（EVENT003）：不符合支持契约的接口编译期报错，配合生成器跳过，保证不产出坏代码。
    /// </summary>
    private void AnalyzeInterfaceDeclaration(SyntaxNodeAnalysisContext context)
    {
        var interfaceNode = (InterfaceDeclarationSyntax)context.Node;
        var attributeType = context.Compilation.GetTypeByMetadataName(Definition.EventInterface);

        if (attributeType == null)
        {
            return;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceNode) as INamedTypeSymbol;
        if (symbol == null)
        {
            return;
        }

        // 语义匹配 TEngine.EventInterfaceAttribute，其他库同名特性不触发。
        var isEventInterface = symbol.GetAttributes().Any(a =>
            a.AttributeClass != null && SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));

        if (!isEventInterface)
        {
            return;
        }

        if (!EventInterfaceContract.TryValidate(symbol, out var reason))
        {
            var diagnostic = Diagnostic.Create(EventInterfaceContract.RuleUnsupportedShape,
                interfaceNode.Identifier.GetLocation(), symbol.ToDisplayString(), reason);
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// 程序集级事件注册标记检查（EVENT004）：禁止手写 [assembly: EventAssemblyRegistrar]。
    /// <remarks>自动生成代码被 ConfigureGeneratedCodeAnalysis(None) 跳过，此规则只会命中手写代码；
    /// 属于代码库约束，预编译/动态程序集不受此规则约束。</remarks>
    /// </summary>
    private void AnalyzeRegistrarAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;

        // 仅在程序集级特性上检查
        if (!(attribute.Parent is AttributeListSyntax attributeList) ||
            attributeList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) != true)
        {
            return;
        }

        var attributeType = context.Compilation.GetTypeByMetadataName(Definition.EventAssemblyRegistrar);
        if (attributeType == null)
        {
            return;
        }

        var symbolInfo = context.SemanticModel.GetSymbolInfo(attribute);
        if (!(symbolInfo.Symbol is IMethodSymbol constructor))
        {
            return;
        }

        if (constructor.ContainingType != null &&
            SymbolEqualityComparer.Default.Equals(constructor.ContainingType, attributeType))
        {
            var diagnostic = Diagnostic.Create(EventInterfaceContract.RuleManualRegistrarAttribute,
                attribute.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}