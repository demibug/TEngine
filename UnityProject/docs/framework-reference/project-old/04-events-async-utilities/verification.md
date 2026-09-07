# S04 验证记录

## 1. 调查与写入基线

| 检查 | 实际结果 |
|---|---|
| 调查日期 | 主源码调查在 2026-09-06 完成；最终文档和范围核验在 2026-09-07 完成，Asia/Shanghai |
| OLD 可读性 | D:\Work\SAUnity\ProjectOld 存在；能读取指定 framework、game、ILScriptProject/Core、LocalPackages 入口 |
| CURRENT 可写范围 | 只创建/写入 docs/framework-reference/project-old/04-events-async-utilities |
| 说明文件 | 读取当前工程 AGENT.md，并按其要求读取 CLAUDE.md；OLD 入口未发现额外 AGENT/CLAUDE 指令 |
| Unity 版本 | 两工程 ProjectSettings/ProjectVersion.txt:1-2 均为 2022.3.62f2 (7670c08855a9) |
| 旧工程版本信息 | OLD 根级无 Git 工作树/根级 SVN working copy；framework 与 game 的嵌套 SVN info 均为 revision 10480 |
| 当前 git 基线 | 写入前保留 ProjectSettings/boot.config 删除、UserSettings/Layouts/default-2022.dwlt 未跟踪及其他 slave handoff 未跟踪项 |

OLD 参考工程全程按只读方式检查，没有执行其脚本、Unity、导表、生成、更新或 SVN update。没有读取 Unity 根级 Library、Temp、Logs 等缓存目录；调查到的 OLD Assets/Scripts/framework/Library 是框架源码目录，不是 Unity 根级缓存。

## 2. 证据核验

已按证据索引回查以下关键定义和调用位置：

- OLD EventDispatcher 的 Add/Remove/Dispatch、CallbackVo 参数清理和 NotifyDispatcher 双缓冲：S04-E003～S04-E007。
- OLD TimelineMgr 的真实 RegisterNotify → ModuleMgr → EventDispatcher，以及 CommonModule OnDispose → UnRegisterNotify → Remove：S04-E008。
- OLD UniTaskManager、BasePanel cancellation、AsyncTodo、FairyGUI.Timers、DelayTodo、RunTimer、两套业务 FSM：S04-E010～S04-E016。
- CURRENT GameEvent 生成入口、EventDelegateData 的 pending list/异常路径、GameEventMgr/UIBase 生命周期：S04-E017～S04-E020。
- CURRENT TimerModule/UI HideTimerId、RootModule/ModuleSystem 主循环、ResourceExtComponent linked CTS/finally、Fsm/Procedure、MemoryPool/ObjectPool/AssetObject：S04-E021～S04-E027。
- CURRENT 资源回调转移与销毁、RootModule 的 `!UNITY_EDITOR` 关闭条件/GameEvent.Shutdown 可达性、Fsm 清理回调到 map/池归还：S04-E028～S04-E030。

行号核验方式是 PowerShell Get-Content 带序号和 rg -n 的只读定位；evidence.md 没有粘贴完整源码。

## 3. 版本和快照复查

### 3.1 SVN/Git 范围

- OLD Assets/Scripts/framework：URL 为 https://10.0.2.9/svn/SA/client/trunk/framework，working-copy revision 10480，last changed revision 10453，日期 2026-06-12 16:32:17 +0800。
- OLD Assets/Scripts/game：URL 为 https://10.0.2.9/svn/SA/client/trunk/game，working-copy revision 10480，last changed revision 10478，日期 2026-06-12 18:38:57 +0800。
- OLD ILScriptProject/Core 不是 SVN working copy；OLD 根目录也没有统一 revision。因此没有声称整个 OLD 工程处于单一提交。
- CURRENT 调查分支为 framework，调查前 HEAD 为 16afccb5df2a2a2efcb5003ecf9fbc0781c0170a，提交主题 change version；未执行提交、暂存、恢复或清理。

