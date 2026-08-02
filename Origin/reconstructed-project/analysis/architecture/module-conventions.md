# 重建工程模块规范

版本：Round 02 起生效。

## 1. 模块格式

当前 `src/` 采用 **CommonJS**：

```js
const { Dependency } = require('../path/Dependency');
module.exports = { ReconstructedClass };
```

选择依据：

1. 原始 `game.js` 和最终构建器仍缺失，不能证明原工程可直接运行原生 ES Module。
2. 微信小游戏宿主和常见 LayaAir 小游戏入口均可由 `require` 顺序加载 CommonJS 文件。
3. CommonJS 可在当前 Node 测试环境直接执行，不需要为测试改变 `package.json` 的全局模块类型，也不会破坏第一轮 CommonJS 工具。
4. 最终仍可由待确认的构建器把这些明确模块边界重新打包为单 IIFE；本轮不虚构 Rollup、Webpack 或 LayaAir 构建配置。

此选择是工程承载格式，不代表原始源码一定使用 CommonJS。

## 2. 文件与追溯

- 每个正式模块必须在文件顶部记录原始范围、原始主要符号和重建状态。
- 原符号到新符号的完整映射写入 `analysis/mappings/`，不依赖源码中的猜测性注释。
- 原始协议值，包括 URL、请求字段、事件字符串、资源路径和 UUID，不因可读性改名。
- 逻辑跨越非连续源码范围时，模块报告必须记录所有范围及扩展原因。
- `original/` 和 `work/` 只读或视为不可变输入；重建代码只写入 `src/`。

## 3. 命名与可信度

- `HIGH`：字符串、字段读写、调用目标或返回值可直接证明。
- `MEDIUM`：多个调用点共同支持，但原始语义名称未恢复。
- `LOW`：只能确认技术用途，使用中性名称并保留 `UNKNOWN`。
- 推断命名不能改变协议字段和序列化键。
- 低可信度符号不得使用具体业务名称伪装为已确认事实。

## 4. 单例规范

原 `qU` 单例语义统一重建为：

```js
static instance() {
  return this.Instance || (this.Instance = new this());
}
```

规则：

- 延迟创建，不在模块载入时抢先实例化。
- 实例缓存保存在具体子类的静态 `Instance` 字段。
- 不把单例改成模块级普通对象。
- 测试可以使用显式的 `resetInstanceForTests()` 删除缓存；生产启动代码不得调用该方法。
- 后续模块若继承同一原始 `qU`，统一依赖 `src/core/SingletonBase.js`，不复制单例实现。

## 5. 异步规范

- 回调、Promise、Generator 和 Laya Handler 按原代码逐模块保留，不强制统一成 `async/await`。
- 保留请求、监听、定时器和回调的真实注册顺序，即使该顺序看起来不理想。
- 不自动吞掉 rejection，不新增重试，不新增请求取消。
- 原逻辑将错误转换为 `null` 或布尔值时，重建代码保持相同返回契约。
- 后续可在分析文档提出 Promise 化或错误类型统一建议，但行为等价层不直接实施。

## 6. 错误处理规范

只恢复原代码体现的错误类别：

- Laya `ERROR` 事件代表网络、HTTP、超时或 JSON 解析失败的统一错误通道。
- 服务端业务响应目前由 `COMPLETE` 直接交给调用者；除非原模块明确判断 code，否则不得新增业务错误判定。
- 登录网络失败可由原逻辑转换为 `null`。
- 主动取消、登录失效和平台不可用只有在原代码出现对应分支时才建立类型。
- 不引入未经证明的 `NetworkError`、`ServerResponse` 等抽象。

## 7. 晚绑定与循环依赖

原 IIFE 中在类定义之后才赋值的引用，不得在模块载入时强行解析。

统一策略：

1. 在模块启动阶段注入“解析函数”，而不是直接注入当前对象快照。
2. 业务方法执行时再调用解析函数获取依赖，保持原晚绑定时机。
3. 依赖缺失时抛出明确错误，不使用空对象或 Proxy 吞掉调用。
4. 生产绑定由后续 `bootstrap` 模块统一配置；测试使用可观测 mock。
5. 目标模块正式重建后，用语义模块导入替换临时解析函数，不复制目标实现。

## 8. 平台隔离

- 网络模块不新增 `wx.*` 或 `tt.*` 调用。
- 原模块若直接调用平台 API，先原样迁移，再在分析层记录隔离建议。
- 平台登录负责构造微信或字节平台请求体，`HttpClient.login()` 不修改调用方字段。

## 9. 可测试性

可替换依赖仅用于承载原闭包或全局对象，不改变生产调用顺序：

- `Laya.HttpRequest`
- `Laya.timer`
- `Laya.LocalStorage`
- 玩家数据聚合根
- 事件中心
- 云存档编解码
- 日期差工具

测试必须：

- 禁止真实网络、登录、广告、云存储和分享。
- 比较 URL、方法、请求体、请求头、超时、事件和回调顺序。
- 验证成功、错误、空响应、非法 JSON 对应事件、节流和晚绑定分支。
- 单例测试结束后清理缓存，避免用例间状态污染。

## 10. 构建状态

当前只建立可维护模块边界，不生成未经确认的构建配置。待取得或重建 `game.js`、`game.json` 和完整类注册表后，再确定：

- CommonJS 文件的最终加载顺序；
- 是否由 LayaAir 构建器或独立 bundler 合并；
- 是否需要转为单 IIFE；
- 微信分包和资源包如何映射。

## 11. Round 03 关键路径与开发适配器

- 正式控制器不得读取 `DevelopmentConfig`。
- `directBattle` 只能由 `DevelopmentBootstrap` 的临时 `window.$_main_` 实现，不能写入 LoadScene、MainScene、MatchScene 或 BattleScene。
- 开发适配器目录固定为 `src/**/dev/`；样本值必须标记 `DEVELOPMENT_SAMPLE` 或 `DEVELOPMENT_ONLY`。
- 开发平台和网络对象不得调用 `wx.*`、`tt.*`、真实 HTTP、云存储、广告或分享。
- 未实现方法必须明确抛错，不得使用 Proxy、空对象或动态返回空函数。
- 开发场景工厂仅用于无 `.ls` 的测试，节点必须标记为 `DEVELOPMENT_SCENE_STUB`，不能作为正式场景结构证据。

## 12. GameLoop 规范

- 原 `pV/nx` 统一命名为 `GameLoop`。
- 保留 `frameLoop(1)`、单帧最大 500ms、每次最大 80ms 的逻辑子步。
- 注册键是行为的一部分，不能随意改名：本轮使用 `enemyMgr`、`BattleMgr`、`BattleScene`、`MatchScene`。
- 全局服务注册和每局注册分开清理，不能在结算时移除 `enemyMgr`。

## 13. 场景节点缺失处理

- 正式场景类使用 `requireNode(name)`；必要节点缺失时明确抛错。
- 不凭空构造正式 UI 层级。
- 测试场景工厂可以创建最小节点，但必须与正式控制器文件分离。
- UUID 注册使用 LayaAir 3.3.10 的 `Laya.regClass(uuid)(ClassType)`，不发明新 API。

## 14. 对象池与工厂

- 原代码使用对象池时，必须恢复池键、创建函数、重置顺序和回收时机。
- 未注册实体或敌人类型必须抛错。
- 本轮 `aDou` 使用 `sk_aDou`、`resources/anim/aDou/skeleton.json`、`setIsFastMode(false)`。
- 未恢复的 `uz`/`ro`/`pe` 不能用空类替代；允许通过显式创建器注入开发可观测对象。
