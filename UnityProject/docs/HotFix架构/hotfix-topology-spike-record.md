# 热更拓扑 Spike 记录（task 1.7）

> 本文件是 OpenSpec change `port-minimal-battle-to-gamebattle` task 1.7 的 spike 产物。
> 为避免与同批次 task 1.9 并发写 `战斗移植设计总纲.md` 冲突，结论独立记录于此。
> Spike 性质：记录机制为主，不真生成 DLL、不跑真机；但 asmdef references 与共享配置
> 列表须实际补齐，使拓扑可编译且真机构建可加载。
> 静态验证日期：2026-08-05（Unity CLI 不可用，结论基于源码 + 配置文件静态检查）。

## 1. 唯一热更拓扑

目标拓扑（加载顺序，箭头表示“先加载 → 后加载”，依赖方向为后者引用前者）：

```
GameProto → GameCommon → GameFUI → GamePlay → GameBattle → GameLogic
```

GameLogic 必须最后加载（依赖所有上游），禁止程序集循环引用。

### 1.1 asmdef references 现状与补齐

GUID 注册表（asmdef name ←→ GUID，来自各 `.asmdef.meta`）：

| 程序集 | GUID |
|---|---|
| TEngine.Runtime | `24c092aee38482f4e80715eaa8148782` |
| GameProto | `af760644fe07b7945b1afa1ccb7cff14` |
| GameCommon | `c9a297ec4539fd644a9fd43c8974f15f` |
| GameFUI | `473ed939a7a3445bb96ab45d14c81c88` |
| GamePlay | `71cac8d924e6c4b4dbace957d201d5f4` |
| GameBattle | `52382661ab6e0e243961d4e43bbac1ca` |
| GameLogic | `6e76b07590314a543b982daed6af2509` |

各 asmdef 实际 references（本任务前后对照）：

| asmdef | references（本任务后） | 说明 |
|---|---|---|
| GameProto | TEngine.Runtime + ByteBuf/Luban 包 GUID（4 项） | 基线，未改。ByteBuf 等为既有包引用，非 asmdef。 |
| GameCommon | TEngine.Runtime | task 1.6 已加 TEngine.Runtime + rootNamespace。本任务未改。 |
| GameFUI | TEngine.Runtime + FairyGUI + YooAsset + UniTask | 属 integrate-fairygui-module，本任务**未改**。 |
| GamePlay | （空，仅 name） | 属 fairygui/后续 change 范围，本任务**未改**。当前无代码。 |
| GameBattle | TEngine.Runtime + GameCommon | task 1.6 已加，本任务**未改**（保留 1.6 references 与 1.12 AssemblyInfo.cs）。符合拓扑：GameBattle 只需引 GameCommon + TEngine，不反向引 GameFUI/GamePlay/GameLogic。 |
| GameLogic | TEngine.Runtime + GameProto + GameCommon + UniTask + YooAsset + 包 GUID + **GameBattle**（本任务新增） | 本任务新增 `GUID:52382661ab6e0e243961d4e43bbac1ca`（GameBattle）。task 1.6 已加 GameCommon。 |
| GameBattle.Tests | GameBattle + TestRunner | task 1.12 产物，本任务未改。 |

### 1.2 拓扑无环验证

- GameBattle 不引用 GameLogic/GameFUI/GamePlay/GameProto（只引 GameCommon + TEngine）→ 无反向边。
- GameCommon 不引用 GameBattle/GameLogic → 无环。
- GameProto 不引用 GameCommon/GameBattle → 无环。
- GameLogic 引用 GameProto + GameCommon + GameBattle（单向下游）→ 无环。

结论：引用图无环，GameLogic 为拓扑终点（最后加载）。

### 1.3 与 integrate-fairygui-module 分工（不覆盖对方修改）

参考 `openspec/changes/integrate-fairygui-module/proposal.md`、`design.md`、`tasks.md`：