### 3.2 OLD 关键源码 SHA-256

以下 hash 已用 Get-FileHash -Algorithm SHA256 重新核验，与 evidence.md 一致：

| 相对路径 | SHA-256 |
|---|---|
| Assets/Scripts/framework/Library/ZeroFramework/Event/EventDispatcher.cs | 8F7D5D3938BCAD0F9ADBA74CF64075DFF5E4F36A9F71F3F3A2E9574FD5834AB4 |
| Assets/Scripts/framework/Library/ZeroFramework/Event/NotifyDispatcher.cs | 7714E1C948577F1ACF42C09EE5126952E37BDE8D1B83FEF1074AF43975483845 |
| Assets/Scripts/framework/Library/ZeroFramework/Manager/UniTaskManager.cs | 5BF99AB3B48EB9952413FF9ED583B6E049916EE4637E3526DCC46DDF8BEE3EB8 |
| Assets/Scripts/framework/FairyGUI/Scripts/Utils/Timers.cs | F7C21A275D5913C9A30D5C4E1A9B86EEFEC5F97F7E10FD67AA012FB83577FD7C |
| Assets/Scripts/framework/Library/ZeroFramework/Module/ModuleMgr.cs | C7E944D94D40D70DEAA4E55443C2158A852264E9F500D5D82EF1ACE71F7CCFCB |
| Assets/Scripts/game/Managers/TimelineMgr.cs | C85C4315AF5D13F761557BC57C97485596D7349747457B94737B71A9CF248C1F |
| Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel_CancellationToken.cs | F51C4006D387B2C488765D358FDB5107503A253F1D504A7282FD455B02B5BAEE |

## 4. 文档完整性和范围检查

最终核验结果：

1. 文件集检查 PASS：本目录只有 README.md、findings.md、evidence.md、candidates.md、open-questions.md、verification.md 六份文档，六份正文均非空。
2. 相对链接检查 PASS：本目录内的 Markdown 相对链接均可解析。
3. 证据编号检查 PASS：跨六份文档发现的 30 个 S04-E 编号均存在于 evidence.md，S04-E001 至 S04-E030 连续；7 个 S04-C 编号均存在于 candidates.md。
4. 图文一致性检查 PASS：Mermaid 图中的节点和文字调用链与 findings 中的行号证据一致：OLD Timeline 订阅闭环、CURRENT ResourceExt 异步/池链、CURRENT Procedure/FSM 关闭链；RootModule 节点标明 `!UNITY_EDITOR` 条件。
5. 关键源码路径检查 PASS：证据索引涉及的事件、通知、异步、计时器、FSM、内存池、资源回调、RootModule 和关闭链关键文件均存在。
6. 语义复核修订 PASS：重新回查 S04-E019、S04-E022、S04-E023、S04-E028～S04-E030，已把嵌套事件重入、待删除 handler 仍可能执行、资源回调异常/销毁等待、编辑器关闭条件、GameEvent.Shutdown 未接入证据和 FSM 清理异常路径写成静态事实或待验证项，没有标记为运行测试通过。
7. Git 范围检查 PASS：git diff --限定本目录没有输出，这是因为六份文件仍是未跟踪文件；git ls-files --others 和逐文件正文直读确认限定目录仅有上述六份。git status 全量输出中的既有 boot.config 删除、UserSettings 未跟踪和其他 slave 文档均保持原样。

## 5. 未运行的验证

- 未运行 Unity Editor、编译、打包、运行时场景、单元测试、性能测试。
- 未执行旧工程脚本、生成代码、导表、依赖升级或包安装。
- 因此本文不能把 EventDelegateData 异常后状态、嵌套事件/分发中移除语义、TimerModule 异常清理、System.Timer 回调线程、ResourceExtComponent 回调异常和销毁等待、Fsm.Create/DestroyFsm 池清理、GameEvent 生成结果或 UI pooled manager 复用风险写成“已运行复现”；这些均已在 open-questions.md 标记下一步验证。
