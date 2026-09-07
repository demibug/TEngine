# 证据索引

约定：

- OLD 路径相对 D:/Work/SAUnity/ProjectOld；
- CURRENT 路径相对 E:/MyWork/MyFramework/TEngine/UnityProject；
- “代码确认”表示在源码、编译条件或明确调用点看到；“调用链推断”表示由多个代码点拼接出的合理路径；“待复核”表示不能仅凭当前扫描闭环；
- 路径、类/方法、行号和结论均以 2026-09-06 调查快照为准。

## S05-E001：Unity 与仓库基线

- OLD：ProjectSettings/ProjectVersion.txt:1-2，m_EditorVersion 与 revision 均为 2022.3.62f2 / 7670c08855a9；根目录未发现 Git/SVN 元数据。
- CURRENT：ProjectSettings/ProjectVersion.txt:1-2，Unity 版本与 OLD 相同；Git 根为 E:/MyWork/MyFramework，分支 framework，HEAD 为 16afccb5df2a2efcb5003ecf9fbc0781c0170a。
- 精确结论：版本可比，但 OLD 没有可引用的 VCS 提交号；源码哈希另见 verification.md。
- 证据性质：代码确认/仓库状态确认。

## S05-E002：旧网络接口与主线程 facade

- OLD：Assets/Scripts/framework/Library/ZeroFramework/Network/IGameNetwork.cs:15-147 的接口包含 Connect、Send、Dispatch、Update、listener、timeout 与事件代理；NetworkBase.cs:33-72、186-275 持有 callback、IMsgCoder、NetworkWorker，并生成发送 id。
- CURRENT：Assets/TEngine/Runtime/Core/Utility/Utility.Http.cs:17-114 只有静态 HTTP helper；没有对应 IGameNetwork/NetworkBase 类型。
- 精确结论：旧工程将网络生命周期和消息监听抽象在 IGameNetwork，当前工程目前只有请求级 HTTP helper。
- 证据性质：代码确认。

## S05-E003：旧 socket worker、帧读写与断开

- OLD：Assets/Scripts/framework/Library/ZeroFramework/Network/NetworkWroker.cs:24-125、179-216、222-342、346-397、399-527；worker 自己管理 socket、连接、发送错误、长度头、解码、超时和 S2C_Disconn。NetworkBase.cs:388-560 把 worker 命令转换成连接/断开/收发事件。
- CURRENT：Assets/TEngine/Runtime/Core/Utility/Utility.Http.cs:25-114 使用 UnityWebRequest，无 socket worker、帧协议或长连接断开事件。
- 精确结论：旧工程的传输层是独立线程 worker + actor command；当前工程未找到等价长连接传输实现。
- 证据性质：代码确认。

## S05-E004：旧工程的真实网络入口与重连边界

- OLD：Assets/Scripts/game/Module/Launch/GameLaunch.cs:351、411、493 注册/使用 NetworkMgr；Assets/Scripts/game/Managers/Network/NetworkMgr.cs:109-164 创建 Main、选择 GameNetwork/GameWebSocketNetwork、每帧 Update 并在不可达时 Close；Assets/Scripts/game/Module/Login/LoginModule.cs:1162、1327、1727 的 ConnLoginSer/ConnectGameServer 调用 m_network.Connect；Assets/Scripts/game/Managers/Network/GameNetwork.cs:189-212、481-506 处理 Connect 和连接失败 RetryVo。
- CURRENT：Assets/GameScripts/HotFix/GameLogic/GameApp.cs:20-38 只初始化热更事件、生命周期监听和 UI 逻辑；当前工程未发现对应 NetworkMgr 初始化调用。
- 精确结论：旧工程有可追踪的启动→管理器→网络路径；其重试代码位于连接失败回调，不是已证明的断线自动恢复机制。
- 证据性质：OLD 为调用链推断加代码确认；CURRENT 为代码检索结果，待主工程确认是否存在外部/未纳入扫描的网络模块。

## S05-E005：旧协议 coder 与 type mapping

