# 配置预加载验证记录（任务 1.11）

> 关联 OpenSpec Change：`port-minimal-battle-to-gamebattle`
> 关联任务：tasks.md 1.11（第 1 节 Phase 0）
> 关联规格：`specs/battle-config-snapshot/spec.md`、`specs/battle-hotfix-integration/spec.md`
> 记录日期：2026-08-05
> 状态：**已验证，结论为未补齐预加载（范围受限，移交给任务 3.4）**

本记录独立于同批次任务 1.8 的战斗移植总纲，仅承载 1.11 的时序验证结论，避免写入冲突。

---

## 1. 验证目标

1. `Assets/AssetRaw/Configs` 是否被 YooAsset 收集，且 `PRELOAD` 标签是否覆盖战斗配置 `.bytes`。
2. `ProcedurePreload` 到 `ConfigSystem.Instance.Load()` 的时序：是否在预加载阶段显式加载配置 Tables。
3. 若配置未预加载，是否补齐启动期预加载，使 `BattleSimulation` 子步不触发同步 IO。

---

## 2. YooAsset 收集验证

**证据文件**：`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset`

- `Configs` 组（行 54-66）`CollectPath: Assets/AssetRaw/Configs`，`AddressRuleName: AddressByFileName`，`FilterRuleName: CollectAll`，`PackRuleName: PackDirectory`，`ActiveRuleName: EnableGroup`。
- 组级 `AssetTags`（行 56）与 collector 级 `AssetTags`（行 65）**均为空**。

**结论**：

- Configs 目录**已被收集**，`Assets/AssetRaw/Configs/bytes/` 下的配置文件以文件名为 address 纳入 YooAsset 寻址。
- **无 `PRELOAD` 标签**：整个 setting 中无任何资源被打 `PRELOAD` 标签（全文件 grep 仅在 `ProcedurePreload.cs` 消费侧出现 `GetAssetInfos("PRELOAD")`，收集侧无对应供给）。

**实际配置文件清单**（`Assets/AssetRaw/Configs/bytes/`，共 18 个 `.bytes`）：

```
battle_tbboss.bytes      battle_tbeconomy.bytes   battle_tbgeneral.bytes   battle_tbprojectile.bytes  battle_tbresultschema.bytes  battle_tbunitlevel.bytes   battle_tbweapon.bytes        item_tbitem.bytes
battle_tbbuff.bytes      battle_tbenemy.bytes     battle_tbevent.bytes     battle_tbmap.bytes         battle_tbrank.bytes          battle_tbskill.bytes       battle_tbunit.bytes          battle_tbwave.bytes
battle_tbweaponregistry.bytes   battle_tbweapontext.bytes
```

战斗相关 `battle_*.bytes`（map/wave/enemy/unit/economy 等）**均已存在于收集目录内**，因此“是否被收集”成立，但“是否带 PRELOAD 标签”不成立。

---

## 3. ProcedurePreload → ConfigSystem.Load 时序验证

**证据文件**：

- `Assets/GameScripts/Procedure/ProcedurePreload.cs`
- `Assets/GameScripts/HotFix/GameProto/ConfigSystem.cs`
- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
- `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`

### 3.1 ProcedurePreload 行为

- `PreloadResources()`（行 118-124）在 `_needProLoadConfig` 为 true 时调用 `LoadAllConfig()`。
- `LoadAllConfig()`（行 126-150）：
  - **EditorSimulateMode 直接 return**（行 128-131），不查询 `GetAssetInfos("PRELOAD")`，不预加载任何配置。
  - 非 EditorSimulateMode 调用 `_resourceModule.GetAssetInfos("PRELOAD")`（行 133），但因收集侧无 PRELOAD 标签，返回空数组，循环 0 次。
  - 行 145-149：`_loadedFlag.Count <= 0` 时直接 return。
- **全程未调用 `ConfigSystem.Instance.Load()`**。`ProcedurePreload` 只做 YooAsset 资产预加载，与 Luban `Tables` 解析无任何关联。

