# TEngine Luban 配置表指南

> **适用场景**：ConfigSystem/Tables 访问配置数据、Excel 数据表字段定义、Luban 代码生成流程、配置表初始化与预加载 | **关联文档**：[architecture.md](architecture.md)（AssetRaw/Configs 目录）、[resource-api.md](resource-api.md)（配置文件加载）

## 核心 API

### 技术栈

- **Luban**（luban-next）：Excel/JSON/XML/YAML → C# 代码 + 二进制数据
- **生成格式**：`cs-bin`（C#）+ `bin`（二进制）
- **数据位置**：`Assets/AssetRaw/Configs/bytes/`（YooAsset 管理，收集组 `Configs`，按文件名寻址；目录由转表脚本输出，首次转表后生成）
- **代码位置**：`GameScripts/HotFix/GameProto/GameConfig/`（热更程序集，首次导表后生成）
- **配置工程**：`TEngine/Configs/GameConfig/`（Unity 工程外，与 UnityProject 同级的 TEngine 根目录下）
- **前置条件**：`Tools/Luban/Luban.dll` 需先执行 `Tools/build-luban.bat` 构建（自动克隆 focus-creative-games/luban 源码并 `dotnet build`；仓库中 `Tools/Luban/` 仅跟踪 about.txt）
- **仓库现状**：生成代码与 `.bytes` 数据均未提交——`GameProto/` 下现有 LubanLib 运行时库（`Luban` 命名空间的 ByteBuf/BeanBase/StringUtil/ITypeId），首次转表后才会出现 `GameConfig/`、`ConfigSystem.cs`、`bytes/`

---

### ConfigSystem 加载器

> `ConfigSystem.cs` 不在 Assets 默认目录中：由生成脚本从 `Configs/GameConfig/CustomTemplate/ConfigSystem.cs` 自动复制到 `GameProto/` 根目录（同时复制 `ExternalTypeUtil.cs`），首次导表后出现（当前仓库尚未包含，.gitignore 未忽略该产物，生成后按团队约定提交）。

```csharp
// ConfigSystem.cs（GameProto/ 中，桥接 Luban 与 YooAsset）
public class ConfigSystem
{
    private static ConfigSystem _instance;
    public static ConfigSystem Instance => _instance ??= new ConfigSystem();

    private bool _init = false;
    private Tables _tables;
    private IResourceModule _resourceModule;

    /// <summary>
    /// 懒加载访问所有配置表。首次访问时自动加载。
    /// </summary>
    public Tables Tables
    {
        get
        {
            if (!_init)
            {
                Load();
            }
            return _tables;
        }
    }

    /// <summary>
    /// 加载所有配置表。
    /// </summary>
    public void Load()
    {
        _tables = new Tables(LoadByteBuf);
        _init = true;
    }

    /// <summary>
    /// 通过 YooAsset 加载二进制配置文件。
    /// </summary>
    private ByteBuf LoadByteBuf(string file)
    {
        if (_resourceModule == null)
        {
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
        }
        TextAsset textAsset = _resourceModule.LoadAsset<TextAsset>(file);
        return new ByteBuf(textAsset.bytes);
    }
}
```

**关键点**：
- 使用 `ModuleSystem.GetModule<IResourceModule>()`（非 `GameModule.Resource`）：`GameModule` 类定义在 GameLogic 程序集（`GameScripts/HotFix/GameLogic/GameModule.cs`），而 ConfigSystem 位于 GameProto 程序集；GameLogic 单向依赖 GameProto，GameProto 中无法访问 `GameModule`
- `_resourceModule` 延迟获取并缓存，避免构造时模块未就绪
- `Tables` 属性懒加载，首次访问自动调用 `Load()`
- `LoadByteBuf(file)` 的 `file` 参数即 Luban 数据文件名（不含扩展名，如 `item_tbitem`），与 YooAsset 收集器 `AddressByFileName` 寻址规则匹配

