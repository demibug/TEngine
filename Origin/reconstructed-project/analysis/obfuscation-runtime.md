# `bundle.js:1–1014` 混淆运行时分析

## 范围与状态

| 范围 | 内容 | 处理状态 |
|---|---|---|
| `bundle.js:1–1000` | 可隔离的常量、字符串表、索引表和符号初始化 | `CONFIRMED`，仅此范围在受限 VM 中求值 |
| `bundle.js:1001–1014` | 反篡改/环境检查和辅助函数 | `CONFIRMED`，未执行、未删除、未改写 |
| `bundle.js:1014:1` 起 | 游戏主体大型 IIFE | 未执行，仅做 AST 静态分析 |

提取文件：`work/obfuscation-runtime.original.js`。实际进入 VM 的安全子集另存为 `work/obfuscation-runtime.safe-eval.js`。

- 大小：75,764 字节
- SHA-256：`c75d469ac4537376709eab6aea22c733400c36f00e061fb7abd9ce2a63f295ad`
- 内容：原件前 1,014 行的逐字节提取

## 运行时组成

### 1. 数学别名与常量

- `bundle.js:1–14`：把 `Math.log`、`Math.pow`、`Math.floor`、`Math.exp`、`Math.abs`、`Math.round` 等绑定到短变量。
- `bundle.js:15–94`：定义大量整数常量。
- `bundle.js:95–134`：以对数、指数、均值不等式和整数运算构造不透明谓词。

这些表达式在当前构建中可以确定求值：40 个谓词中 20 个为真、20 个为假，完整值见 `analysis/opaque-predicate-values.json`。本轮没有删除或折叠它们；控制流简化留到下一阶段并需要逐项证明报告。

### 2. 字符串分片与反转

`bundle.js:237–254` 定义两个纯辅助逻辑：

```js
String.prototype.s = function (chunkSize) { /* 按固定长度分片 */ };

var hp = {
  _4: function (text) { /* 逐字符反转 */ }
};
```

后续 49 组字符串先反转，再按固定字符宽度切分。

### 3. 字符串表 `hr`

`bundle.js:255–354` 构造：

```js
var hr = new Array(49);
```

隔离求值结果：

- 表数量：49
- 字符串条目总数：5,987
- 最大表：`hr[0]`，3,075 项
- 最小表：`hr[48]`，1 项

该表包含：

- JavaScript 属性名与方法名
- Laya API 名称
- 微信/字节 API 名称
- 场景名、资源路径、网络路径
- UI 文案、日志文案
- 游戏业务名称和描述

本轮确认主体 IIFE 中没有对真实 `hr` 根表或其子表执行写操作、更新操作或数组变异方法，因此把静态索引结果替换为同值字符串字面量具有低风险。

### 4. 数值索引表 `hu`

`bundle.js:355–386` 定义字符串 `hs` 和函数 `ht`。`ht` 使用一个 90 字符数字字母表，把逗号分隔的高进制数字解码后加上首项偏移量，生成：

```js
var hu = ht(hs);
```

隔离求值结果：

- `hu.length === 334`
- 主要用于属性索引、控制流顺序、数组位置和混淆常量

本轮使用 `hu` 的静态数值帮助解析字符串别名，但不把单纯数值表达式批量替换进输出；控制流仍保持原样。

### 5. 顶层符号初始化

`bundle.js:387–1000` 把字符串表和索引表映射到数百个短变量，并通过不透明谓词拼接少量字符串。已确认的示例：

| 原符号 | 静态值 | 状态 |
|---|---|---|
| `ck` | `"charAt"` | `CONFIRMED` |
| `cl` | `"5"` | `CONFIRMED` |
| `cm` | `93` | `CONFIRMED` |
| `dY` | `"prototype"` | `CONFIRMED` |
| `d5` | `"resources/anim"` | `CONFIRMED` |
| `fB` | `"STOPPED"` | `CONFIRMED` |
| `fM` | `"onFire"` | `CONFIRMED` |
| `gF` | `https://alicdn.mihuangame.com/wxGame/ZhaoYunAndADou/share_v2.json` | `CONFIRMED` |
| `g9` | `"IK"` | `CONFIRMED` |

求值结束时：

- `hq === null`
- `hs === null`
- `hb === 0`
- 共捕获 449 个可序列化的顶层原始值或数组
- 其中 426 个顶层绑定可安全用于主体静态传播

### 6. 反篡改区间

#### `bundle.js:1001–1003`

原始检查等价于验证：

```js
String(Math.LN10).charAt(7) === "5";
```

若不相等，会对一个由长哈希字符串第 93 个字符得到的属性名执行自减。该字符同样是 `"5"`。

#### `bundle.js:1004`、`1012–1014`

`hw()` 中比较：

```js
"undefined" !== typeof Int16Array
```

正常 JavaScript 环境中该条件为真，随后会向 `Int16Array` 构造器写入一个长混淆属性名。该副作用可能参与环境探测或保护逻辑。

#### 处理原则

- 没有执行 `bundle.js:1001–1014`
- 没有删除或改写该区间
- `work/bundle.strings-decoded.js` 的前 1,014 行与原件逐字节一致
- 该保护区间 SHA-256：`7ed3d02602a8bb8a1b384780a759e0b4ae2e37f52e74cd042a308bb110c589e9`

## 隔离求值安全边界

解码工具只执行 SHA-256 固定为 `180d9cc230e43ded3088726cd68f2fc661c64c7b8883036f2ddb6cfd41fde9e7` 的 `bundle.js:1–1000`。

VM 中明确屏蔽：

- `wx`
- `tt`
- `Laya`
- `require`
- `process`
- `fetch`
- `XMLHttpRequest`
- `WebSocket`
- `Date`
- `performance`
- 定时器
- `Function`
- `eval`
- `console`

并禁用动态代码生成和 `Math.random`。完整 IIFE、平台 API、网络、文件系统、场景和战斗代码均未执行。
