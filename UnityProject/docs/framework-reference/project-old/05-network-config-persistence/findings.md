# 深度发现：网络、配置与持久化

## 1. 参与者分类

| 参与者 | ProjectOld 归类 | 当前工程归类 | 备注 |
| --- | --- | --- | --- |
| NetworkBase、NetworkWorker、IGameNetwork、IMsgCoder | 自研框架抽象/传输适配 | 游戏运行时代码未发现等价长连接层；Editor-only MCP WebSocket 不计入 | worker 与主线程通过 actor command 交互 |
| GameNetwork、NetworkMgr、Heartbeater、JsonNetwork | 自研业务 facade/业务适配 | Utility.Http 目前只能确认是通用 HTTP helper 定义，未确认有游戏业务调用 | 旧工程将通用网络、游戏消息和 JSON RPC 分开 |
| Google.Protobuf 生成消息、MsgTypeProvider、各类 Config 映射类 | 第三方运行库 + 生成代码 | LubanLib/ByteBuf 运行库与编辑器入口 | 生成物本身不等于当前工程已接通的运行时系统 |
| BaseDao、BaseCfgDecoder、AssetReader/CsvReader | 自研配置基础设施 | 当前未找到等价运行时表访问 | 旧工程的 AssetReader 在运行时直接取 BaseCfgData 资产 |
| Prefs、DataStorageHelper、ConfigMissingCollector | 自研/业务持久化包装 | Utility.PlayerPrefs 是自研包装 | 都建立在 Unity PlayerPrefs 或普通文件之上 |
| PersistentStorage | 旧工程/当前工程均带有外部本地化模块适配 | 当前归属 Localization 模块 | 不是游戏存档的完整一致性层 |

## 2. 网络分层与真实链路

### 2.1 传输层由谁拥有

ProjectOld 的 IGameNetwork 暴露连接状态、Connect、Send、Dispatch、Update、按类型/整数注册 listener、超时设置和事件代理；NetworkBase 保存 coder、worker、actor、session 与主线程事件。NetworkBase.Connect 在主线程侧建立 NetworkWorker 和 ThreadActor，然后发送 C2S_Connect；真正的 DNS、socket 连接、非阻塞切换、读写发生在 NetworkWorker.DoConnect/DoSendMsg/DoRevMsg。

worker 的接收流程是“先读固定头长度，再由 coder 算完整帧长，再 Decode，再把对象和 serial 放回主线程”。发送错误以本地 sendId 反馈；该 sendId 只对一次发送操作的错误有用，不是服务端响应的 requestId。

生命周期的责任分散如下：

- NetworkWorker.OnExit 在 worker 退出时关闭 socket、释放 reader/writer，并在已连接状态发送 S2C_Disconn。
- NetworkBase.DoCmds 把服务端关闭、worker 停止、输出队列满、S2C_Disconn 等状态转成 OnDisconnect。
- NetworkMgr.Update 负责网络可达性检查与每帧 Update；不可达时主动 CloseAll。
- GameNetwork.OnConnectHandler 只在 ConnectResult 失败时按 RetryVo 重新 Connect，并递增 timeout；OnDisconnectHandler 本身只记质量日志并转发断线事件。

因此“底层传输生命周期”与“业务重连策略”有清晰边界，但重连策略只覆盖连接失败路径。代码没有显示一个通用的断线后自动重连状态机，也没有显示它如何恢复/重放所有 pending request。

### 2.2 协议与消息分发

MsgCoder 实现 IMsgCoder：

1. 以 2 字节长度头确定需要继续读取的帧；
2. 从帧中读取消息类型与服务端 serial；
3. 使用 MsgMap 的 id/type 表解析 Google.Protobuf 消息；
4. 支持 BigPack/Zip/MessageMerger 相关处理；
5. Decode 后由 GameNetwork 按 IMessage 类型进入 EventDispatcher，非 IMessage 的 UndefinedMsg 走事件名 fallback。

MsgMap 的静态初始化通过 MsgTypeProvider.Get 建立映射；编辑器下的 GetMessageRequestId 只是按消息 id/name 做请求-回复辅助映射，不能证明运行时存在 request/response pending 关联。另一个 IM 链路的 ProtoType.Init 会扫描程序集并按枚举名映射 protobuf 类型，这是 IM 的协议适配，不是主游戏网络的通用 RPC 机制。

### 2.3 请求/响应关联、超时、取消与断线

主游戏的普通 IMessage 路径没有发现通用 pending 表：

- IGameNetwork.Send 返回 int；
- NetworkBase 生成 m_genSendId，并仅在 OnSendErr 中带回该 id；
- GameNetwork.OnRevMsg 只按消息类型/serial 分发；
- 搜索未发现普通消息路径使用 CancellationToken、TaskCompletionSource 或 request context。

