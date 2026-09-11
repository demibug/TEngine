using System.Collections.Generic;
using System.Linq;
using EventContract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EventAnalyzer;

public static class AnalyzerHelper
{
    /// <summary>
    /// 解析事件ID参数，提取接口名和方法名
    /// <remarks>e.g.: ITestUI_Event.Test -> InterfaceShortName="ITestUI", MethodName="Test"；
    /// 方法名使用 ValueText（去除 @ 转义），与接口符号成员名一致；同时保留 _Event 类语义符号供程序集定位。</remarks>
    /// </summary>
    public static bool TryParseEventId(ExpressionSyntax expression, SemanticModel semanticModel,
        out EventIdInfo? eventIdInfo)
    {
        eventIdInfo = null;

        // 处理 ITestUI_Event.Test 这种成员访问表达式
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            // 获取成员名（方法名）Test（ValueText 去除 @ 转义）
            var methodName = memberAccess.Name.Identifier.ValueText;
            INamedTypeSymbol? typeSymbol = null;
            var eventClassName = string.Empty;

            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression);
            if (symbolInfo.Symbol is INamedTypeSymbol namedTypeSymbol)
            {
                typeSymbol = namedTypeSymbol;
                eventClassName = namedTypeSymbol.Name;
            }
            else if (symbolInfo.Symbol is IErrorTypeSymbol errorTypeSymbol)
            {
                eventClassName = errorTypeSymbol.Name;
            }
            else if (memberAccess.Expression is IdentifierNameSyntax identifier)
            {
                eventClassName = identifier.Identifier.Text;
            }

            if (string.IsNullOrEmpty(eventClassName) || !eventClassName.EndsWith(Definition.EventClassNameEndsWith))
            {
                return false;
            }

