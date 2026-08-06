# Phase 1 asmdef / 热更配置确认记录（task 2.1）

> 本文件是 OpenSpec change `port-minimal-battle-to-gamebattle` task 2.1 的执行产物。
> task 2.1 性质为"确认 / 补齐"，非"重做"。task 1.6 / 1.7 / 1.12 已在 Phase 0 完成实际配置，
> 本 task 独立核对 1.7 配置是否完整满足 2.1 全部要求，并补齐任何遗漏。
> 独立成文以避免与同批次 task 2.3 / 2.8 / 2.11 的文件写入冲突。
> 静态验证日期：2026-08-05（Unity CLI 不可用，结论基于源码 + 配置文件静态检查 + 引用图解析）。

## 0. 总结论

- **结果：全部就绪，无需修改任何配置文件。** task 1.6 / 1.7 已完成的配置完整满足 task 2.1 的全部 7 条要求。
- 本 task **未修改** `GameBattle.asmdef`、`UpdateSetting.asset`、`HybridCLRSettings.asset`、`AssetBundleCollectorSetting.asset` 中任何一项——配置已完整，最小修改原则下不再触碰。
- 唯一可选项（GameBattle.asmdef 是否预置 UniTask reference）经评估**决定留给 task 2.4**，理由见 §2。
- 独立引用图解析确认：6 个热更 asmdef 之间**无循环引用**，GameLogic 为拓扑终点（最后加载）。

## 1. GameBattle.asmdef 最小依赖确认

实际文件 `Assets/GameScripts/HotFix/GameBattle/GameBattle.asmdef`（task 1.6 产物，未改）：

```json
{
    "name": "GameBattle",
    "rootNamespace": "GameBattle",
    "references": [
        "GUID:24c092aee38482f4e80715eaa8148782",  // TEngine.Runtime
        "GUID:c9a297ec4539fd644a9fd43c8974f15f"   // GameCommon
    ],
    ...
}
```

GUID 校验（来自各 `.asmdef.meta`）：
- `24c092aee38482f4e80715eaa8148782` → TEngine.Runtime ✓
- `c9a297ec4539fd644a9fd43c8974f15f` → GameCommon ✓

结论：
- 符合热更拓扑 `GameProto → GameCommon → GameFUI → GamePlay → GameBattle → GameLogic`：GameBattle 只引 GameCommon + TEngine.Runtime，**不反向**引 GameFUI / GamePlay / GameLogic / GameProto。
- 不需引 GameProto（GameBattle 不直接消费 Luban 生成层；经 GameCommon 间接可达）。
- 不需引 GameFUI / GamePlay（拓扑中它们在 GameBattle 上游，GameBattle 不反向依赖；表现层经端口接入，见 design 决策 4 / task 7.x）。

### 1.6 / 1.12 产物完整性（未破坏）

- `Assets/GameScripts/HotFix/GameBattle/EventSpike.cs`（task 1.6）存在，`using TEngine;`，仅用 `[EventInterface]` + `GameEvent.Get<T>()`，**不使用 UniTask** → 当前 references 足够。
- `Assets/GameScripts/HotFix/GameBattle/AssemblyInfo.cs`（task 1.12）存在，`[assembly: InternalsVisibleTo("GameBattle.Tests")]`，依赖 asmdef `name` 保持 "GameBattle"——**未改 name，friend assembly 仍有效**。
- `Assets/GameScripts/HotFix/GameBattle.Tests/GameBattle.Tests.asmdef`（task 1.12）references 含 `GameBattle` + TestRunner，未被触碰。
- asmdef `name` / `rootNamespace` 保持 1.6 值 → 1.6 spike 的 SourceGenerator 生成归属与 1.12 的 InternalsVisibleTo 均不失效。

## 2. UniTask reference 预置决策（留给 task 2.4）

任务说明："核对是否需补 UniTask reference（2.4 将用 UniTask/CancellationToken，可在此预置或留给 2.4）"。

**决定：留给 task 2.4，不在 2.1 预置。**

理由：
1. GameBattle 当前**无任何 .cs 文件使用 UniTask**：`EventSpike.cs`（1.6）只用 TEngine；目录下无 Runtime/ / Module/ 生产代码（属同批次 2.8 / 2.11 或后续）。
2. task 1.6 / 1.7 spike 记录明确 GameBattle references 为 GameCommon + TEngine，**刻意不含 UniTask**（`hotfix-topology-spike-record.md` §2、§8）。
3. task 2.4 原文："公共异步 API 使用 UniTask/CancellationToken"——即 UniTask 的消费点在 2.4 创建 `IBattleModule` / `BattleOperationResult` 异步 API 时才出现。在 2.4 同批次添加 UniTask reference 是最小且最准确的时机（按需引入，与首个消费代码同批）。
4. 现在预置会产生**未被引用的 asmdef reference**（Unity 允许但违反最小依赖原则，且 1.7 spike 已定性"最小 references"）。