JsonNetwork 是明确的例外。它为 MsgCL2GSGeneralRequest 分配递增 SessionId，创建 ReqContext，同时放入 FIFO m_reqList 与 m_reqMap。GeneralReplyHandler 按 SessionId 从 map 取 context，并将 JsonReply 回调标记为完成；Update 每秒扫描队首，超时则以 IsTimeout=true 回调。它把 JSON RPC 的响应关联隔离在 GameNetwork 之上，这是旧工程最接近“请求抽象”的实现。

但 JsonNetwork 仍有三个边界：

1. 没有取消 API，也没有 CancellationToken；调用方只能等待回调或超时。
2. GameNetwork.OnDisconnectHandler 不会清理或失败完成 JsonNetwork 的 pending map；只要 Update 继续运行，通常会等到超时；如果 JsonNetwork 被 Dispose，OnDispose 直接清空队列和池，pending callback 不会收到断线结果。
3. 普通 binary IMessage 与 JsonNetwork 是两套语义，不能把 JSON 的 SessionId 推广成旧框架已经具备的全局 request correlation。

心跳 Heartbeater 监听一个具体回复类型、计算 ping/丢包、在 connect/disconnect 时重置；它是 liveness/quality 机制，不是任意业务请求的响应关联器。IM 的 OvertimeMonitor 和 LoginController 也属于业务层超时：控制器以回调注册/停止，超时后关闭 IM 网络，而不是由底层消息层完成请求。

### 2.4 网络调用图

    GameLaunch
      -> MgrRegisterCommander(NetworkMgr.Inst)
      -> NetworkMgr.Main = AddNetwork(...)
      -> GameNetwork
           -> NetworkBase
                -> ThreadActor + NetworkWorker
                     -> socket/DNS/read/write
                     -> IMsgCoder (MsgCoder)
           -> EventDispatcher / Heartbeater / JsonNetwork
      -> NetworkMgr.Update
           -> GameNetwork.Update
                -> NetworkBase.Update
                -> JsonNetwork.Update

这个图只表达代码已确认的结构；它不表示所有业务登录、服务地址或服务器行为。

## 3. 配置加载与访问

### 3.1 ProjectOld 的接口与两条读取路径

旧配置接口分成三层：

- IConfig<TKey> 只要求 GetKey；
- ICfgReader 负责路径、存在性、行列读取；
- ICfgDecoder 负责 DecodeByPath/Decode、Data、清理和全表后处理。

BaseDao 负责每个 typed DAO 的实例管理、路径到 DAO 的 map、Key 到 config 的 map、默认 DAO、重载和清理。LoadConfig 根据编译条件和 FrameworkConfig.Inst.UseYooAsset 选择：

- CsvReader：调用 decoder.DecodeByPath，得到 BaseCfgData<TConfig>.Data；
- AssetReader：通过 LoadMgr.Inst.LoadAsset<BaseCfgData<TConfig>>(path) 取已经生成/导入的 ScriptableObject 数据，decoder 参数不参与运行时解码。

这意味着旧工程不是单一路径：编辑器/Windows 可以走 CSV，资源运行路径通常依赖配置资产；同一个 DAO 的访问接口不暴露底层格式。

### 3.2 强类型、生成代码与手工扩展

XBattleStageConfig 是一个典型强类型映射类：字段、GetKey、XBattleStageDao、XBattleStageDecoder 和按列名读取的 ProcessRow 按固定骨架分工；XBattleStageConfig_Extend.cs 再用 partial class 添加业务索引、GetName、ProcessRowExt、AfterProcess 和全局后处理。文件分工强烈表明它是生成式骨架加手工扩展，但本次源码中没有保留具体生成命令，因此“由什么工具生成”仍是待复核项；这也不等于运行时通过反射读取任意 schema。

StringConfig/StringDao 还展示了手工业务规则：多语言路径由 ConfigMgr.Language 决定，ProcessData 会合并 StringFixDao 数据，缺失 key 交给 ConfigMissingCollector。配置数据本身仍是强类型对象，业务扩展在 DAO/partial 层。

### 3.3 加载失败、延迟初始化与版本不兼容

BaseDao 在 LoadMgr 不可用时记录 m_retryLoad=true，并在 GetCfg/GetCfgs 时尝试 Reload；找不到路径返回 null，之后非 AllowEmpty 会记录错误并以空数组继续；reader 还区分 DecodeNull、DataNull、LoadMgrNoInit 等结果。这个机制解决了“资源系统尚未 ready”的时序问题，但没有把错误统一成可供上层区分的异常/状态对象。

