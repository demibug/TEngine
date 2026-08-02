# NET-01 行为说明

## 1. 分析边界

请求范围为 `work/bundle.strings-decoded.js:5087–6037`。实际网络模块由四段非连续证据组成：

- `3316`：静态字段 `Ba=5000`、`ja="playGameCount"`、`qa=1201`、`$a=1203`。
- `3763–3768`：共享单例基类 `qU.instance()`。
- `5087–5392`：网络类 `tn` 的构造、请求和业务接口。
- `5395`：运行时别名 `qK = tn`。

`5395–6037` 的其余内容分别是段位换算、平台基类、场景管理器和用户类型管理器，不属于 NET-01。`qN` 微信平台实现从 `6038` 开始，本轮未进入。

## 2. 类与初始化语义

`tn` 是继承 `qU` 的普通类。它不是模块载入时立即创建的对象；业务通过 `qK.instance()` 首次访问时创建并缓存单例。

构造字段：

| 新字段 | 原字段 | 初始值 | 状态 | 证据 |
|---|---|---:|---|---|
| `loginCloudSaveRaw` | `xa` | `null` | `CONFIRMED` | 登录响应的 `data.userData` 写入，云存档同步读取 |
| `productionBaseUrl` | `path` | 正式 API URL | `CONFIRMED` | `url` getter 返回 |
| `useDebugServer` | `ba` | `false` | `CONFIRMED` | 为真时切换调试 API URL |
| `authentication` | 同名 | `""` | `CONFIRMED` | 每次请求写入请求头 |
| `userId` | `Ma` | `0` | `CONFIRMED` | 登录响应写入，`Aa` 返回 |
| `userType` | 同名 | `0` | `CONFIRMED` | 登录响应写入，`Ea` 返回 |
| `rankingType` | `Pa` | `3` | `INFERRED` | 只作为两个排行接口的 `type` 参数 |
| `channelAppId` | 同名 | `0` | `CONFIRMED` | `init` 写入；NET-01 内未再次读取 |

静态值：

- 默认超时：`5000ms`。
- 云存档上传计数键：`playGameCount`。
- `1201`、`1203`：已确认赋值但全 bundle 无读取，语义为 `UNKNOWN`。

## 3. 基础请求行为

所有请求均通过 `Laya.HttpRequest`：

```text
new Laya.HttpRequest
→ request.http.timeout = timeout
→ request.send(baseUrl + endpoint, data, method, "json", headers)
→ once(Laya.Event.COMPLETE)
→ once(Laya.Event.ERROR)
```

注意：原代码是**先 send，再注册监听**。重建代码没有优化为先注册监听，以免改变顺序。

固定请求头：

```text
Content-Type: application/json
authentication: <当前 authentication 字段>
```

固定响应类型：`json`。

`LayaAir 3.3.10` 的 `HttpRequest` 参考实现位于 `original/libs/laya.core.js:2326–2382`：

- 对象请求体会由引擎 `JSON.stringify`。
- `HTTP 200/204/0` 进入 `complete()`。
- `responseType === "json"` 时由引擎执行 `JSON.parse`。
- JSON 解析异常会转为 `ERROR` 事件。
- 原生 XHR 回调在 complete/error 前由 `clear()` 清理。

NET-01 本身没有：

- 服务端业务 code 检查；
- 重试；
- 请求取消；
- 请求队列；
- 签名计算；
- `wx.request` 或 `tt.request` 直接调用；
- `Laya.Handler` 使用。

因此任何通过 `COMPLETE` 到达的响应，即使 `code=1201/1203/500`，仍交给 `success` 回调。

## 4. 方法行为

