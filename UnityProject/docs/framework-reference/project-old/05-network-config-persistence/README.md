# Slave 05：网络、配置与持久化抽象

## 调查目标

本目录记录参考工程 ProjectOld 中网络、配置加载/访问、以及本地持久化的真实实现，并与当前 TEngine 工程做逐项对照。重点是回答：

- 网络传输、协议编解码、消息分发、请求/响应关联、超时、取消、重连分别由谁拥有；
- 配置是 CSV/资产/生成代码/反射中的哪一种组合，加载失败与版本不兼容如何表现；
- 持久化是否有统一接口、版本信息、原子性、事务或一致性保证；
- 哪些结论是代码确认，哪些只是调用链推断，哪些仍需主工程复核。

调查日期：2026-09-06（Asia/Shanghai）。

## 范围与边界

| 范围 | 本轮处理 | 明确不处理 |
| --- | --- | --- |
| 网络 | ProjectOld 的 TCP 风格长连接、线程 worker、协议 coder、消息监听、JSON 请求/响应、心跳、连接失败重试 | 不改造当前网络，不复制协议或业务消息 |
| 配置 | DAO、reader、decoder、ScriptableObject 配置资产、生成式配置类、缺失配置诊断 | 资源字节读取的通用细节；Luban 生成器内部实现由 06 负责 |
| 持久化 | PlayerPrefs 包装、JSON 文件、版本键、诊断文件、本地化插件的持久化适配器 | 不读取或复制用户数据、服务地址、token、真实业务配置内容 |
| 当前工程 | 只确认已有 HTTP、PlayerPrefs、文件/JSON、Luban 运行库与编辑器入口 | 不补写源码、asmdef、配置表或外部生成目录 |

只写入本目录的六份 Markdown 文件；未修改源码、Unity 配置、Package 配置、其他 slave 目录或既有并行变更。

## 基线

- OLD：D:/Work/SAUnity/ProjectOld；根目录未发现 Git/SVN 元数据，本次按只读参考工程处理。
- CURRENT：E:/MyWork/MyFramework/TEngine/UnityProject；Git 根为 E:/MyWork/MyFramework，分支为 framework，基线 HEAD 为 16afccb5df2a2efcb5003ecf9fbc0781c0170a。
- 两套工程的 Unity 版本均为 2022.3.62f2，revision 均为 7670c08855a9，见 S05-E001。
- 调查开始时工作区已有 ProjectSettings/boot.config 删除和多个 handoffs 未跟踪项；均未触碰。状态与范围记录见 verification.md。
- 关键源码 SHA-256 见 verification.md；哈希用于复核取证版本，不代表 OLD 有可用提交号。

## 两条真实调用链

### 网络链路

GameLaunch 注册 NetworkMgr → NetworkMgr 构造 Main 网络并选择 GameNetwork 或条件编译的 GameWebSocketNetwork → LoginModule.ConnLoginSer/ConnectGameServer 作为真实业务调用点调用 m_network.Connect → GameNetwork 创建 MsgCoder 与 NetworkBase → Connect 将命令送入 NetworkWorker → worker 建立 socket、按 MsgCoder 读写帧 → NetworkBase.Update 取回 worker 命令 → GameNetwork.OnRevMsg 进入 EventDispatcher 或 JSON listener。连接失败的有限重试在 GameNetwork.OnConnectHandler；服务端断开由 NetworkBase/NetworkMgr 通知，但没有等价的通用断线后请求恢复协议。

### 配置与持久化链路

ProjectOld 的代表配置链是 CompTroopShower.Init → TroopDao.Inst.GetCfg → BaseDao.GetDefault/GetByPath → LoadConfig → AssetReader 或 CsvReader → BaseCfgData.Data → ProcessData 建索引 → GetCfg。当前工程能确认的启动持久化链是 ProcedureLaunch.InitLanguageSettings → Utility.PlayerPrefs.HasSetting/GetString/SetString/Save；资源版本链是 ProcedureInitResources/ProcedureDownloadOver → Utility.PlayerPrefs 的 GAME_VERSION。当前工程的 Luban 菜单只指向外部生成脚本，工程内未找到与旧 BaseDao 等价的运行时表集合。

## 推荐阅读顺序

1. [findings.md](findings.md)：先看分层结论、两条调用链和 PLAN_CONFLICT。
2. [evidence.md](evidence.md)：按 S05-E 编号回到旧/当前源码。
3. [candidates.md](candidates.md)：看可迁移的设计候选，不把候选误读为已批准改动。
4. [open-questions.md](open-questions.md)：查看当前仍需要主工程确认的边界。
5. [verification.md](verification.md)：查看检查、哈希、未执行的构建类操作。

## 结论快照

1. ProjectOld 的网络核心是自研的 NetworkBase + NetworkWorker + IMsgCoder，GameNetwork 负责业务 facade 与监听分发；它不是一个把每次 Send 自动变成可等待 Future 的 RPC 层。
2. 只有 JsonNetwork 另建了 SessionId、队列、字典和超时扫描来实现请求/响应关联；其断线处理和 Dispose 对 pending callback 的语义并不完整。
3. ProjectOld 配置采用“生成/手写强类型类 + CSV 编辑器解码 + YooAsset/ScriptableObject 运行时资产”双路径；BaseDao 处理延迟初始化、路径回退和空表策略，但未看到通用 schema/version 兼容协议。
4. ProjectOld 持久化以 PlayerPrefs 和直接覆盖文件为主；有局部版本键，但没有统一原子提交、事务或崩溃恢复抽象。
5. 当前工程已有 HTTP 超时/取消、PlayerPrefs 用户键和本地化文件适配器；在游戏运行时代码范围内，没有被代码确认的长连接网络层、消息协议层、通用 RPC pending registry 或运行时 Luban 表访问层。Packages/MCPForUnity/Editor 下的 WebSocket 适配器受 Editor-only 程序集约束，不是游戏运行时等价物。因此不能把旧工程直接当成当前工程的现状。