### 3.2 ConfigSystem 行为

- `Tables` 属性（行 19-30）为**懒加载**：`_init` 为 false 时首次 getter 触发 `Load()`。
- `Load()`（行 37-41）：`_tables = new Tables(LoadByteBuf)`，`LoadByteBuf`（行 48-57）调用 `_resourceModule.LoadAsset<TextAsset>(file)` —— 这是**同步**加载 API（`resource-api.md` 列为“同步加载，需资源已预加载”）。
- **无任何代码显式调用 `ConfigSystem.Instance.Load()`**：`GameApp.Entrance`（行 25-34）只做 `GameEventHelper.Init()`、保存程序集、注册销毁回调、`StartGameLogic()`（直接 `ShowUIAsync<BattleMainUI>()`），未触发配置加载。

### 3.3 时序结论

当前时序链为：

```
ProcedurePreload（预加载 PRELOAD 资产 → 实际为空）
  -> ProcedureLoadAssembly（加载热更 DLL，反射调用 GameApp.Entrance）
    -> GameApp.Entrance（未调用 ConfigSystem.Load）
      -> StartGameLogic（直接开 UI）
        -> 业务首次访问 ConfigSystem.Instance.Tables → 懒加载 → 同步 LoadAsset
```

**配置未在启动期预加载**。若业务首次访问 `Tables` 发生在 `BattleSimulation` 子步内（例如构建 `BattleConfigSnapshot` 时读取 Luban 表），`LoadByteBuf` 的同步 `LoadAsset<TextAsset>` 会在模拟子步触发**同步 IO**，违反 `battle-config-snapshot` spec“逻辑子步不得反复访问资源加载器”及 1.11“不允许 BattleSimulation 子步触发同步 IO”。

### 3.4 程序集边界约束（为何不在本任务内补齐）

- `Assets/GameScripts/Procedure/` 下**无 asmdef**，`ProcedurePreload` 编入主包 `Assembly-CSharp`（不可热更）。
- `ConfigSystem` 位于热更程序集 `GameProto`（`Assets/GameScripts/HotFix/GameProto/GameProto.asmdef`，`autoReferenced: true`）。
- TEngine 红线（`tengine-dev` SKILL“热更边界”）：`GameScripts/Main` 与 `Launcher/` 不热更，主包**不得引用**热更程序集。若在 `ProcedurePreload` 直接调用 `ConfigSystem.Instance.Load()`，将形成主包引用热更程序集的逆向依赖，破坏热更边界。
- `tengine-dev/references/luban-config.md` 明确推荐的标准初始化时机为：**“`ProcedurePreload` 预加载 PRELOAD 资源后，在 `GameApp.Entrance` 中调用 `ConfigSystem.Instance.Load()`”**。`GameApp.Entrance` 位于热更 `GameLogic`（`ProcedureLoadAssembly` 反射调用后运行），是该调用的合法宿主。
- 但本任务允许修改的文件清单仅含 `ProcedurePreload.cs`、`AssetBundleCollectorSetting.asset`、`ConfigSystem.cs` 与本记录文件，**不含 `GameApp.cs`**。在 `ProcedurePreload` 内无法合法触发 `ConfigSystem.Load`；在 `ConfigSystem.cs` 内自注册又缺乏合法调用时机（仍需热更侧入口驱动）。

**决策**：经用户确认，本任务**不补齐预加载代码**，仅记录结论与风险。预加载补齐由 tasks.md 任务 **3.4** 承接——3.4 原文已明确要求“在 `ProcedurePreload` 完成后、任何 BattleModule 配置访问前初始化 ConfigSystem”，与 `luban-config.md` 推荐位置一致，是补齐 `GameApp.Entrance` 调用与 PRELOAD 标签的正确归属任务。

---

## 4. 待办（移交任务 3.4）

任务 3.4 实施时需同时完成以下两项，方可满足 1.11 与 spec：