UniTask asmdef GUID（备用，供 2.4 直接使用）：`f51ebe6a0ceec4240a699833d6309b23`（来自 `Packages/UniTask/Runtime/UniTask.asmdef.meta`）。

> 若 2.4 执行时确认公共异步 API 确需 UniTask，应在 `GameBattle.asmdef` 的 `references` 数组追加 `"GUID:f51ebe6a0ceec4240a699833d6309b23"`，并保持 1.6 / 1.12 既有 references 不动。

## 3. UpdateSetting.asset 确认

实际文件 `Assets/TEngine/Settings/UpdateSetting.asset`（task 1.7 产物）：

```yaml
HotUpdateAssemblies:
- GameProto.dll
- GameCommon.dll
- GameFUI.dll
- GamePlay.dll
- GameBattle.dll
- GameLogic.dll          # 最后
LogicMainDllName: GameLogic.dll
AssemblyTextAssetExtension: .bytes
AssemblyTextAssetPath: AssetRaw/DLL
AOTMetaAssemblies: [mscorlib.dll, System.dll, System.Core.dll, TEngine.Runtime.dll, UniTask.dll, YooAsset.dll, UnityEngine.CoreModule.dll]
```

核对：
- HotUpdateAssemblies **6 项齐全**，顺序严格符合拓扑加载序：GameProto → GameCommon → GameFUI → GamePlay → GameBattle → GameLogic。
- **GameLogic.dll 在列表最后**，且 `LogicMainDllName: GameLogic.dll`（主入口反射调 `GameApp.Entrance`）——GameLogic 最后加载并作为主入口，符合 2.1 要求与决策 0.10 / 0.11。
- `AssemblyTextAssetPath: AssetRaw/DLL` 指向 DLL bytes 目录（与 §5 YooAsset DLL 组一致）。
- AOTMetaAssemblies 未含 GameBattle——正确：AOT 元数据补充对象是 AOT 程序集（mscorlib/System/TEngine.Runtime/UniTask/YooAsset/...），不是热更 dll；GameBattle 新增泛型组合的 AOT 补充由 task 8.5 在目标平台 profile 后处理（见 `hotfix-workflow.md` AOT 泛型补充 + spike record §7）。
- **无遗漏，无需补齐。**

## 4. HybridCLRSettings.asset 确认

实际文件 `ProjectSettings/HybridCLRSettings.asset`（task 1.7 产物）：

```yaml
hotUpdateAssemblyDefinitions: []
hotUpdateAssemblies:
- GameProto
- GameCommon
- GameFUI
- GamePlay
- GameBattle
- GameLogic                # 最后
preserveHotUpdateAssemblies: []
patchAOTAssemblies: [mscorlib.dll, System.dll, System.Core.dll, TEngine.Runtime.dll, YooAsset.dll, UniTask.dll, UnityEngine.CoreModule.dll]
outputAOTGenericReferenceFile: HybridCLRGenerate/AOTGenericReferences.cs
```

核对：
- `hotUpdateAssemblies` **6 项齐全**（注意此处无 `.dll` 后缀，与 UpdateSetting 的带后缀列表是两套不同字段，均正确）。
- 顺序与 UpdateSetting 一致，GameLogic 最后。
- `hotUpdateAssemblyDefinitions: []`（空，用名字列表而非 asmdef 引用）——1.7 未改，正确。
- `patchAOTAssemblies` 与 UpdateSetting 的 AOTMetaAssemblies 一致——未改。
- GameBattle 新增泛型组合的 AOT 元数据补充属 task 8.5 范围（目标平台验证后），本 task 不占位（无生产代码 → 无已知泛型组合）。
- **无遗漏，无需补齐。**

## 5. DLL 复制 + YooAsset 配置确认

### 5.1 DLL bytes 复制机制（机制确认，非手工建 bytes）

源码 `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`：

- `CopyHotUpdateAssembliesToAssetPath()`（line 158-173）遍历 `SettingsUtil.HotUpdateAssemblyFilesExcludePreserved`，对每个热更 dll：
  - `src = HybridCLRData/HotUpdateDlls/{Target}/{dll}`
  - `dst = {Application.dataPath}/{UpdateSetting.AssemblyTextAssetPath}/{dll}.bytes`  → `Assets/AssetRaw/DLL/{name}.dll.bytes`
  - `File.Copy(src, dst, true)`
- `HotUpdateAssemblyFilesExcludePreserved` 来源于 `HybridCLRSettings.hotUpdateAssemblies`（本配置已含 GameBattle）→ **GameBattle.dll.bytes 会由构建流程自动生成并拷贝到 `Assets/AssetRaw/DLL/`**。
- 菜单入口：`HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath`（line 86）。

