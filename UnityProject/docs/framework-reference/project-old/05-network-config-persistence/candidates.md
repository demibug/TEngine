# 设计候选（非实施计划）

候选只用于主工程评审，不表示本轮已批准或已实现。每项都同时写明旧证据、当前等价物、成本与依赖。

## S05-C001：分层网络骨架

- 旧证据：IGameNetwork → NetworkBase → NetworkWorker/IMsgCoder → GameNetwork → NetworkMgr；见 S05-E002 至 S05-E005。
- 当前等价物：Utility.Http 目前只能确认是请求级 helper 定义，未确认有游戏业务调用；游戏运行时代码没有长连接 transport、消息 codec 或统一 dispatcher。Editor-only MCP WebSocket 适配器不属于当前游戏网络等价物。
- 建议：若当前游戏需要长连接，保留“transport 生命周期、协议 codec、typed facade、event dispatcher”四层边界；将 socket/WebSocket 作为 transport 适配，不让业务模块直接持有 socket。
- 收益：连接状态、帧错误、平台 transport、业务分发可分别测试；心跳与业务请求不会绑死在 codec。
- 成本：需要主线程/后台线程边界、关闭时序、错误分类、平台兼容和可观测性；当前没有现成实现可直接复用。
- 约束：不能把旧 GameNetwork 的业务消息类型或服务配置直接搬入；必须先确定新协议与运行平台。
- 依赖：协议定义、目标平台 transport 选择、启动/资源流程的网络所有权。
- 优先级：P0（只有在确认需要长连接网络后才进入实现）。

## S05-C002：独立 RPC pending registry

- 旧证据：JsonNetwork 的 SessionId、m_reqList、m_reqMap、超时扫描与 JsonReply；见 S05-E006；普通 IMessage 没有同等能力，见 S05-E007。
- 当前等价物：Utility.Http 每次调用有内部 CTS/timeout，但没有 caller token、request id、断线语义或统一错误结果。
- 建议：在 typed message facade 之上独立提供 RequestId、Pending、Timeout、Cancel、Complete、FailAllOnDisconnect 六个明确动作；传输层只返回 send/receive/error。
- 收益：可以明确回答“服务端回复、超时、主动取消、连接代际变化、断线”谁完成 callback/UniTask；避免把 sendId 当 response id。
- 成本：需要一次性完成保证、late reply 丢弃、连接 generation、回调线程、取消与重试策略；代码量和测试矩阵高于旧 JsonNetwork。
- 约束：是否可重试必须按请求幂等性定义；断线后不能默认重放所有请求。
- 依赖：协议中真实存在的 request id/response id 或客户端生成的 correlation 字段；UniTask/取消策略。
- 优先级：P0（若需要业务请求/响应）；P2（纯推送协议可暂缓）。

## S05-C003：生成配置 + 运行时资产访问契约

- 旧证据：BaseDao 的 AssetReader/CsvReader、BaseCfgData、强类型 config/DAO/decoder；见 S05-E009 至 S05-E011。
- 当前等价物：LubanTools 菜单、LubanLib/ByteBuf、GameProto asmdef；未找到 ConfigSystem/Tables/生成 DAO，见 S05-E010、S05-E016。
- 建议：延续 Luban 的“生成强类型代码 + 二进制/资产运行时加载”方向；为运行时只暴露 typed Tables/DAO，编辑器 CSV/生成器不进入热路径。05 只定义 loader、状态、错误和版本契约，生成器细节交给 06。
- 收益：编译期字段类型、运行时读取低反射、资源系统可替换；与当前工程文档方向一致。
- 成本：需要明确生成输出目录、程序集依赖、资产地址、懒加载缓存和热更新清理。
- 约束：不能用当前存在的 ByteBuf 推断表访问已经存在；外部 Configs/GameConfig 必须被纳入可复核流程。
- 依赖：Luban schema/生成输出、YooAsset 资产地址规则、配置包版本/schema hash、热更程序集装载顺序。
- 优先级：P0（当前存在 PLAN_CONFLICT）。

## S05-C004：分层且可恢复的本地持久化接口

- 旧证据：旧 Prefs/DataStorageHelper/FileHelper 直接写 PlayerPrefs/文件，版本键与文件写入均是局部能力；当前 Utility.PlayerPrefs 与 Localization.PersistentStorage 同样是 helper/adapter，见 S05-E012 至 S05-E015。
- 当前等价物：Utility.PlayerPrefs 适合小型设置；PersistentStorage 适合本地化文件；没有统一 SaveResult/Version/AtomicCommit 接口。
- 建议：先定义小设置、诊断数据、可恢复文件三种 scope；每种 scope 选 backend，并统一 namespace、schema version、读失败策略。对可恢复文件采用 temp + replace 或备份策略；PlayerPrefs 只承担小型设置，不承担大对象存档。
- 收益：版本不兼容、损坏、清理和用户隔离能够被测试；避免业务散落直接调用 UnityEngine.PlayerPrefs/File。
- 成本：需要迁移旧 key、目录策略、平台差异、崩溃恢复测试和数据清理策略；原子替换在不同平台需验证。
- 约束：不能宣称 Unity PlayerPrefs.Save 是跨键事务；多键数据必须设计提交标记或单文件快照。
- 依赖：存档/设置数据模型、版本迁移策略、目标平台文件语义。
- 优先级：P1（设置可先包一层，存档/诊断按风险分级）。
