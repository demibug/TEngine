using System.Collections.Generic;
using Microsoft.CodeAnalysis;
namespace EventAnalyzer;

/// <summary>
/// 事件分析器常量定义
/// </summary>
public sealed class Definition
{
    #region 诊断ID

    /// <summary>
    /// 参数类型不匹配的诊断ID
    /// </summary>
    public const string DiagnosticId_TypeMatch = "EVENT001";

    /// <summary>
    /// 参数数量不匹配的诊断ID
    /// </summary>
    public const string DiagnosticId_ParamCount = "EVENT002";

    #endregion

    #region 类型不匹配诊断信息

    /// <summary>
    /// 类型不匹配错误标题
    /// </summary>
    public static readonly LocalizableString TitleTypeMatch = "事件参数类型不匹配";

    /// <summary>
    /// 类型不匹配错误消息格式
    /// {0}: 实际泛型参数类型
    /// {1}: 接口名
    /// {2}: 方法名
    /// {3}: 期望的参数类型
    /// {4}: 参数位置
    /// </summary>
    public static readonly LocalizableString MessageFormatTypeMatch = "泛型参数类型 '{0}' 与接口方法 '{1}.{2}' 的参数类型 '{3}' 不匹配 (第 {4} 个参数)";

    /// <summary>
    /// 类型不匹配错误描述
    /// </summary>
    public static readonly LocalizableString DescriptionTypeMatch = "事件监听方法的泛型参数类型必须与对应接口方法的参数类型一致。";

    #endregion

    #region 数量不匹配诊断信息

    /// <summary>
    /// 参数数量不匹配错误标题
    /// </summary>
    public static readonly LocalizableString TitleParamCount = "事件参数数量不匹配";

    /// <summary>
    /// 参数数量不匹配错误消息格式
    /// <remarks>{0}: 实际泛型参数数量</remarks>
    /// <remarks>{1}: 接口名</remarks>
    /// <remarks>{2}: 方法名</remarks>
    /// <remarks>{3}: 期望的参数数量</remarks>
    /// </summary>
    public static readonly LocalizableString MessageFormatParamCount = "泛型参数数量 ({0}) 与接口方法 '{1}.{2}' 的参数数量 ({3}) 不匹配";

    /// <summary>
    /// 参数数量不匹配错误描述
    /// </summary>
    public static readonly LocalizableString DescriptionParamCount = "事件监听方法的泛型参数数量必须与对应接口方法的参数数量一致。";

    #endregion

    /// <summary>
    /// 诊断类别
    /// </summary>
    public const string Category = "GameEvent";

    /// <summary>
    /// 需要检测的方法名列表
    /// <remarks>Add 与 Remove 成对检查：Remove 泛型参数写错会导致静默移除失败，与 Add 同构。
    /// 非 UI 类监听建议经 GameEventMgr（AddEvent），其事件 ID 同样来自生成类字段。</remarks>
    /// </summary>
    public static readonly List<string> CheckMethodNameList =
    [
        "AddUIEvent", "AddEventListener", "RemoveEventListener"
    ];

    /// <summary>
    /// 事件类型后缀结尾
    /// </summary>
    public const string EventClassNameEndsWith = "_Event";

    /// <summary>
    /// TEngine.EventInterfaceAttribute 元数据全名（语义匹配用）。
    /// </summary>
    public const string EventInterface = "TEngine.EventInterfaceAttribute";

    /// <summary>
    /// TEngine.EventAssemblyRegistrarAttribute 元数据全名（语义匹配用）。
    /// </summary>
    public const string EventAssemblyRegistrar = "TEngine.EventAssemblyRegistrarAttribute";
}