- fairygui change **拥有** GameFUI 基础设施（GameFUI.asmdef、Runtime/Binding/Resource、生成代码、`Assets/AssetRaw/FUI`、FGUIProject 插件）。
- fairygui change **明确不修改**：GameBattle/GamePlay/GameLogic asmdef、HotFixModules、UpdateSetting、HybridCLR、DLL 复制、Obfuz 配置（proposal Impact 段、design 决策 10、tasks 7.7）。
- 本任务修改的共享配置（UpdateSetting.asset、HybridCLRSettings.asset）此前**未被 fairygui change 触碰**（git 确认：fairygui 只改了 `AssetBundleCollectorConfig.xml` 的 FUI 组）。
- 本任务**未改** GameFUI.asmdef、GamePlay.asmdef、GameCommon.asmdef、GameProto.asmdef（fairygui/已确认拓扑拥有）。

→ 本任务在共享配置上**扩展**（加 4 个程序集到热更列表），**未覆盖** fairygui 的 FUI 收集组与 GameFUI asmdef。

## 2. GameBattle asmdef spike 结论

`Assets/GameScripts/HotFix/GameBattle/GameBattle.asmdef` 当前 references（task 1.6 产物，本任务保留不动）：

```json
"references": [
    "GUID:24c092aee38482f4e80715eaa8148782",  // TEngine.Runtime
    "GUID:c9a297ec4539fd644a9fd43c8974f15f"   // GameCommon
]
```

- 符合拓扑：GameBattle → GameCommon + TEngine.Runtime。
- **不需**引 GameFUI/GamePlay（拓扑中它们在 GameBattle 上游，GameBattle 不反向依赖）。
- **不需**引 GameProto（GameBattle 经 GameCommon 间接可达；GameProto 是 Luban 生成层，GameBattle 不直接消费）。
- 1.6 的 EventSpike.cs 与 1.12 的 AssemblyInfo.cs **未破坏**（asmdef name 保持 "GameBattle"，rootNamespace 保持 GameBattle，InternalsVisibleTo("GameBattle.Tests") 仍有效）。

## 3. DLL bytes 落位机制 spike

### 3.1 生成与拷贝链路

源码：`Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`

```
HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath
  → CompileDllCommand.CompileDll(target)                      # 编译热更 DLL 到 HybridCLRData/HotUpdateDlls/{Target}
  → CopyAOTHotUpdateDlls(target)
       → CopyAOTAssembliesToAssetPath()                       # AOT 元数据 dll → Assets/AssetRaw/DLL/{name}.bytes
       → CopyHotUpdateAssembliesToAssetPath()
            foreach dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved:
              src = HybridCLRData/HotUpdateDlls/{Target}/{dll}
              dst = {Application.dataPath}/{UpdateSetting.AssemblyTextAssetPath}/{dll}.bytes
              File.Copy(src, dst, true)                        # 热更 DLL → Assets/AssetRaw/DLL/{name}.dll.bytes
```

关键：`HotUpdateAssemblyFilesExcludePreserved` 列表来源于 `HybridCLRSettings.hotUpdateAssemblies`（本任务已补 GameBattle 等 4 项）。

### 3.2 DLL bytes 目录现状

`Assets/AssetRaw/DLL/` 当前为**空**（无任何 .bytes）。原因：DLL bytes 由 HybridCLR 构建流程生成，非手工提交；当前未跑过真机构建。`UpdateSetting.AssemblyTextAssetPath: AssetRaw/DLL` 指向此目录。

### 3.3 GameBattle.dll.bytes 占位结论（spike 性质）

- **不需手工新建 GameBattle.dll.bytes 占位文件**：YooAsset Collector 的 DLL 组（见 §4）按目录自动收集，空目录不产生资源；构建流程会自动生成并拷贝。
- 真机构建前需运行 `HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath`，生成 GameBattle.dll.bytes 到 `Assets/AssetRaw/DLL/`。
- 本任务不生成真实 DLL（spike 性质 + 无 Unity CLI）。