            var interfaceName = eventClassName.Substring(0,
                eventClassName.Length - Definition.EventClassNameEndsWith.Length);
            eventIdInfo = new EventIdInfo
            {
                EventClassName = eventClassName,
                InterfaceShortName = interfaceName,
                MethodName = methodName,
                EventTypeSymbol = typeSymbol!
            };
            return true;
        }

        return false;
    }

    /// <summary>
    /// 查找对应的接口类型
    /// <remarks>主路径：依据 _Event 类符号的程序集归属 + 元数据全名定位（跨程序集事件接口、同名接口多程序集均可靠）；
    /// 解析不到符号时回退：遍历所有语法树查找同名接口。</remarks>
    /// </summary>
    /// <param name="compilation">编译对象</param>
    /// <param name="eventIdInfo">事件ID解析结果</param>
    /// <returns>找到的接口符号，未找到返回 null</returns>
    public static INamedTypeSymbol? FindInterface(Compilation compilation, EventIdInfo eventIdInfo)
    {
        // 候选全名（短名 + 命名空间限定名），主路径与回退共用。
        var possibleFullNames = new List<string> { eventIdInfo.InterfaceShortName };

        if (eventIdInfo.EventTypeSymbol != null)
        {
            // 主路径：处理器类与接口同命名空间且同程序集，用程序集限定查找，防止同名接口查错。
            var ns = eventIdInfo.EventTypeSymbol.ContainingNamespace;
            var nsText = ns.IsGlobalNamespace ? string.Empty : ns.ToDisplayString();

            if (!string.IsNullOrEmpty(nsText))
            {
                possibleFullNames.Insert(0, $"{nsText}.{eventIdInfo.InterfaceShortName}");
            }

            foreach (var fullName in possibleFullNames)
            {
                var symbol = eventIdInfo.EventTypeSymbol.ContainingAssembly.GetTypeByMetadataName(fullName);
                if (symbol != null && symbol.TypeKind == TypeKind.Interface)
                {
                    return symbol;
                }
            }
        }

        // 回退：遍历引用程序集（含当前编译）按候选全名查找（error symbol / 跨程序集场景兜底）。
        var assembliesToSearch = new List<IAssemblySymbol>();

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol referencedAssembly)
            {
                assembliesToSearch.Add(referencedAssembly);
            }
        }

        foreach (var assembly in assembliesToSearch)
        {
            foreach (var fullName in possibleFullNames)
            {
                var symbol = assembly.GetTypeByMetadataName(fullName);
                if (symbol != null && symbol.TypeKind == TypeKind.Interface)
                {
                    return symbol;
                }
            }
        }

        // 兜底：遍历所有语法树查找同名接口
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var root = syntaxTree.GetRoot();

            // 查找接口声明
            var interfaces = root.DescendantNodes()
                .OfType<InterfaceDeclarationSyntax>()
                .Where(i => i.Identifier.Text == eventIdInfo.InterfaceShortName);

            foreach (var interfaceDecl in interfaces)
            {
                // 获取命名空间
                var namespaceDecl = interfaceDecl.Ancestors()
                    .OfType<NamespaceDeclarationSyntax>()
                    .FirstOrDefault();

                string? namespaceName = namespaceDecl?.Name.ToString();

                if (namespaceName != null)
                {
                    var fullInterfaceName = $"{namespaceName}.{eventIdInfo.InterfaceShortName}";
                    var interfaceSymbol = compilation.GetTypeByMetadataName(fullInterfaceName);

                    if (interfaceSymbol != null && interfaceSymbol.TypeKind == TypeKind.Interface)
                    {
                        return interfaceSymbol;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 解析调用表达式实际引用的方法符号。
    /// <remarks>参数类型/数量错误时重载解析会失败（GetSymbolInfo(invocation) 返回 null），
    /// 而 EVENT001/EVENT002 恰恰要检测这类错误，因此回退到方法名符号绑定（泛型名/标识符/成员访问名）。
    /// 分析器与 CodeFix 共用，保证诊断和修复看到同一个符号。</remarks>
    /// </summary>
    /// <param name="semanticModel">语义模型</param>
    /// <param name="invocation">调用表达式</param>
    /// <returns>解析到的方法符号，无法解析返回 null</returns>
    public static IMethodSymbol? ResolveInvokedMethodSymbol(SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol != null)
        {
            return methodSymbol;
        }

        switch (invocation.Expression)
        {
            case GenericNameSyntax genericName:
                return semanticModel.GetSymbolInfo(genericName).Symbol as IMethodSymbol;

            case IdentifierNameSyntax identifierName:
                return semanticModel.GetSymbolInfo(identifierName).Symbol as IMethodSymbol;

            case MemberAccessExpressionSyntax memberAccess when memberAccess.Name is GenericNameSyntax memberGenericName:
                return semanticModel.GetSymbolInfo(memberGenericName).Symbol as IMethodSymbol;

            default:
                return null;
        }
    }

    /// <summary>
    /// 获取类型的显示名称
    /// <remarks>将系统类型转换为 C# 关键字别名（e.g. System.Int32 -> int）</remarks>
    /// </summary>
    /// <param name="type">类型符号</param>
    /// <returns>类型的显示名称</returns>
    public static string GetTypeName(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Int32:
                return "int";

            case SpecialType.System_String:
                return "string";

            case SpecialType.System_Boolean:
                return "bool";

            case SpecialType.System_Single:
                return "float";

            case SpecialType.System_Double:
                return "double";

            case SpecialType.System_Int64:
                return "long";

            case SpecialType.System_Byte:
                return "byte";

            case SpecialType.System_Char:
                return "char";

            case SpecialType.System_Object:
                return "object";

            default:
                return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }
    }

    /// <summary>
    /// 获取接口方法的参数信息列表
    /// </summary>
    /// <param name="interfaceMethod">接口方法符号</param>
    /// <returns>参数信息列表（类型名，参数名）</returns>
    public static List<(string TypeName, string ParamName)> GetParameterInfos(IMethodSymbol interfaceMethod)
    {
        return interfaceMethod.Parameters
            .Select(p => (GetTypeName(p.Type), p.Name))
            .ToList();
    }

    /// <summary>
    /// 构建正确的方法参数列表语法
    /// <remarks>参数名是 C# 关键字（如 event）时自动加 @ 转义，避免修复结果产生非法代码。</remarks>
    /// </summary>
    /// <param name="parameterInfos">参数信息列表</param>
    /// <returns>参数列表语法</returns>
    public static ParameterListSyntax BuildParameterList(List<(string TypeName, string ParamName)> parameterInfos)
    {
        var parameters = parameterInfos.Select(info =>
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(EventInterfaceContract.EscapeIdentifier(info.ParamName)))
                .WithType(SyntaxFactory.ParseTypeName(info.TypeName)
                    .WithTrailingTrivia(SyntaxFactory.Space)));

        return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));
    }

    /// <summary>
    /// 查找回调方法的声明
    /// </summary>
    /// <param name="callbackExpression">回调表达式（e.g. Test）</param>
    /// <param name="semanticModel">语义模型</param>
    /// <param name="root">语法树根节点</param>
    /// <returns>方法声明语法，未找到返回 null</returns>
    public static MethodDeclarationSyntax? FindCallbackMethodDeclaration(
        ExpressionSyntax callbackExpression,
        SemanticModel semanticModel,
        SyntaxNode root)
    {
        // 获取回调方法的符号
        var callbackSymbolInfo = semanticModel.GetSymbolInfo(callbackExpression);

        if (!(callbackSymbolInfo.Symbol is IMethodSymbol callbackMethodSymbol))
        {
            return null;
        }

        // 获取方法声明的位置
        var declaringReferences = callbackMethodSymbol.DeclaringSyntaxReferences;

        if (declaringReferences.Length == 0)
        {
            return null;
        }

        // 优先在当前语法树中查找
        foreach (var reference in declaringReferences)
        {
            if (reference.SyntaxTree == root.SyntaxTree)
            {
                var syntax = reference.GetSyntax();

                if (syntax is MethodDeclarationSyntax methodDecl)
                {
                    return methodDecl;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 修复回调方法的参数列表
    /// </summary>
    /// <param name="methodDeclaration">方法声明</param>
    /// <param name="parameterInfos">正确的参数信息</param>
    /// <returns>修复后的方法声明</returns>
    public static MethodDeclarationSyntax FixCallbackMethodParameters(
        MethodDeclarationSyntax methodDeclaration,
        List<(string TypeName, string ParamName)> parameterInfos)
    {
        var newParameterList = BuildParameterList(parameterInfos);
        return methodDeclaration.WithParameterList(newParameterList);
    }
}