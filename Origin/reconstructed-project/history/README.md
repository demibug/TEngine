# 历史交付文件归档

本目录保存此前已交付、但已被后续累计工程实现替代或与当前模块格式不兼容的文件。

- `superseded/round-02/`：第二轮 ES Module 试验期的 `src/package.json` 和旧验证工具。当前活动工程已经统一为 CommonJS，因此这些文件不能放回活动路径，否则会改变 `src/` 的解析语义。
- `superseded/round-05/`：第五轮早期实现中已被后续同轮实现替换的刀兵攻击、配置、测试辅助和运行工具。

这些文件仅用于追溯，不参与当前 `npm run verify:all`。当前可执行实现位于项目根目录的 `src/`、`tests/` 和 `tools/`。