1. **补 PRELOAD 标签**（`AssetBundleCollectorSetting.asset`，`Configs` 组或其 collector 的 `AssetTags` 加 `PRELOAD`），使 `ProcedurePreload.LoadAllConfig()` 的 `GetAssetInfos("PRELOAD")` 能取到 `battle_*.bytes` 与 `item_tbitem.bytes`，真正预加载到缓存。
2. **显式调用 `ConfigSystem.Instance.Load()`**：在 `GameApp.Entrance`（热更 GameLogic）中 `GameEventHelper.Init()` 之后、`StartGameLogic()` 之前调用。此为 `luban-config.md` 推荐位置，且在 `ProcedureLoadAssembly` 反射调用 `Entrance` 后运行，时序上晚于 `ProcedurePreload` 的 PRELOAD 预加载，保证 `LoadByteBuf` 的同步 `LoadAsset` 命中已预加载缓存、不触发真实同步 IO。

可选增强（非强制，3.4 视情况）：将 `ConfigSystem.LoadByteBuf` 由同步 `LoadAsset` 改为已预加载后的缓存命中，或提供异步 `LoadAsync()` 在 `Entrance` 内 `await`，进一步消除同步路径风险。

---

## 5. EditorSimulateMode 与真机差异（已知风险）

- `ProcedurePreload.LoadAllConfig` 在 `EditorSimulateMode` 直接 return（行 128-131），即编辑器模拟模式下**完全不预加载**，依赖后续懒加载。真机（Offline/Host/Web）模式下虽进入 `GetAssetInfos("PRELOAD")` 分支，但因无标签同样空转。
- 因此当前**所有模式**下配置均未预加载；EditorSimulateMode 只是更早 return，行为结果一致（均懒加载）。
- 3.4 补齐后须注意：`EditorSimulateMode` 下 YooAsset 直接读文件系统，`LoadAsset` 同步命中本地文件，风险较低但仍为同步调用；真机依赖 PRELOAD 预加载缓存命中。两种模式均需在 3.4 验证 `Load()` 在 `Entrance` 触发后不进入模拟子步。

---

## 6. 验证命令摘要

- `find Assets/AssetRaw/Configs -type f`：确认 18 个 `.bytes` 存在。
- `grep -n "PRELOAD\|Configs\|AssetRaw" AssetBundleCollectorSetting.asset`：确认 Configs 组被收集、无 PRELOAD 标签。
- `Read ProcedurePreload.cs`：确认 `LoadAllConfig` EditorSimulateMode return + `GetAssetInfos("PRELOAD")` + 无 `ConfigSystem.Load` 调用。
- `Read ConfigSystem.cs`：确认 `Tables` 懒加载 + `LoadByteBuf` 同步 `LoadAsset<TextAsset>`。
- `Read GameApp.cs`：确认 `Entrance` 未调用 `ConfigSystem.Load`。
- `find Assets/GameScripts -name "*.asmdef"`：确认 `Procedure/` 无 asmdef（主包），`GameProto.asmdef` 为热更程序集。
- `Read ProcedureLoadAssembly.cs`：确认 `Entrance` 由反射在热更 DLL 加载后调用。

---

## 7. 结论

- Configs 目录**已被 YooAsset 收集**。
- PRELOAD 标签**缺失**，`battle_*.bytes` 未被 PRELOAD 覆盖。
- `ProcedurePreload` **未显式调用** `ConfigSystem.Load`；`ConfigSystem.Tables` 懒加载，懒加载路径为同步 IO。
- **未补齐预加载**（用户决策：范围受限，移交给任务 3.4）。
- 风险：当前状态下 `BattleSimulation` 子步首次访问配置将触发同步 IO，违反 spec 与 1.11。需在任务 3.4 通过补 PRELOAD 标签 + `GameApp.Entrance` 显式 `ConfigSystem.Instance.Load()` 双管齐下修复。
