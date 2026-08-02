# 缺失的 Skill/Boss 资源

Round 07M 已完成技能状态、时间线、清理和 GameOver 核心。缺少的资源不会阻塞逻辑。

详细清单：`analysis/resources/missing-skill-and-boss-resources.json`。

每个条目均标记 `TODO_RESOURCE_MISSING`，并给出正式资源键、路径、动画名、音频键和源码范围。接入资源时应实现对应 Presentation Port，不要修改 Skill/Boss 核心状态机。