初始化时机：`GameApp.Entrance`（热更入口）中**没有**强制的 ConfigSystem 调用。默认懒加载模式下无需显式调用，首次访问 `ConfigSystem.Instance.Tables` 自动触发；如需启动时全量加载（同步模式），可在热更启动流程自行调用 `ConfigSystem.Instance.Load()`。`ProcedurePreload` 会预加载 PRELOAD 标签资源，但不专门处理配置表。

> 项目默认使用 **lazyload 模板**（Unity 菜单"TEngine/Luban/转表"执行的即 `gen_code_bin_to_project_lazyload.bat`）：除 ConfigSystem 层懒加载外，生成的 `Tables` 内每张表属性也是访问时才加载对应 `.bytes`（见 `CustomTemplate/CustomTemplate_Client_LazyLoad/cs-bin/tables.sbn`），两层按需加载。

---

### 配置数据访问

```csharp
// Tables 生成于 GameConfig 命名空间；表/记录类位于 GameConfig.{模块} 子命名空间（如 GameConfig.item）
var tables = ConfigSystem.Instance.Tables;

// 按 ID 查询（map 模式，默认）
var itemCfg = tables.TbItem.Get(1001);        // 不存在抛异常
var item = tables.TbItem.GetOrDefault(1001);  // 不存在返回 null
var item2 = tables.TbItem[1001];              // 索引器，等价于 Get

// 遍历所有行
foreach (var it in tables.TbItem.DataList) { }

// 字典访问
tables.TbItem.DataMap.TryGetValue(1001, out var cfg);

// 条件查询（quality 为 item.EQuality 枚举字段；注意该列分组为 s，client 目标生成代码不含此字段，需 using GameConfig.item;）
var redItems = tables.TbItem.DataList.Where(i => i.Quality == EQuality.RED).ToList();   // 仅 server/all 目标可用
```

**生成字段风格**：记录类字段为 `public readonly` 的 PascalCase 命名字段（如 Excel 列 `id` → `public readonly int Id;`），不是属性。

#### 配置管理器封装（推荐）

复杂模块封装配置管理器（放 GameLogic 热更程序集，继承 `GameLogic.Singleton<T>`），不直接在业务代码散落 `ConfigSystem.Instance.Tables.TbXxx`：

```csharp
public class ItemConfigMgr : Singleton<ItemConfigMgr>
{
    private TbItem TbItem => ConfigSystem.Instance.Tables.TbItem;

    public Item GetItemConfig(int itemId) => TbItem.GetOrDefault(itemId);

    public List<Item> GetItemsByQuality(EQuality quality) =>
        TbItem.DataList.Where(i => i.Quality == quality).ToList();
}
```

---

## 使用模式

### 配置工程结构

```
TEngine/Configs/GameConfig/                  # Unity 工程外的同级 Configs 目录
├── luban.conf                               # Luban 主配置（分组 c/s/e、schemaFiles、targets）
├── gen_code_bin_to_project.bat/.sh          # 客户端生成（同步模板）
├── gen_code_bin_to_project_lazyload.bat/.sh # 客户端生成（懒加载模板，项目默认）
├── gen_code_bin_to_server.bat/.sh           # 服务器生成（-t server，数据 → Server/GameConfig，代码 → Server/Hotfix/Config/GameConfig）
├── Defines/builtin.xml                      # 内置类型定义（vector2/3/4、vector2int/vector3int 映射 UnityEngine 类型）
├── Datas/                                   # Excel 数据源
│   ├── __tables__.xlsx                      # 表索引（注册所有表）
│   ├── __beans__.xlsx                       # Bean 复合类型
│   ├── __enums__.xlsx                       # 枚举类型
│   └── item.xlsx                            # 业务数据表
└── CustomTemplate/                          # 自定义模板（通常不改）
    ├── ConfigSystem.cs                      # 配置加载器（导表时复制到 GameProto/）
    ├── ExternalTypeUtil.cs                  # Unity 类型转换（导表时复制到 GameProto/）
    └── CustomTemplate_Client_LazyLoad/cs-bin/tables.sbn   # 懒加载 Tables 模板
```

