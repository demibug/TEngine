# 未决问题

以下问题不是本次源码调查的阻塞项，但会影响迁移时对启动契约、热更产物和退出语义的最终确认。

| 编号 | 问题 | 当前状态 | 关联证据/候选 | 最小闭合方式 |
|---|---|---|---|---|
| UQ-001 | CURRENT 的 GameEntry.Awake、RootModule.Awake、ResourceModuleDriver.Start、ProcedureSetting.StartProcedure continuation 的确切顺序是什么？ | 仅有源码推断 | S01-E019、S01-E021、S01-E023、S01-E026、S01-C001 | Development Player 加时间戳，或用 Unity lifecycle 日志/Profiler 核对 |
| UQ-002 | CURRENT 的 YooAsset/资源模块在缺包、版本失败、地址错误时是否能把 Procedure 推进到可见失败状态？ | 未运行验证 | S01-E023、S01-E024、S01-C002 | 构造缺失包与错误地址测试包，记录 FSM 当前状态和 UI |
| UQ-003 | CURRENT 的 AOT metadata 文件是否来自与 Player 构建相同的裁剪 DLL，GameProto/GameLogic bytes 是否被复制到 AssetRaw/DLL 并由 YooAsset 正确寻址？ | 源码不能证明产物闭环 | S01-E022、S01-E024、S01-C005 | 完整构建并检查 HybridCLRData、StreamingAssets、资源地址和 Player 日志 |
| UQ-004 | CURRENT 在 GameApp 类型/Entrance 缺失时，先进入 ProcedureStartGame 的行为是否会造成停滞或错误 UI？ | 源码可见顺序，运行时效果未知 | S01-E024、S01-C003 | 用三种故障包分别验证日志、状态和用户可恢复性 |
| UQ-005 | CURRENT 退出时 RootModule.OnDestroy 是否先触发 ModuleSystem.Shutdown，从而由 UpdateDriver.Shutdown 清空 DestroyEvent；两个独立对象的 OnDestroy 顺序和幂等性是什么？ | 代码已确认一条条件路径，Unity 对象顺序仍未验证；Editor 与 Player 代码分支不同 | S01-E021、S01-E025、S01-C004 | Player 退出、销毁 GameEntry、Editor Stop 各测一次并统计 GameApp.Release、UpdateDriver.MainBehaviour.OnDestroy、ModuleSystem.Shutdown 的顺序和次数 |
| UQ-006 | CURRENT GameModule.Shutdown 是否由某个生成代码、外部程序集或未检出的场景事件调用？ | 当前 Assets 检索未发现调用，但全运行时未证明 | S01-E025、S01-C009 | 全仓库/生成目录检索调用点，运行时对静态字段变化加日志 |
| UQ-007 | OLD 的 hotUpdate.txt、HybridCLRSettings.asset、构建脚本、最终 YooAsset 资源清单之间谁是源、谁是生成物？ | 配置集合存在差异 | S01-E011、S01-E012、S01-C005 | 追踪构建脚本写入与消费位置，比较最终 Player 的 DLL 集合 |
| UQ-008 | OLD 的 FrameworkEntry/GameEntry 反射桥接是否仅用于状态验证，还是计划替换 GameLaunch/LaunchLoader 的初始化？ | 当前可达主链支持前者，设计意图未确认 | S01-E010、S01-E014、S01-E017、S01-C006 | 运行时采样调用栈并查发布分支/迁移文档 |
| UQ-009 | OLD 的 FrameworkConfig.Inst.UseYooAsset 最终发布值来自哪个资产、构建覆盖或热更配置？ | 静态资产值与保留分支不能单独定论 | S01-E014、S01-E018、S01-E031、S01-C007 | 对最终包打印配置来源和值，核对实际 LaunchLoader commander |
| UQ-010 | OLD HotUpdateEntry、ILScriptProject/Main.cs 或其他未调用入口是否由外部生成代码、反射字符串或平台脚本触发？ | 未发现从 Assets 主链可达 | S01-E017、S01-E027、S01-E032、S01-C008 | 检索生成代码、RuntimeInitializeOnLoadMethod、反射字符串和构建脚本 |
| UQ-011 | OLD Launch 场景中各 Proxy 组件的脚本绑定是否在目标平台和生成阶段保持一致？ | 场景 YAML 能证明 GUID，未做 Unity 序列化解析/运行验证 | S01-E013 | 用 Unity 打开场景并检查组件类型、脚本 GUID、运行时 Awake 日志 |
| UQ-012 | 两个工程退出时是否要求支持“重启而不退出进程”，以及该语义是否属于迁移目标？ | 需求范围外，源码只记录现状 | S01-E018、S01-E021、S01-C004 | 明确目标生命周期，再决定是否需要统一 restart/quit 契约 |

## 已明确不在本次调查中闭合的事项

- 不判断任意业务模块的功能正确性。
- 不判断 DLL 加密密钥、CDN、版本号或资源内容的安全性。
- 不执行 Player 构建，不验证 HybridCLR 生成文件和 IL2CPP 产物。
- 不修改源码以“顺手修复”候选点。