- OLD：Assets/Scripts/game/Managers/Network/MsgCoder.cs:18-107、156-218 实现 IMsgCoder，读取 2 字节长度、消息类型、serial 并按 MsgMap 解码；Assets/Scripts/game/Managers/Network/MsgMap.cs:8-76 建立 MsgTypeProvider 映射，编辑器辅助方法不构成运行时 request correlation。
- CURRENT：Assets/GameScripts/HotFix/GameProto/LubanLib/ByteBuf.cs:41-58、1471-1483 是 Luban 字节缓冲区；Assets/GameScripts/HotFix/GameProto/GameProto.asmdef:1-25 仅能确认 GameProto 程序集与 LubanLib 文件存在。
- 精确结论：OLD 有游戏消息协议编码/分发，CURRENT 的 ByteBuf 只证明字节缓冲能力，不能证明网络协议或表访问已接通。
- 证据性质：代码确认。

## S05-E006：JsonNetwork 的请求/响应关联

- OLD：Assets/Scripts/game/Managers/Network/JsonNetwork.cs:56-80 初始化 g_requestId、m_reqList、m_reqMap；:102-143 分配 SessionId 并登记 ReqContext；:147-180 以超时扫描处理 callback；:182-232 按 SessionId 回调并回收；:234-240 Dispose 直接清空队列/池并置空 network。
- CURRENT：Assets/TEngine/Runtime/Core/Utility/Utility.Http.cs:25-114 每次请求只有本地 CTS，没有业务 request id、统一 pending map 或断线失败结果。
- 精确结论：OLD 只有 JSON 子层拥有显式 correlation；断线不主动完成 pending callback，Dispose 会丢弃 pending callback；CURRENT 没有等价 RPC 层。
- 证据性质：代码确认。

## S05-E007：普通 binary 消息不是 pending RPC

- OLD：Assets/Scripts/game/Managers/Network/GameNetwork.cs:47、215-260、267-295、397-474；普通 Send 返回 network send result，OnSendErr 只处理 sendId，OnRevMsg 按类型/serial 分发；:475-506 的断开与连接回调没有通用 pending registry。
- CURRENT：未找到普通 binary message、IMessage、MsgCoder 或 response registry；可见 HTTP 代码仍局限于 Utility.Http.cs:17-114。
- 精确结论：不能把旧 m_genSendId、心跳或 MsgMap.GetMessageRequestId 解释成一个运行时请求完成机制。
- 证据性质：OLD 为代码确认；CURRENT 为源码检索结果。

## S05-E008：心跳与 IM 业务超时的边界

- OLD：Assets/Scripts/game/Managers/Network/Heartbeater.cs:20-121、130-160 监听专用心跳回复并计算 ping/丢包；Assets/Scripts/im/Gather/Network/ProtoType.cs:18-53 反射扫描程序集建立 IM 类型表；Assets/Scripts/im/Gather/Tools/Overtime/OvertimeMonitor.cs:20-137 管理业务回调超时；Assets/Scripts/im/Gather/Controller/LoginController.cs:24-159 以消息 listener 和 OvertimeMonitor 驱动登录/查询超时。
- CURRENT：Assets/TEngine/Runtime/Core/Utility/Utility.Http.cs:25-114 的 timeout 仅属于 HTTP 请求。
- 精确结论：旧心跳、IM 业务超时、JSON RPC timeout 是三种不同机制；不能合并成一个“网络超时层”。
- 证据性质：代码确认。

## S05-E009：旧配置接口与数据容器

- OLD：Assets/Scripts/framework/Library/ZeroFramework/Config/IConfig.cs:9-12 只定义 key；ICfgReader.cs:11-42 定义 CSV/行列 reader；ICfgDecoder.cs:6-26 定义 decode/data；BaseCfgData.cs:10-15 以 ScriptableObject 保存 TConfig[]。
- CURRENT：Assets/GameScripts/HotFix/GameProto/GameProto.asmdef:1-25 仅能确认 GameProto 程序集；Assets/GameScripts/HotFix/GameProto/LubanLib/ByteBuf.cs:41-58、1471-1483 仅为 Luban 字节缓冲基础类；在 Assets/GameScripts/HotFix 与 Assets/TEngine/Runtime 的游戏运行时代码范围内未找到 IConfig/ICfgReader/ICfgDecoder/BaseCfgData 等价运行时接口。
- 精确结论：旧配置访问契约与数据容器完整存在，当前工程的 Luban 运行库尚未形成同等 DAO/reader/decoder API。
- 证据性质：代码确认。