### 当前配置内容概览

仓库现有配置定义（截至当前，Unity 工程内尚无生成产物）：

- **表**：仅注册 `item.TbItem`（输出数据文件 `item_tbitem.bytes`）
- **Bean**（`__beans__.xlsx`）：`item.ItemExchange`（`id`:int 道具id、`num`:int 道具数量）；`test.TestExcelBean1/2`、`test.Shape/Circle/Rectangle` 为模板示例定义
- **枚举**（`__enums__.xlsx`）：`item.EQuality`（WHITE/BLUE/PURPLE/RED）；`test.AccessFlag`（位标记使用示例，含 WRITE|READ 写法）

### 数据表定义

#### __tables__.xlsx 表索引

第 1 行为列头（首列 `##var` 标记），第 2-3 行为列说明，第 4 行起为注册行。当前仅注册一张表：

| full_name | value_type | read_schema_from_file | input | mode |
|-----------|-----------|----------------------|-------|------|
| item.TbItem | Item | true | item.xlsx | （空=默认 map） |

- `full_name`：`模块.表名`（如 `item.TbItem`）→ 生成 `Tables.TbItem` 属性，表类命名空间为 `GameConfig.item`
- `value_type`：记录类名（如 `Item` → 生成 `item/Item.cs`）
- `input`：数据文件（相对 `Datas/`，可逗号分隔多个）
- `mode`：`one`（单例）/ `map`（按 id 索引，默认）/ `list`（列表）
- `output`：数据文件名，默认 `{module}_{表名}` 全小写（如 `item_tbitem`）
- 其余列：`read_schema_from_file`、`index`、`group`、`tags` 等，见表内中文说明行

#### Excel 数据行结构（以 item.xlsx 为例）

```
第1行：##var 标记 + 字段名（id, name, desc, price, upgrade_to_item_id, expire_time, batch_useable, quality, exchange_stream, exchange_list, exchange_column）
第2行：##var 标记 + Bean 子字段展开列（如 exchange_column 拆分为 id、num 两列，第2行填子字段名）
第3行：##type 标记 + 类型（int, string, string, int, int#ref=item.TbItem, datetime?, bool, item.EQuality, item.ItemExchange, (list#sep=;),item.ItemExchange, item.ItemExchange）
第4行：##group 标记 + 分组（c=客户端, s=服务端，可逗号组合如 c,s）
第5行：## 标记 + 注释（名字、描述、价格、品质等中文说明）
第6行起：数据
```

> 分组取值由 `luban.conf` 的 groups 定义：`c`（客户端）/ `s`（服务端）/ `e`，组合用逗号书写（如 `c,s`），不存在 `cs` 写法。普通字段不需要分组标记时，该列留空即可（如 id/name/price 列在第 4 行未填分组）。

---

### 代码生成

```bat
# 方式一：Unity 菜单 TEngine → Luban → 转表（快捷键 Alt+X；Windows 执行 lazyload .bat，macOS/Linux 执行对应 .sh）
# 方式二：在 TEngine/Configs/GameConfig/ 目录下运行
gen_code_bin_to_project_lazyload.bat   # 懒加载（项目默认）
gen_code_bin_to_project.bat            # 同步加载
gen_code_bin_to_server.bat             # 服务器（-t server，数据 → Server/GameConfig，代码 → Server/Hotfix/Config/GameConfig）
```

生成产物（自动生成，勿手改）：
```
GameScripts/HotFix/GameProto/GameConfig/ → Tables.cs（GameConfig 命名空间）
                                         → item/TbItem.cs、item/Item.cs（按模块分子目录/子命名空间）
GameScripts/HotFix/GameProto/            → ConfigSystem.cs、ExternalTypeUtil.cs（脚本自动复制）
AssetRaw/Configs/bytes/                  → item_tbitem.bytes（{module}_{表名} 全小写）
```

