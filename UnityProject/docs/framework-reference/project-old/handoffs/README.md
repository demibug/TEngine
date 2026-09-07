# ProjectOld 框架调查：并行手动交接

目标：以 D:\Work\SAUnity\ProjectOld 的真实代码为准，调查可复刻的框架机制并沉淀到 E:\MyWork\MyFramework\TEngine\UnityProject；本阶段不改代码，不迁移游戏功能。

## 使用方法
1. 在当前工程建立 6 个独立 slave 会话。使用同一个本地工作区，分别发送下面对应提示词文件的全部内容；也可让会话读取对应提示词的绝对路径并执行。
2. 六份任务可同时执行。每份提示词自包含，独占一个输出目录，不依赖其他 slave 完成，也不修改共享索引。
3. 每个 slave 完成后提供文档和 MASTER_REVIEW_HANDOFF。可先单独审查；不必为汇总重复做六次完整调查。
4. 全部完成后，新开 master 会话执行 [07-master-synthesis-review.md](07-master-synthesis-review.md)，集中核验、处理边界冲突，并输出候选设计总表与后续规划入口。
5. 汇总审查通过后再选择需要复刻的设计、形成改造方案；本组任务不授权实现改造。

## 分工
| 提示词 | 负责内容 | 独占输出目录 |
|---|---|---|
| [01 启动、模块组织与热更边界](01-startup-modules-hotfix.md) | 启动、模块组织与热更边界 | ../01-startup-modules-hotfix/ |
| [02 资源与场景生命周期](02-resources-scenes.md) | 资源与场景生命周期 | ../02-resources-scenes/ |
| [03 UI 框架与表现层组织](03-ui-framework.md) | UI 框架与表现层组织 | ../03-ui-framework/ |
| [04 事件、异步与通用运行时机制](04-events-async-utilities.md) | 事件、异步与通用运行时机制 | ../04-events-async-utilities/ |
| [05 网络、配置与持久化抽象](05-network-config-persistence.md) | 网络、配置与持久化抽象 | ../05-network-config-persistence/ |
| [06 编辑器工具、构建与代码生成](06-editor-build-generation.md) | 编辑器工具、构建与代码生成 | ../06-editor-build-generation/ |

## 边界规则
- 01 负责运行时入口、模块生命周期与热更加载；06 负责生成/构建流程。
- 02 负责资源机制；03 负责 UI 对资源的使用契约。
- 04 负责通用事件、任务、计时器和对象池；其他任务记录调用与清理边界。
- 05 负责网络/配置/持久化运行时；06 负责相关生成工具。
- 遇到边界问题，各自在 open-questions.md 记录证据；最后由 master 统一判断，不互相改文件。
- 所有候选只是研究建议，不能当作已经批准的改造决策。

提示词由 master-planner 手动交接流程生成；没有自动创建或启动任何 slave。