| 新方法 | 原符号 | 行为 | 状态 |
|---|---|---|---|
| `initializeChannel` | `init` | 只写入 `channelAppId` | `CONFIRMED` |
| `request` | `request` | 回调式 Laya HTTP 请求 | `CONFIRMED` |
| `requestAsPromise` | `Da` | success→resolve，fail→reject | `CONFIRMED` |
| `waitForPromiseWithinTimeout` | `Ia` | fulfilled→`true`；reject/timeout→`false` | `CONFIRMED` |
| `login` | `Ca` | 空 code 同步 fail；否则 POST 登录，网络失败转 `null` | `CONFIRMED` |
| `applyLoginResponse` | `Ta` | 更新鉴权、用户状态、云存档缓存和省份 | `CONFIRMED` |
| `synchronizeCloudSaveAfterLogin` | `Ra` | 云端较新则应用并刷新道具；否则强制上传本地 | `CONFIRMED` |
| `reportGameStart` | `Oa` | 上报开局 | `CONFIRMED` |
| `reportGameEnd` | `Ya` | 读取当前星数并上报胜负 | `CONFIRMED` |
| `requestCountryRanking` | `Xa` | 国家排行 | `CONFIRMED` |
| `requestProvinceRanking` | `Ga` | 省份排行 | `CONFIRMED` |
| `requestCountryRankingAlias` | `Ha` | 仅转调国家排行 | `CONFIRMED` |
| `requestServerTime` | `getTime` | 获取服务器时间 | `CONFIRMED` |
| `requestBestRankIfDue` | `Wa` | 跨自然日后再请求 `bestRank` | `INFERRED_NAME` |
| `uploadCloudSave` | `Fa` | 首局和每五局上传；force 立即上传 | `CONFIRMED` |
| `uploadUserInfo` | `za` | 上传昵称、头像、省份等调用方数据 | `CONFIRMED` |
| `track` | `track` | 上传埋点；空值或 `length===0` 跳过 | `CONFIRMED` |
| `uploadErrorLog` | `Na` | 上传错误日志 | `CONFIRMED` |
| `baseUrl` | `url` | 正式/调试地址切换 | `CONFIRMED` |
| `getUserId` | `Aa` | 返回用户 ID | `CONFIRMED` |
| `getUserType` | `Ea` | 返回用户类型 | `CONFIRMED` |

## 5. 登录行为

### 请求

```text
POST sys/user/login
```

`loginCode` 仅用于空值判断。实际请求体完全使用调用方传入的 `requestPayload`：

- 微信平台调用体：`{ channelAppId, js_code }`。
- 字节平台调用体：`{ channelAppId, code }`。

NET-01 不自行插入或改写这些字段。

### 成功

无论响应中的业务 `code` 是什么，只要 Laya 发出 `COMPLETE`：

1. 调用 `applyLoginResponse`。
2. 调用可选 `callbacks.success(response)`。
3. Promise resolve 原响应。

### 网络失败

1. 输出 `[Server] login failed or timeout`。
2. 调用可选 `callbacks.fail(error)`。
3. Promise resolve `null`，而不是 reject。

### 空登录 code

1. 同步调用 `callbacks.fail("login code is empty")`。
2. 不创建请求。
3. 返回 `Promise.resolve(null)`。

## 6. 登录响应处理

从 `response.data` 读取：

- `authentication`：只在真值时覆盖旧值；缺失时保留旧鉴权。
- `userId`：仅 `typeof === "number"` 时采用，否则置 0。
- `userType`：仅 `typeof === "number"` 时采用，否则置 0。
- `userData`：保存为后续云存档原始值。
- `attach.province`：非空字符串写入玩家省份，否则写入 `未知`。

当 `userId > 0` 时发布原事件 `sS.xs`。重建代码通过 `emitAuthenticatedUserId(userId)` 语义绑定承载该行为，事件中心正式恢复后替换绑定。

## 7. 云存档流程

### 登录后同步

```text
userId <= 0
  → 记录未登录并退出

parseCloudSaveRaw(loginCloudSaveRaw) 失败
  → 使用本地数据并退出

player.resolveCloudOnLoad(cloudSave) === true
  → 云端较新，应用云端数据
  → 调用 uq.instance().Ua() 的语义绑定，重建道具数据

player.resolveCloudOnLoad(cloudSave) === false
  → 本地局数不低于云端
  → uploadCloudSave(true) 强制回传本地存档
```