`Assets/AssetRaw/DLL/` 当前为空目录——**符合预期**：DLL bytes 由构建流程生成，非手工提交；当前未跑真机构建。**不需手工新建 GameBattle.dll.bytes 占位文件**。

### 5.2 YooAsset DLL 收集组确认

`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset` 与 `AssetBundleCollectorConfig.xml` 的 DLL 组均存在且覆盖 `Assets/AssetRaw/DLL`：

```xml
<Group GroupActiveRule="EnableGroup" GroupName="DLL" GroupDesc="" AssetTags="">
  <Collector CollectPath="Assets/AssetRaw/DLL" CollectGUID="3aad79ec1ea08c24c891bd3c669d4125"
    CollectType="MainAssetCollector" AddressRule="AddressByFileName" PackRule="PackDirectory"
    FilterRule="CollectAll" UserData="" AssetTags="" />
</Group>
```

- 规则：`AddressByFileName`（地址=去扩展名文件名，即 `GameBattle.dll`）+ `PackDirectory`（整目录一个 bundle）+ `CollectAll`。
- **任何放入 `Assets/AssetRaw/DLL/` 的 `*.bytes`（含 GameBattle.dll.bytes）自动被收集**，地址为 `GameBattle.dll`（与 `ProcedureLoadAssembly` 的 `LoadAssetAsync<TextAsset>(assetLocation)` 中 `assetLocation = hotUpdateDllName` 一致）。
- **不需新增 GameBattle 专用收集规则**：既有 DLL 组已覆盖。
- `.asset` 与 `.xml` 的 DLL 组完全一致；二者仅 FUI 组不同步（`.asset` 缺 FUI 组，`.xml` 有）——属 integrate-fairygui-module 遗留，非本 task 范围，本 task 未触碰两者。

### 5.3 非 Editor 加载链路（决策 0.11 所有权对齐）

`Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`：
- 真机/打包模式按 `HotUpdateAssemblies` 顺序 `LoadAssetAsync<TextAsset>(name)` → `Assembly.Load(textAsset.bytes)` → `_hotfixAssemblyList.Add(assembly)` → **`_resourceModule.UnloadAsset(textAsset)`**（line 217）释放 DLL TextAsset。
- 全部加载完后反射调 `_mainLogicAssembly.GetType("GameApp").GetMethod("Entrance").Invoke(...)`（GameLogic.dll 中的 GameApp）。
- **加载顺序由 HotUpdateAssemblies 列表顺序决定**：GameBattle 在 GameLogic 之前；GameLogic 最后加载并反射调用 Entrance——符合拓扑。
- DLL TextAsset 由 Launcher 在 Load 后释放，不由战斗持有（决策 0.11）。

## 6. 程序集循环引用独立解析

### 6.1 解析后的引用图（6 热更 asmdef，仅 asmdef-to-asmdef 边）

| asmdef | 引用的热更 asmdef（解析 GUID 后） | 引用的外部/包 |
|---|---|---|
| GameProto | （无） | TEngine.Runtime + ByteBuf/Luban 包 GUID |
| GameCommon | （无） | TEngine.Runtime |
| GameFUI | （无） | TEngine.Runtime + FairyGUI + YooAsset + UniTask |
| GamePlay | （无） | （空，无代码） |
| GameBattle | GameCommon | TEngine.Runtime |
| GameLogic | GameProto + GameCommon + GameBattle | TEngine.Runtime + UniTask + YooAsset + 包 GUID |

热更 asmdef 间的边（仅这些参与环检测）：
```
GameBattle -> GameCommon
GameLogic  -> GameProto, GameCommon, GameBattle
（GameProto / GameCommon / GameFUI / GamePlay 不引用任何热更 asmdef）
```

### 6.2 环检测结果

DFS 三色标记法（WHITE/GRAY/BLACK）在 6 节点子图上独立运行：
- **环数：0**。无循环引用。
- 入度（被多少热更 asmdef 引用）：GameCommon=2，GameProto=1，GameBattle=1，GameFUI=0，GamePlay=0，GameLogic=0。
- **引用 GameLogic 的热更 asmdef：[]（空）** → GameLogic 为拓扑终点，最后加载，符合 2.1 要求。

### 6.3 拓扑终点确认

- GameLogic 引用 GameBattle + GameCommon + GameProto（单向下游），无人引用 GameLogic → 无环。
- GameBattle 引用 GameCommon，无人引用 GameBattle（除 GameLogic）→ 无环。
- GameCommon / GameProto 无热更 asmdef 出边 → 无环。
- GameFUI / GamePlay 不参与热更 asmdef 间边（GameLogic 不直接引 GameFUI/GamePlay；二者上游可见性由加载顺序保证，非直接 reference）→ 无环。