## S05-E010：旧配置加载、路径、失败与重试

- OLD：Assets/Scripts/framework/Library/ZeroFramework/Config/BaseDao.cs:151-271、296-404、426-494；LoadConfig 按编译条件选择 CsvReader/AssetReader，LoadMgr 未就绪时置 m_retryLoad，缺失路径返回 null，ReadConfigErr 区分 DecodeNull/DataNull/LoadMgrNoInit；GetCfg/GetCfgs 触发 Reload。
- CURRENT：Assets/TEngine/Editor/LubanTools/LubanTools.cs:6-17 只调用外部 Configs/GameConfig 生成脚本；Assets/AssetRaw/Configs/about.txt 只有占位说明，当前工程未找到运行时 BaseDao/ConfigSystem/Tables。
- 精确结论：OLD 有“资源系统 ready 后重试”的配置生命周期；CURRENT 的生成入口依赖工程外部路径，运行时表访问尚未被代码确认。
- 证据性质：OLD 代码确认；CURRENT 代码确认加检索结果；外部生成目录待复核。

## S05-E011：强类型/生成式配置与业务扩展

- OLD：Assets/Scripts/game/Data/Config/XBattleStage/XBattleStageConfig.cs:13-15、179-229、214-243 具备字段、GetKey、typed Dao、decoder 和按列名 ProcessRow；XBattleStageConfig_Extend.cs:7-113、171-257 用 partial/override 添加 GetName、索引和后处理；StringConfig.cs:44-104、238-263 展示多语言 ProcessData 与 decoder。
- CURRENT：未发现已导入的生成表类/DAO；Assets/TEngine/Editor/LubanTools/LubanTools.cs:12-14 的脚本路径不能替代生成物。
- 精确结论：旧工程以强类型代码和手写 partial 扩展为主，文件分工呈现生成式骨架，但具体生成命令未在本轮源码中闭环；未见配置运行时依赖任意 schema 反射。当前仅能确认 Luban 工具意图。
- 证据性质：代码确认（类/文件分工）；生成来源为调用链推断，待复核。

## S05-E012：旧 PlayerPrefs、文件与版本键

- OLD：Assets/Scripts/framework/Library/ZeroFramework/Utils/Prefs.cs:13-99、169-259、383-441 支持缓存、分组 key、JSON wrapper 和 typed accessor；Assets/Scripts/game/Helper/DataStorageHelper.cs:114-234、908-951 直接读写 JSON 文件/PlayerPrefs、写入 key_version、另行比较版本；FileHelper.cs:292-312 以 FileMode.Create 覆盖写目标文件。
- CURRENT：Assets/TEngine/Runtime/Core/Utility/Utility.PlayerPrefs.cs:12-145、283-286 提供开关、typed set、用户 key、Save，但没有版本或事务字段。
- 精确结论：旧工程有局部版本标记但没有统一迁移/原子提交；当前 PlayerPrefs 包装更小，也没有版本一致性语义。
- 证据性质：代码确认。

## S05-E013：旧配置缺失诊断是独立持久化

- OLD：Assets/Scripts/framework/Library/ZeroFramework/Config/ConfigMissingCollector.cs:13-79、103-116、158-249 使用 persistentDataPath 下的诊断文件，记录缺失字符串 key/message id，并以 File.WriteAllLines 覆盖保存。
- CURRENT：Assets/TEngine/Runtime/Core/Utility/Utility.PlayerPrefs.cs:12-45、112-145 提供 PlayerPrefs 包装；Assets/TEngine/Runtime/Module/LocalizationModule/Core/Configurables/PersistentStorage.cs:9-65、185-267 提供本地化持久化适配；未找到等价 ConfigMissingCollector，因此这些类不表示配置缺失诊断已接通。
- 精确结论：旧工程的诊断持久化不应被误认成游戏存档或配置加载本身；其写入也未提供事务/原子替换。
- 证据性质：代码确认。