## 4. YooAsset 收集 spike

源码：`Assets/Editor/AssetBundleCollector/AssetBundleCollectorConfig.xml`（权威源）+ `.asset`（序列化产物）。

DLL 组（既有，本任务**未改**）：

```xml
<Group GroupActiveRule="EnableGroup" GroupName="DLL" GroupDesc="" AssetTags="">
  <Collector CollectPath="Assets/AssetRaw/DLL" CollectGUID="3aad79ec1ea08c24c891bd3c669d4125"
    CollectType="MainAssetCollector" AddressRule="AddressByFileName" PackRule="PackDirectory"
    FilterRule="CollectAll" UserData="" AssetTags="" />
</Group>
```

- 规则：`AddressByFileName`（地址=文件名，即 `GameBattle`）+ `PackDirectory`（整目录打一个 bundle）+ `CollectAll`。
- 覆盖：任何放入 `Assets/AssetRaw/DLL/` 的 `*.bytes`（含 GameBattle.dll.bytes）自动被收集，地址为去掉扩展名的文件名。
- **不需新增 GameBattle 专用收集规则**：既有 DLL 组已覆盖。
- fairygui 的 FUI 组（`Assets/AssetRaw/FUI`，GUID `7592d58d...`）**保留不动**（integrate-fairygui-module task 4.1 产物）。
- task 1.11 的 Configs 组（`Assets/AssetRaw/Configs`）**保留不动**。

注意：`.asset` 序列化文件与 `.xml` 源**不完全同步**（`.asset` 缺 FUI 组，`.xml` 有）。这是 fairygui change 留下的待同步项，**非本任务范围**；YooAsset 实际以 `.xml` 为构建源。本任务未触碰两者。

## 5. UpdateSetting spike

源码：`Assets/TEngine/Settings/UpdateSetting.asset`

本任务前 `HotUpdateAssemblies` 仅 2 项：`GameProto.dll`、`GameLogic.dll`。
本任务后（按拓扑顺序）：

```yaml
HotUpdateAssemblies:
- GameProto.dll
- GameCommon.dll
- GameFUI.dll
- GamePlay.dll
- GameBattle.dll
- GameLogic.dll
```

- `LogicMainDllName: GameLogic.dll`（主入口 DLL，反射调 `GameApp.Entrance`）——**未改**，GameLogic 仍为最后加载的主入口。
- `AssemblyTextAssetPath: AssetRaw/DLL`——**未改**。
- `AOTMetaAssemblies`（mscorlib/System/System.Core/TEngine.Runtime/UniTask/YooAsset/UnityEngine.CoreModule）——**未改**。GameBattle 新增泛型组合的 AOT 元数据补充由 task 8.5 在目标平台验证后处理（见 §7）。
- 顺序：GameProto 先、GameLogic 后，符合拓扑加载序；中间 4 项为拓扑新增。

## 6. 非 Editor Assembly.Load spike

源码：`Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`、`Assets/GameScripts/HotFix/GameLogic/GameApp.cs`

### 6.1 加载机制（非 Editor 真机）

```
ProcedureLoadAssembly.OnEnter
  → LoadAssembly()
       if _setting.Enable && PlayMode != EditorSimulateMode:   # 真机/打包模式
         foreach hotUpdateDllName in _setting.HotUpdateAssemblies:   # 本任务已含 GameBattle.dll
           assetLocation = hotUpdateDllName                      # AddressByFileName → 地址 = "GameBattle.dll"
           result = await _resourceModule.LoadAssetAsync<TextAsset>(assetLocation)  # YooAsset 加载
           LoadAssetSuccess(result)
       else:                                                    # Editor / 未启用 HybridCLR
         GetMainLogicAssembly()                                  # 直接从 AppDomain 已加载程序集查找
  → LoadAssetSuccess(textAsset):
       assembly = Assembly.Load(textAsset.bytes)                # ← 非 Editor 加载点
       if assetName == LogicMainDllName: _mainLogicAssembly = assembly
       _hotfixAssemblyList.Add(assembly)
       _resourceModule.UnloadAsset(textAsset)                   # ← Launcher 在 Load 后释放 DLL TextAsset（决策 0.11）
  → AllAssemblyLoadComplete():
       appType = _mainLogicAssembly.GetType("GameApp")          # GameLogic.dll 中的 GameApp
       entryMethod = appType.GetMethod("Entrance")
       entryMethod.Invoke(appType, new object[]{ new object[]{ _hotfixAssemblyList } })
```