在所查 BaseDao、decoder 和 generated config 路径中，没有发现通用的 schema version、字段兼容、迁移或资产版本比较。旧工程的资源版本/patch 系统可能在别处负责资产选择，但这不是本轮配置 DAO 已证实的能力；主工程需要另行决定新系统的 schema hash、配置包版本和旧存档兼容策略。

## 4. 持久化抽象与一致性

### 4.1 ProjectOld

Prefs<T> 是带缓存的 typed PlayerPrefs wrapper：Value 首次读取后缓存；IGroupKey 变化会重置读写标记；JsonPrefs<T> 用 Unity JSON 包装复杂对象；底层 accessor 仍然是 PlayerPrefs.Get/Set。它降低重复读取并支持分组 key，但没有在 accessor 层提供事务或原子批量提交。

DataStorageHelper 是业务级混合入口：

- LoadData/SaveData 将 int/float/string 直接映射到 PlayerPrefs，其他类型序列化为 JSON；
- SaveData 可写 key_version，并可选择立即 PlayerPrefs.Save；
- LoadData 不会自动按版本键拒绝或迁移数据，版本比较由 IsLowVersionByKey 单独调用；
- LoadDataFromFile/SaveDataToFile 使用 persistentDataPath 下的 JSON 文本文件和 FileUtils 直接覆盖写入。

FileHelper.SaveBytesToFile 使用 FileMode.Create 后直接写目标文件；ConfigMissingCollector 以 File.WriteAllLines 直接覆盖诊断文件，每次新增缺失 key/message id 都可能重写整个集合。所查路径未实现临时文件、fsync、rename、双写校验、锁或崩溃恢复。

### 4.2 当前 TEngine

当前已有三个不等价的能力：

1. Utility.PlayerPrefs 提供全局开关、普通 typed get/set、可选用户前缀和 Set 时默认 Save；它没有版本字段、批量事务、JSON 对象 API 或跨 backend 接口。
2. Localization.PersistentStorage 是本地化模块的 I2 适配器：PlayerPrefs 大字符串分片，文件接口按 Persistent/Temporal/Streaming 选择路径，并直接 File.WriteAllText/File.ReadAllText；它没有原子写与目录创建的统一契约。
3. Utility.File 目前提供创建文本文件、路径/大小等辅助，不是完整的 Save/Load persistence interface；Utility.Json 只提供可替换的序列化 helper，不负责存储。

ProcedureLaunch 真实使用 Utility.PlayerPrefs 读取语言/音量设置，ProcedureInitResources 与 ProcedureDownloadOver 读写 GAME_VERSION。当前代码未把这些设置组合成版本化存档或事务。

## 5. 当前工程对照与 PLAN_CONFLICT

在当前工程的游戏运行时代码范围内，可确认 Utility.Http.Get/Post/SendWebRequest 是 helper 定义，但本轮未找到 Utility.Http 的游戏业务调用点，因此不能把它称为已确认的网络业务入口。该 helper 每次调用创建 UnityWebRequest 与 CancellationTokenSource，使用 UniTask cancellation/timeout；SendWebRequest 的正常返回路径直接取 downloadHandler.text，未见按 UnityWebRequest.result 做统一错误分类，非取消失败如何向上层表达仍需补齐。没有发现 ProjectOld 的 NetworkBase、socket worker、二进制 IMessage coder、长连接状态或 request-id map 的等价实现。当前 GameApp 入口也只展示热更逻辑与 UI 启动，未提供网络初始化链。Packages/MCPForUnity/Editor/Services/Transport/WebSocketTransportClient.cs 虽定义了 Editor 工具使用的 WebSocket 传输，但其程序集由 MCPForUnity.Editor.asmdef 限定为 Editor；它不是游戏运行时网络实现，也不改变上述缺口范围。

当前工程内可确认的配置入口是 LubanTools 菜单，它把生成脚本路径指向当前 UnityProject 外部的 Configs/GameConfig；工程内 GameProto 仅有 LubanLib/ByteBuf 等运行库，Assets/AssetRaw/Configs 只有占位说明文件，未找到 ConfigSystem、Tables、生成表 DAO 或运行时表加载调用。ByteBuf 不能单独证明配置系统已接通。

## PLAN_CONFLICT

这是一个影响主工程设计评审的真实缺口：在当前工程游戏运行时代码范围内，没有代码已证实的游戏长连接/协议/RPC 层，也没有代码已证实的运行时 Luban 配置访问层。Editor-only 的 MCP WebSocket 不能填补游戏网络缺口。因而本轮不能给出“旧工程抽象可直接映射到当前实现”的结论；候选方案只能作为待实现边界和验收契约，不能按迁移任务执行。主工程需要先确认网络协议/传输归属、Luban 生成物实际来源，以及配置包版本策略，再决定是否实现 05 的候选。
