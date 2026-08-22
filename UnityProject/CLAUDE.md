# CLAUDE.md

请使用中文写提案和回答
这个文件为 Claude Code (claude.ai/code) 提供指导，用于处理此代码库中的代码。

TEngine 基于 HybridCLR + YooAsset + UniTask + Luban 构建。

## 通用编辑约束

- 新增或修改的注释必须使用中文。
- 文件保持 UTF-8 无 BOM。修改已有文件时保持原有 EOF 换行状态；新文件默认以换行结尾。
- 新增或修改变量、字段、属性、函数、事件、类、接口、结构体、枚举、命名空间及其他标识符时，优先使用常见、简单、易懂、简短的英文单词。避免生僻词和不必要的复杂缩写；已有项目缩写、Unity / C# 官方命名以及外部协议、接口、资源、配置和第三方 API 名称保持原样。不要仅为符合本规则而大范围重命名已有代码。
- 除非必要，业务对象先存入具名局部变量，再作为方法实参。
- 除非必要，方法结果先存入具名局部变量，再返回。

---

## 命令执行约束（减少权限弹窗）

- 每次 Bash 调用只执行一条命令，禁止用 `&&`、`;`、`|` 或 `||` 拼接；多步操作分多次调用。
- `ls`、`cat`、`grep`、`git diff`、`git log`、`head`、`tail` 等只读命令单独调用。
- 仅在确有必要时使用管道；不要追加 `echo "exit=$?"`，直接读取命令返回值。

---

## 强制工作流

- L1 仅限不涉及框架 API、UI 节点、事件、资源、生命周期、模块或静态状态的 typo、注释、日志和单行变量改名；典型 L2+ 标志包括 `UIWindow`/`UIWidget`、`GameEvent`/`AddUIEvent`、`LoadAssetAsync`/`UnloadAsset`、`GameModule`、HybridCLR/YooAsset 和资源路径。不确定时上调到 L2。
- L2（已知 API 的单主题局部修改）、L3（新功能、跨文件或新增 UI/资源/事件逻辑）和 L4（架构、重构或多模块协作）必须先使用 `tengine-dev`。reference 选择、章节级读取、源码核验和会话缓存仅以该 skill 为准；触发 skill 不等于读取全部 reference。
- L2 修改代码前简要说明领域与入口、调用链、方案、风险和验证；L3/L4 完整说明领域与入口、调用链、数据流、生命周期、方案、文件与位置、风险和验证。
- reference 与当前代码 API 冲突时，用 Grep 搜索实际声明和调用方，以源码为准，在交付中说明冲突，并按“问题记录”规则保存。

---

## 核心原则（编码红线）

1. **异步优先**：IO 操作用 `UniTask`，禁止同步加载/Coroutine
2. **模块访问**：通过 `GameModule.XXX` 访问，而非 `ModuleSystem.GetModule<T>()`
3. **资源必须释放**：`LoadAssetAsync` 对应 `UnloadAsset`，GameObject 用 `LoadGameObjectAsync`
4. **热更边界**：`GameScripts/Main` 不热更，`GameScripts/HotFix/` 全部热更
5. **事件解耦**：模块间用 `GameEvent`，UI 内部用 `AddUIEvent`

---

## 问题记录

发现 reference 与源码冲突、知识库导致编译/运行错误，或用户指出文档错误时，记录到 `.claude/memory/problem_YYYY-MM-DD.md`。仅记录问题现象、文档位置、源码验证后的正确 API 和建议修正。