### 6.2 GameApp.Entrance（热更主入口）

`GameApp.cs:25-34`：

```csharp
public static void Entrance(object[] objects)
{
    GameEventHelper.Init();                          // 1. 最先调用（task 1.6 唯一初始化入口）
    _hotfixAssembly = (List<Assembly>)objects[0];    // 2. 保存热更程序集列表（含 GameBattle）
    Utility.Unity.AddDestroyListener(Release);       // 3. 注册销毁回调
    StartGameLogic();                                // 4. 启动游戏逻辑
}
```

### 6.3 GameBattle 在非 Editor 的加载结论

- GameBattle.dll 经 `UpdateSetting.HotUpdateAssemblies` 列表（本任务已补）触发 `LoadAssetAsync<TextAsset>("GameBattle.dll")`。
- YooAsset Collector 的 DLL 组（§4）提供该资源地址。
- `Assembly.Load(textAsset.bytes)` 加载后加入 `_hotfixAssemblyList`，传入 `GameApp.Entrance`。
- **加载顺序由 `HotUpdateAssemblies` 列表顺序决定**：GameProto → GameCommon → GameFUI → GamePlay → GameBattle → GameLogic。GameBattle 在 GameLogic 之前加载，GameLogic（主入口）最后加载并反射调用 Entrance——符合拓扑。
- Editor 模式（`PlayMode == EditorSimulateMode` 或未启用 HybridCLR）走 `GetMainLogicAssembly()`，从 AppDomain 已编译程序集直接查找，不经 `Assembly.Load`；GameBattle 程序集经 asmdef references（本任务 GameLogic→GameBattle 已补）可解析。
- **spike 未跑真机**：机制已记录，真机验证留待 task 8.6（非 Editor HybridCLR 构建）。

### 6.4 资源所有权（决策 0.11 对齐）

- Launcher（`ProcedureLoadAssembly`）在 `Assembly.Load` 后立即 `_resourceModule.UnloadAsset(textAsset)` 释放 DLL TextAsset —— DLL bytes 不由战斗持有。
- `_hotfixAssemblyList` 保存的是 `Assembly` 引用（元数据，非 TextAsset），GameApp 持有。
- BattleRuntime 只持有不可变配置快照；BattleModule 只释放自己加载的 Scene/FUI 租约/Prefab/战斗专属句柄。不重复释放 DLL TextAsset。

## 7. HybridCLR / AOT 元数据 spike

源码：`ProjectSettings/HybridCLRSettings.asset`

本任务前 `hotUpdateAssemblies`：`GameProto`、`GameLogic`。
本任务后（按拓扑顺序）：

```yaml
hotUpdateAssemblies:
- GameProto
- GameCommon
- GameFUI
- GamePlay
- GameBattle
- GameLogic
```

- `hotUpdateAssemblyDefinitions: []`（空，用名字列表而非 asmdef 引用）——**未改**。
- `patchAOTAssemblies`（AOT 元数据 dll 列表：mscorlib/System/System.Core/TEngine.Runtime/YooAsset/UniTask/UnityEngine.CoreModule）——**未改**。这些是 AOT 程序集，需补充元数据的是它们，不是热更 dll。
- `outputAOTGenericReferenceFile: HybridCLRGenerate/AOTGenericReferences.cs`——GameBattle 新增 `List/Dictionary/UniTask/Action` 自定义泛型组合的 AOT 补充由 task 8.5 在目标平台 profile 后处理（见 hotfix-workflow.md “AOT 泛型补充”）。
- **本任务不新增 AOT 元数据占位**：GameBattle 尚无生产代码，无已知泛型组合；待 Phase 1-6 实现后由 task 8.5 补充。