结论：**引用图无环，GameLogic 最后加载**，满足 2.1 全部要求。

> 注：spike record §1.1 表格曾表述 GameLogic 引用含 GameFUI/GamePlay 的拓扑上游，但实际 `GameLogic.asmdef` 只直接 reference GameProto + GameCommon + GameBattle（asmdef 级），GameFUI/GamePlay 经加载顺序在 GameLogic 之前加载但无直接 asmdef reference。本 task 以实际文件为准——这不构成环，且与"GameLogic 最后加载"一致。

## 7. FairyGUI Change 所有权（未破坏）

参考 `openspec/changes/integrate-fairygui-module/proposal.md` Impact 段：
- fairygui change **拥有** GameFUI 基础设施（GameFUI.asmdef、Runtime/Binding/Resource、生成代码、`Assets/AssetRaw/FUI`、FGUIProject 插件、`AssetBundleCollectorConfig.xml` 的 FUI 组）。
- fairygui change **明确不修改**：GameBattle/GamePlay/GameLogic asmdef、HotFixModules、UpdateSetting、HybridCLR、DLL 复制、Obfuz 配置。

本 task（2.1）核对范围：
- **未改** GameFUI.asmdef、GamePlay.asmdef、GameCommon.asmdef、GameProto.asmdef（1.7 已定，本 task 仅 Read 确认）。
- **未改** `AssetBundleCollectorConfig.xml` / `.asset` 的 FUI 组（`.xml` 的 FUI 组 GUID `7592d58d...` 保留不动）。
- UpdateSetting / HybridCLRSettings 的 GameFUI/GamePlay 条目是 1.7 为对齐拓扑所加，**不修改 fairygui 拥有的文件**，仅扩展共享热更列表（spike record §1.3 已记录此跨 change 协调点）。
- **未破坏** fairygui change 的任何所有权边界。

## 8. 本 task 修改文件清单

**无修改。** 配置已由 1.6 / 1.7 / 1.12 完整就绪，本 task 为纯确认。

新建记录（1 项）：
1. `docs/HotFix架构/phase1-asmdef-config-record.md`（本文件）。

未碰：GameBattle.asmdef（1.6）、AssemblyInfo.cs（1.12）、EventSpike.cs（1.6）、GameBattle.Tests.asmdef / SmokeTest.cs（1.12）、GameLogic.asmdef / GameCommon.asmdef / GameProto.asmdef / GameFUI.asmdef / GamePlay.asmdef（1.7 已定）、UpdateSetting.asset、HybridCLRSettings.asset、AssetBundleCollector（.asset/.xml）、Origin/、openspec/ artifacts、CSV、总纲.md / module-list.md、任何 .cs 业务代码（属同批次 2.3 / 2.8 / 2.11）。

## 9. 对后续 task 的输入

- **task 2.2**：GameBattle.Tests 对 GameBattle 的引用与 friend assembly 配置已就绪（1.12），2.2 只需补 smoke test 逻辑，不动 asmdef。
- **task 2.4**：若公共异步 API 确需 UniTask，在 `GameBattle.asmdef` 追加 `GUID:f51ebe6a0ceec4240a699833d6309b23`（见 §2）。这是 2.1 留给 2.4 的唯一可选补齐项。
- **task 2.7**：GameLogic→GameBattle asmdef reference 已补（1.7），HotFixModules.cs 注册 BattleModule 可编译。
- **task 8.5**：GameBattle 新增泛型组合的 AOT 元数据补充——本 task 未占位，待生产代码确定后补充。
- **task 8.6**：非 Editor HybridCLR 构建验证——本 task 确认了 Assembly.Load 机制与 DLL bytes 复制链路，真机验证留待 8.6。

## 10. 已知风险

- **真机未跑**：所有结论为静态分析（Unity CLI 不可用）。task 8.6 需在非 Editor 构建实测 GameBattle.dll 加载、GameApp.Entrance 反射调用、BattleModule 注册。
- **AOT 元数据未补**：GameBattle 生产代码尚未实现，无已知泛型组合；task 8.5 需在目标平台补充并验证无 `ExecutionEngineException`。
- **`.asset` 与 `.xml` FUI 组不同步**：fairygui 遗留，不影响 DLL 组；建议 fairygui change 或后续同步。
- **GamePlay 空 asmdef**：当前无代码、references 为空。加入热更列表后构建会生成空 GamePlay.dll——无害，待 GamePlay 有代码时需补 asmdef references。
- **UniTask 预置决策**：留给 2.4。若 2.4 未在引入异步 API 时同步补 reference，会编译失败——2.4 执行者须注意。