---

### 类型支持

| Excel 类型 | C# 类型 | 示例 |
|-----------|---------|------|
| `int` | `int` | `100` |
| `long` | `long` | `1000000` |
| `float` | `float` | `1.5` |
| `bool` | `bool` | `true` |
| `string` | `string` | `"剑士"` |
| `int#ref=item.TbItem` | `int`（引用校验，另生成 Ref 属性） | `1001` |
| `datetime?` | `long?`（Unix 时间戳，可空） | `2024-01-01 00:00:00` |
| `item.EQuality`（枚举） | 枚举类型 | `RED` |
| `(list#sep=;),item.ItemExchange` | `List<ItemExchange>` | `10001,2;10002,3` |
| `vector3` | `UnityEngine.Vector3` | `1,2,3` |
| `item.ItemExchange`（Bean） | `ItemExchange` 类 | `10001,2`（按 Bean `sep=,` 分隔） |

> `vector2/3/4`、`vector2int`、`vector3int` 由 `Defines/builtin.xml` 映射为 UnityEngine 类型，构造经 `ExternalTypeUtil` 转换。完整类型系统见 Luban 官方文档。

---

### 添加新配置表

```
1. __tables__.xlsx 注册新表：full_name=模块.TbNewTable, value_type=NewTableRow, input=new_table.xlsx（mode 空=map）
2. 创建 Datas/new_table.xlsx，添加列定义和数据
3. （如需）在 __beans__.xlsx 定义 Bean、__enums__.xlsx 定义枚举
4. 运行 gen_code_bin_to_project_lazyload.bat（或 Unity 菜单 TEngine/Luban/转表）
5. 验证：GameConfig/{模块}/ 下新增 TbNewTable.cs/NewTableRow.cs，Configs/bytes/ 下新增 模块_tbnewtable.bytes
6. 在 GameLogic 中创建 NewTableConfigMgr : Singleton<NewTableConfigMgr> 封装查询方法
```

**注意**：新增字段前向兼容（旧客户端忽略），删除字段不可前向兼容。

---

## 常见错误

| 错误 | 原因 | 修复 |
|------|------|------|
| ConfigSystem.cs 找不到 | 首次导表前尚未生成，当前仓库未包含该文件 | 运行生成脚本，自动从 `CustomTemplate/ConfigSystem.cs` 复制到 `GameProto/`（连同 `ExternalTypeUtil.cs`） |
| Luban.dll 不存在 | 未构建 Luban 工具 | 执行 `Tools/build-luban.bat`（克隆 focus-creative-games/luban 并 dotnet build） |
| `GameModule.Resource` 在 GameProto 中不可用 | `GameModule` 类定义在 GameLogic 程序集，GameProto 不依赖 GameLogic | ConfigSystem 使用 `ModuleSystem.GetModule<IResourceModule>()` |
| `_resourceModule` 为 null | Load() 在模块系统初始化前调用 | 确保在模块系统运行后（热更启动流程中）调用 |
| .bytes 加载失败 | 文件未生成或未在 YooAsset 收集器中 | 确认 `AssetRaw/Configs/bytes/` 下已有产物；收集规则在 `Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset`（Configs 组，`CollectPath=Assets/AssetRaw/Configs`） |
| Tables 为 null | 未调用 ConfigSystem.Instance.Load() | 懒加载会在首次访问 Tables 时自动 Load，确认对应 `.bytes` 已生成 |

---

## 交叉引用

- 架构总览见 [architecture.md](architecture.md)
- 热更开发见 [hotfix-workflow.md](hotfix-workflow.md)
- 资源加载见 [resource-api.md](resource-api.md)
- 问题排查见 [troubleshooting.md](troubleshooting.md)