## 8. 修改文件清单

本任务修改（3 项）：

1. `Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef`——新增 GameBattle reference（`GUID:52382661ab6e0e243961d4e43bbac1ca`）。
2. `Assets/TEngine/Settings/UpdateSetting.asset`——HotUpdateAssemblies 补 GameCommon/GameFUI/GamePlay/GameBattle 4 项。
3. `ProjectSettings/HybridCLRSettings.asset`——hotUpdateAssemblies 补同名 4 项。

新建（1 项）：

4. `docs/HotFix架构/hotfix-topology-spike-record.md`（本文件）。

未碰：GameBattle.asmdef（1.6）、AssemblyInfo.cs（1.12）、EventSpike.cs（1.6）、GameBattle.Tests.asmdef/SmokeTest.cs（1.12）、GameProto/GameCommon/GameFUI/GamePlay asmdef、AssetBundleCollector（.asset/.xml）、Origin/、openspec/ artifacts、CSV、总纲.md/module-list.md（1.9）、任何 .cs 业务代码。

## 9. 对后续 task 的输入

- **task 2.1**：GameBattle.asmdef 最小依赖已就绪（1.6 完成）；UpdateSetting/HybridCLR/DLL 复制/YooAsset 配置已由本 task 补齐。task 2.1 只需确认 GameLogic 最后加载、无循环（本 task 已验证）。
- **task 2.7**：GameLogic→GameBattle asmdef reference 已补，HotFixModules.cs 注册 BattleModule 可编译。
- **task 8.5**：GameBattle 新增泛型组合的 AOT 元数据补充——本 task 未占位，待生产代码确定后补充。
- **task 8.6**：非 Editor HybridCLR 构建验证——本 task 记录了 Assembly.Load 机制，真机验证留待 8.6。
- **integrate-fairygui-module task 7.7**：该 task 审查“未修改 UpdateSetting/HybridCLR/DLL 复制”——本 task **确实修改了** UpdateSetting/HybridCLRSettings（补 4 程序集）。这是跨 change 协调点：fairygui change 的“未修改”断言在其 change 范围内成立（它自己没改），但本 change 在其后扩展了这些配置。fairygui change 审查时需知晓此扩展不破坏其 FUI 收集组与 GameFUI asmdef。

## 10. 已知风险

- **真机未跑**：Assembly.Load 机制为静态分析结论，未在非 Editor 构建实测。task 8.6 需验证 GameBattle.dll 加载、GameApp.Entrance 反射调用、BattleModule 注册。
- **AOT 元数据未补**：GameBattle 生产代码尚未实现，无已知泛型组合；task 8.5 需在目标平台补充并验证无 `ExecutionEngineException`。
- **`.asset` 与 `.xml` 不同步**：AssetBundleCollector 的 `.asset` 缺 FUI 组（fairygui 遗留），`.xml` 有。YooAsset 以 `.xml` 为源，不影响 DLL 组；但建议 fairygui change 或后续同步两者。
- **跨 change 协调**：UpdateSetting/HybridCLRSettings 补了 GameFUI/GamePlay，虽不修改 fairygui 文件，但 GameFUI 真机加载现在依赖本配置。若 fairygui change 先归档，其“未修改共享热更配置”断言仍成立；若本 change 先归档，fairygui change 需确认 GameFUI.dll 已在热更列表（本 task 已加）。
- **GamePlay 空 asmdef**：GamePlay 当前无代码、references 为空。加入热更列表后构建会生成空 GamePlay.dll——无害，但待 GamePlay 有代码时需补 asmdef references。
