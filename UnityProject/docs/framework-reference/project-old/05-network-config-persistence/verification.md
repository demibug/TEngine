# 调查与验证记录

## 已执行的检查

- 读取并遵守当前工程 AGENT.md/CLAUDE.md；按项目要求使用 TEngine/Luban 领域说明，未把说明文档替代为代码证据。
- 检查 OLD 与 CURRENT 的 ProjectVersion.txt；两者均为 Unity 2022.3.62f2，revision 均为 7670c08855a9。
- 检查 CURRENT Git：根目录 E:/MyWork/MyFramework，分支 framework，HEAD 16afccb5df2a2efcb5003ecf9fbc0781c0170a。
- 调查前记录工作区已有删除/未跟踪项；本轮只新建 docs/framework-reference/project-old/05-network-config-persistence 下的六个 Markdown 文件。
- 通过 rg/文件清单追踪旧网络、配置、持久化类以及当前 HTTP、PlayerPrefs、PersistentStorage、LubanTools、ByteBuf。
- 复核当前网络缺口时将范围限定为游戏运行时代码；单独检查 Packages/MCPForUnity/Editor 的 WebSocket 适配器及 Editor-only asmdef，确认其不属于游戏运行时网络实现。
- 检查文档目录中的六个文件、S05-E001 至 S05-E016 证据编号、S05-C001 至 S05-C004 候选编号与本目录相对链接。
- 未运行 Unity、编译、构建、转表、代码生成、资源更新、网络请求或旧工程写操作。

## 关键源码 SHA-256

哈希是调查时读取的文件快照，路径相对各自工程根目录。

| 工程 | 相对路径 | SHA-256 |
| --- | --- | --- |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Network/NetworkBase.cs | 20675A600E0B0567476104488C0F6494C46CCC3E0925BFD8437C814BAF758A5C |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Network/NetworkWroker.cs | B1CE2616C5BE37F36C9DF6D1974170FA2C4306DE72C83CC65C5F731A801B4125 |
| OLD | Assets/Scripts/game/Managers/Network/GameNetwork.cs | B87BC0332835760D647A61BCD187CEB330672980DC6C46AEF124F17439A5A7A8 |
| OLD | Assets/Scripts/game/Managers/Network/JsonNetwork.cs | 6406588BC64890EF23D55A9438BA858A6C7273E179F4FE2DA3F5741D20F4BB03 |
| OLD | Assets/Scripts/game/Managers/Network/MsgCoder.cs | 93A7E2627249DACF07FAE46B1A6FC99F0CBD9B0DABA534F1AC73C4605F7C12D7 |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Config/BaseDao.cs | D5E3C415BC83A5BCE065B4E996B9FD417884FA627AD97CC9F255070C6D5EC1DD |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Config/BaseCfgDecoder.cs | 7FA616512828A5EF2D95838DEE732629B2DA723337723831238591CD7760D8D9 |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Config/ConfigMissingCollector.cs | 0FE696CF1E34362AEE5ED4F4436846542C1085EDEE8783ACB1D668280D6E5A7B |
| OLD | Assets/Scripts/game/Helper/DataStorageHelper.cs | 240C7F43EC5036E8FCABFEAAE6B0DD6FDF8C55F7DDB2F139EE056D3394819AD9 |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Utils/Prefs.cs | 77C65D265590B89699841E808642CCF1B3A69DBD521AA5724C45DE9EE915607D |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Utils/FileHelper.cs | F2724F7CA55BF66C0FE290A257731BEEDED1FA839C3C9C518363AB49391A8E9D |
| CURRENT | Assets/TEngine/Runtime/Core/Utility/Utility.Http.cs | 254E2D91ED5F1DD88CCD158C798BF1F1A16BC8E48EBFECD89B149756AEEDA89C |
| CURRENT | Assets/TEngine/Runtime/Core/Utility/Utility.PlayerPrefs.cs | 4FF499D97AF41635E033439D2D7BB4EE0A4C3EF43F87D9E5A13E2A6F0F2D37F5 |
| CURRENT | Assets/TEngine/Runtime/Module/LocalizationModule/Core/Configurables/PersistentStorage.cs | 91C4FDE249FCFF2B88BD2EDC7ED16DAC43EC9A99D3073DAD61996566A5CF5004 |
| CURRENT | Assets/TEngine/Editor/LubanTools/LubanTools.cs | 4DEB4583D2D48F2FA19EDE9F9B6E8D3F48ECD217DC6EBD7F8772214A708ED143 |
| CURRENT | Assets/GameScripts/HotFix/GameProto/LubanLib/ByteBuf.cs | 3991E7225E4921BAA4116B8268B881C75116BBB780E8C28C5FCE071A41515412 |
| CURRENT | Packages/manifest.json | DBBF7DD9A87DFDE6FA082A9ED9C5A23B5B0D0BE7DCFE3CC31617A0746C279496 |

## 文档自检结果

- 文件集合：README.md、findings.md、evidence.md、candidates.md、open-questions.md、verification.md，均位于本目录。
- 证据覆盖：网络传输/编码/分发/请求关联/超时/重连边界，配置接口/路径/失败/生成式代码，PlayerPrefs/文件/版本/一致性，以及当前工程缺口。
- 链接范围：README.md 的阅读顺序只使用本目录内的相对 Markdown 链接，目标文件均存在；源码路径作为取证文本，不依赖当前工程生成可点击链接。
- 变更范围：没有使用 git reset、checkout、删除或覆盖并行文件；没有写入 OLD。
- 风险标记：findings.md 已包含 ## PLAN_CONFLICT；open-questions.md 列出外部生成目录、网络契约、配置版本和持久化一致性待确认项。