## S05-E014：当前 PlayerPrefs 的真实业务调用

- OLD：Assets/Scripts/game/Helper/DataStorageHelper.cs:197-234 由业务 helper 选择立即保存或延后保存。
- CURRENT：Assets/GameScripts/Procedure/ProcedureLaunch.cs:48-91 读取语言/音量键并在语言纠正后 Save；ProcedureInitResources.cs:77-104、130-145 与 ProcedureDownloadOver.cs:12-22 读写 GAME_VERSION。
- 精确结论：当前确实存在设置与资源版本的 PlayerPrefs 使用路径，但它们是零散业务调用，不是统一持久化接口或存档事务。
- 证据性质：代码确认。

## S05-E015：当前本地文件/JSON/HTTP 适配器

- OLD：Assets/Scripts/framework/Library/ZeroFramework/Utils/FileHelper.cs:292-365、Assets/Scripts/game/Helper/DataStorageHelper.cs:114-130 直接 UTF-8 覆盖写/读文件；Assets/Scripts/game/Managers/Network/JsonNetwork.cs:252-299 仅负责 JSON reply 数据视图。
- CURRENT：Assets/TEngine/Runtime/Module/LocalizationModule/Core/Configurables/PersistentStorage.cs:9-65、85-161、185-267 提供本地化模块的 PlayerPrefs/file adapter；Utility.Json.IJsonHelper.cs:7-41 与 DefaultJsonHelper.cs:9-45 只负责序列化；Assets/TEngine/Runtime/Core/Utility/Utility.Http.cs:25-116 定义 HTTP helper，正常返回路径直接取 downloadHandler.text，未见按 UnityWebRequest.result 做统一错误分类；本轮在游戏运行时代码中未找到 Utility.Http 的业务调用点。
- 精确结论：当前已经有三个分离的 helper/adapter，但没有一个跨 HTTP、配置、文件、PlayerPrefs 的统一持久化抽象；PersistentStorage 直接 WriteAllText，也未定义原子写。Utility.Http 目前是 helper 定义，不应被当成已闭环的网络业务入口。
- 证据性质：代码确认。

## S05-E016：当前配置/网络缺口的可复核快照

- OLD：Assets/Scripts/framework/Library/ZeroFramework/Network/NetworkBase.cs:33-72、186-275；Assets/Scripts/game/Managers/Network/GameNetwork.cs:189-212、481-506；Assets/Scripts/game/Managers/Network/JsonNetwork.cs:56-240；Assets/Scripts/framework/Library/ZeroFramework/Config/BaseDao.cs:151-271、296-404；这些关键文件均存在且哈希记录在 verification.md。
- CURRENT：Assets/TEngine/Runtime/Core/Utility/Utility.Http.cs:17-116；Assets/TEngine/Runtime/Core/Utility/Utility.PlayerPrefs.cs:12-145；Assets/TEngine/Runtime/Module/LocalizationModule/Core/Configurables/PersistentStorage.cs:9-65、185-267；Assets/TEngine/Editor/LubanTools/LubanTools.cs:6-17；Assets/GameScripts/HotFix/GameProto/GameProto.asmdef:1-25、LubanLib/ByteBuf.cs:41-58、1471-1483。检索范围为 Assets/GameScripts/HotFix 与 Assets/TEngine/Runtime 的游戏运行时代码，未找到长连接网络类或运行时表集合；Packages/MCPForUnity/Editor/Services/Transport/WebSocketTransportClient.cs:25-48 虽存在 WebSocket 适配器，但 Packages/MCPForUnity/Editor/MCPForUnity.Editor.asmdef:1-10 将其限定为 Editor-only，不计入游戏运行时实现。
- 精确结论：PLAN_CONFLICT 基于带明确路径/行号的当前源码快照缺口，而不是把项目文档中“支持某能力”的描述当作实现证据；缺口范围特指游戏运行时，Editor-only MCP 工具不构成反例。
- 证据性质：代码检索结果/哈希可复核；外部生成目录与未纳入工作区的模块仍待复核。