### 上传节流

非强制上传时：

1. 读取 `Laya.LocalStorage["playGameCount"]`，缺失按 `0`。
2. `Number(value) + 1`。
3. 无条件写回字符串。
4. 仅计数为 `1` 或 `5` 的倍数时上传。

非法存储值会变成 `NaN`，写回字符串 `"NaN"` 并跳过上传；重建代码保留该边界行为。

## 8. 服务端接口清单

| 接口 | 方法 | 数据 | 回调/处理 |
|---|---|---|---|
| `sys/user/login` | POST | 调用方 payload | 应用登录响应；失败转 `null` |
| `sys/user/data` | POST | `player.cloudPush()` | 只记录成功/失败日志 |
| `sys/user/info` | POST | 调用方 payload | 只记录成功/失败日志 |
| `sys/server/time` | GET | `null` | 原样回调 |
| `zyyad/game/start` | GET | `null` | 原样回调 |
| `zyyad/game/end?star={star}&win={0|1}` | GET | `{ skin: 1 }` | 原样回调 |
| `zyyad/game/country/list?type=3` | GET | `null` | 原样回调 |
| `zyyad/game/province/detail/list?type=3` | GET | `null` | 原样回调 |
| `bestRank` | GET | `null` | 只在自然日差至少 1 天后请求 |
| `sys/oa/point/add/new` | POST | 调用方 payload | success 不传响应参数 |
| `sys/oa/errorUpload/add` | POST | 调用方 payload | 只记录日志 |

## 9. 定时器、事件和清理

- `waitForPromiseWithinTimeout` 使用 `Laya.timer.once`。
- Promise 先完成时调用 `Laya.timer.clear(HttpClient, timeoutHandler)`。
- 超时发生后底层 Promise 仍继续运行，不会被取消。
- 每个 HTTP 请求用 `once` 注册 COMPLETE 和 ERROR。
- NET-01 没有显式 `reset()` 或对象池回收；Laya `HttpRequest` 在 complete/error 中清理原生 XHR 回调，局部请求对象随后由 GC 回收。

## 10. 晚绑定依赖处理

原 `uq` 在 `13293` 才绑定到 `tw`，但网络类在 `5087` 已定义。原 IIFE 依靠闭包变量的运行时晚绑定工作。

重建代码没有创建空 `uq`，也没有复制后方 `tw`。当前注入的语义接口为：

- `getPlayerData()`
- `onCloudSaveApplied()`
- `parseCloudSaveRaw(raw)`
- `emitAuthenticatedUserId(userId)`
- `calendarDayDifference(a, b)`

配置函数只绑定解析函数；具体玩家对象在方法调用时获取，未提前到构造阶段。

## 11. 测试结果

使用 `LayaHttpMock` 和 `NetworkMock`，没有真实网络访问。

已验证：

- 请求构造和准确操作顺序；
- 正式/调试 URL；
- 所有请求方法、数据、请求头、响应类型和超时；
- COMPLETE/ERROR；
- 空响应、业务错误响应和 JSON 解析 ERROR；
- 登录成功、网络失败和空 code；
- 云端/本地存档决策；
- 上传节流和强制上传；
- 服务端时间→bestRank 的串行顺序；
- 单例创建和测试重置；
- 22 个原函数/访问器全部映射。

## 12. 未实施的维护建议

以下仅为后续建议，本轮未改变行为：

- 把回调 API 统一包装为 Promise；
- 在业务层增加明确的服务端 code 判定；
- 增加取消、重试和请求去重；
- 把正式/调试地址集中到环境配置；
- 修正 send 后注册事件监听的潜在竞态；
- 为非法 `playGameCount` 增加恢复逻辑。
