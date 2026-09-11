using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EventContract
{
    /// <summary>
    /// 事件接口支持契约（Source Generator 与 GameEventAnalyzer 共享的唯一事实源）。
    /// 支持：顶层、非泛型、public、无继承的接口；void 实例方法；0~MaxEventParameters 个普通值/引用参数；
    /// 参数类型不为指针/函数指针/ref struct；参数不带 params/默认值/ref、out、in 修饰；无同名重载、无泛型方法；
    /// 接口内不允许静态成员、属性、事件、常量、索引器、嵌套类型等其他成员。
    /// 注意：scoped 修饰符无需单独校验（仅能修饰 ref 参数或 ref struct 类型），已由 ref/out/in 与 IsRefLikeType 规则覆盖。
    /// </summary>
    public static class EventInterfaceContract
    {
        /// <summary>
        /// 参数数量上限。对齐 GameEventMgr（5）与 GameEvent.RemoveEventListener（5）；底层 EventDispatcher.Send 实际支持到 6。
        /// </summary>
        public const int MaxEventParameters = 5;

        #region 诊断规则

        /// <summary>
        /// EVENT003：事件接口形状不支持。
        /// </summary>
        public static readonly DiagnosticDescriptor RuleUnsupportedShape = new DiagnosticDescriptor(
            id: "EVENT003",
            title: "事件接口形状不支持",
            messageFormat: "事件接口 '{0}' 形状不受支持：{1}。仅支持顶层、非泛型、public 且无继承的接口；方法必须为 void 实例方法；参数 0~5 个；不允许重载、泛型方法、ref/out/in、params、默认参数及指针/ref struct 参数；不允许静态成员、属性、事件、常量、索引器、嵌套类型等其他成员。",
            category: "GameEvent",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "事件接口必须符合 EventInterfaceContract 支持契约。");

        /// <summary>
        /// EVENT004：手写程序集级事件注册标记（生成代码自动被分析器跳过，此规则仅约束工程源码）。
        /// 注意：这是代码库约束，不是运行时安全边界——预编译程序集或反射动态程序集不受此规则约束。
        /// </summary>
        public static readonly DiagnosticDescriptor RuleManualRegistrarAttribute = new DiagnosticDescriptor(
            id: "EVENT004",
            title: "禁止手写事件注册标记",
            messageFormat: "禁止手动添加 [EventAssemblyRegistrar]，该程序集级特性由 Source Generator 自动生成。",
            category: "GameEvent",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "事件注册标记只能由 Source Generator 生成，手动添加会导致重复或自定义注册器。");

        #endregion

        #region 契约校验

        /// <summary>
        /// 校验事件接口是否符合支持契约。
        /// </summary>
        /// <param name="symbol">接口语义符号。</param>
        /// <param name="reason">不满足时输出原因（供诊断文案）。</param>
        /// <returns>true 表示符合契约。</returns>
        public static bool TryValidate(INamedTypeSymbol symbol, out string reason)
        {
            reason = string.Empty;

            if (symbol.TypeKind != TypeKind.Interface)
            {
                reason = "非接口类型";
                return false;
            }

            if (symbol.ContainingType != null)
            {
                reason = "不允许嵌套接口";
                return false;
            }

            if (symbol.IsGenericType || symbol.TypeParameters.Length > 0)
            {
                reason = "不允许泛型接口";
                return false;
            }

            if (symbol.DeclaredAccessibility != Accessibility.Public)
            {
                reason = "接口必须为 public";
                return false;
            }

            if (symbol.Interfaces.Length > 0)
            {
                reason = "不允许继承其他接口";
                return false;
            }

            var paramCount = 0;
            foreach (var member in symbol.GetMembers())
            {
                if (member is IMethodSymbol method)
                {
                    if (method.MethodKind != MethodKind.Ordinary)
                    {
                        reason = "方法成员只能是普通实例方法";
                        return false;
                    }

                    if (method.IsStatic)
                    {
                        reason = "不允许静态方法";
                        return false;
                    }

                    if (!method.IsAbstract)
                    {
                        reason = "不允许默认接口实现";
                        return false;
                    }

                    if (method.DeclaredAccessibility != Accessibility.Public)
                    {
                        reason = "接口方法必须为 public";
                        return false;
                    }

                    if (method.IsGenericMethod)
                    {
                        reason = "不允许泛型方法";
                        return false;
                    }

                    if (method.ReturnType.SpecialType != SpecialType.System_Void)
                    {
                        reason = "方法必须返回 void";
                        return false;
                    }

                    if (symbol.GetMembers(method.Name).OfType<IMethodSymbol>().Count() > 1)
                    {
                        reason = $"不允许同名方法重载 '{method.Name}'";
                        return false;
                    }

                    paramCount = method.Parameters.Length;
                    if (paramCount > MaxEventParameters)
                    {
                        reason = $"参数数量超过上限（{MaxEventParameters}）";
                        return false;
                    }

                    foreach (var parameter in method.Parameters)
                    {
                        if (parameter.RefKind != RefKind.None)
                        {
                            reason = "不允许 ref/out/in 参数";
                            return false;
                        }

                        if (parameter.IsParams)
                        {
                            reason = "不允许 params 参数";
                            return false;
                        }

                        if (parameter.HasExplicitDefaultValue)
                        {
                            reason = "不允许默认参数值";
                            return false;
                        }

                        if (IsUnsupportedParameterType(parameter.Type))
                        {
                            reason = "不允许指针/函数指针/ref struct 等参数类型";
                            return false;
                        }
                    }

                    continue;
                }

                // 接口内除方法外的其他成员一律不允许（属性/索引器/事件/字段/常量/嵌套类型等）。
                if (member is IFieldSymbol || member is IPropertySymbol || member is IEventSymbol || member is INamedTypeSymbol || member is INamespaceOrTypeSymbol)
                {
                    reason = "接口内只允许方法成员";
                    return false;
                }

                reason = "接口内存在不支持的其他成员";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 参数类型是否不受支持（指针、函数指针、ref struct 族）。
        /// </summary>
        private static bool IsUnsupportedParameterType(ITypeSymbol type)
        {
            if (type == null)
            {
                return false;
            }

            if (type is IPointerTypeSymbol || type is IFunctionPointerTypeSymbol)
            {
                return true;
            }

            return type is INamedTypeSymbol named && named.IsRefLikeType;
        }

        #endregion

        #region 命名规范（Event ID 生成 / 包装类生成 / Analyzer 解析 / CodeFix 查找 统一入口）

        /// <summary>
        /// 去除 C# 标识符转义前缀（@），得到成员原始名。Analyzer 从语法节点取标识符时使用。
        /// </summary>
        public static string Unescape(string identifierText)
        {
            return identifierText.StartsWith("@", StringComparison.Ordinal) ? identifierText.Substring(1) : identifierText;
        }

        /// <summary>
        /// 判断名称是否为 C# 关键字（生成代码时需要 @ 转义）。
        /// </summary>
        public static bool IsKeyword(string name)
        {
            return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None;
        }

        /// <summary>
        /// 生成合法 C# 标识符（关键字加 @ 前缀）。生成代码中使用。
        /// </summary>
        public static string EscapeIdentifier(string name)
        {
            return IsKeyword(name) ? "@" + name : name;
        }

        /// <summary>
        /// 程序集完整身份键（不依赖 Roslyn AssemblyIdentity.ToString 的具体格式）。
        /// 格式：Name|Version|PublicKeyTokenHex。
        /// </summary>
        public static string GetAssemblyIdentityKey(IAssemblySymbol assembly)
        {
            if (assembly == null)
            {
                return "UNKNOWN";
            }

            var identity = assembly.Identity;
            var publicKeyToken = identity.PublicKeyToken;
            var tokenText = publicKeyToken.IsDefaultOrEmpty
                ? string.Empty
                : BitConverter.ToString(publicKeyToken.ToArray()).Replace("-", string.Empty).ToUpperInvariant();

            return $"{identity.Name}|{identity.Version}|{tokenText}";
        }

        /// <summary>
        /// 生成运行期事件键：长度前缀拼接，保证不同三元组（程序集身份/接口全名/方法名）编码不碰撞。
        /// </summary>
        public static string GetEventKey(string assemblyIdentityKey, string interfaceMetadataName, string methodName)
        {
            return LengthPrefix(assemblyIdentityKey, interfaceMetadataName, methodName);
        }

        /// <summary>
        /// 长度前缀拼接："{len1}:{s1}{len2}:{s2}{len3}:{s3}"。
        /// 长度一律使用固定区域设置（invariant）数字格式，语义可逆、不可碰撞。
        /// </summary>
        private static string LengthPrefix(params string[] segments)
        {
            var builder = new StringBuilder();
            foreach (var segment in segments)
            {
                builder.Append(segment.Length.ToString(CultureInfo.InvariantCulture));
                builder.Append(':');
                builder.Append(segment);
            }

            return builder.ToString();
        }

        #endregion
    }

    /// <summary>
    /// 事件ID表达式解析结果（如 ILoginUI_Event.ShowLoginUI）。
    /// 方法名已去除 @ 转义（ValueText 语义），与接口符号成员名一致。
    /// </summary>
    public sealed class EventIdInfo
    {
        /// <summary>
        /// 事件类名（ILoginUI_Event）。
        /// </summary>
        public string EventClassName { get; set; } = string.Empty;

        /// <summary>
        /// 接口短名（ILoginUI）。
        /// </summary>
        public string InterfaceShortName { get; set; } = string.Empty;

        /// <summary>
        /// 方法名（无 @ 前缀）。
        /// </summary>
        public string MethodName { get; set; } = string.Empty;

        /// <summary>
        /// _Event 类的语义符号（语法可解析时非空；用于定位接口的程序集归属）。
        /// </summary>
        public INamedTypeSymbol EventTypeSymbol { get; set; } = null!;
    }
}