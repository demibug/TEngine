# 资源管理模式与生命周期

> **适用场景**：OnRefresh/OnDestroy 内资源生命周期管理、资源泄漏根因分析、CancellationToken 取消模式、多资源包协调 | **关联文档**：[resource-api.md](resource-api.md)（核心 API）、[ui-lifecycle.md](ui-lifecycle.md)（UI 内释放时机）

> 本文档是 [resource-api.md](resource-api.md) 的进阶补充，聚焦于资源生命周期管理模式和常见泄漏根因。

---

## 一、UIWindow 内资源生命周期

### 模式：OnRefresh 加载 + OnDestroy 释放

```csharp
[Window(UILayer.UI, "HeroDetailUI")]
public class HeroDetailUI : UIWindow
{
    private TextAsset _heroConfig;
    private CancellationTokenSource _cts = new();

    protected override async void OnRefresh()
    {
        // 每次 ShowUI 重新加载（userData 决定加载哪个）
        int heroId = (int)UserData;

        // 取消上一次未完成的加载
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        // 取消或失败时返回 null（不抛 OperationCanceledException），按 null 判断即可
        var heroConfig = await GameModule.Resource.LoadAssetAsync<TextAsset>(
            $"hero_config_{heroId}", _cts.Token);
        if (heroConfig == null)
        {
            return; // 已取消或加载失败：不赋值、不使用
        }

        _heroConfig = heroConfig;
        // 使用数据...
    }

    protected override void OnDestroy()
    {
        _cts.Cancel();
        _cts.Dispose();
        if (_heroConfig != null)
        {
            GameModule.Resource.UnloadAsset(_heroConfig);
            _heroConfig = null;
        }
    }
}
```

**关键点**：
- `OnRefresh` 每次 ShowUI 都执行，多次打开同一窗口需先取消上次加载
- 取消传参给 `LoadAssetAsync` 后由框架归还该份引用，业务层只需在返回 null 时跳过赋值
- `OnDestroy` 是释放的唯一时机（生命周期回调中不存在 `OnClose`；`UIWindow.Close()` 只是发起关闭的动作方法，关闭最终仍走 `OnDestroy`）
- `CancellationTokenSource` 本身也需要 `Dispose`

---

### 模式：OnCreate 加载一次 + OnDestroy 释放

适合：整个窗口生命周期内使用同一份资源（如背景音乐、固定配置）。

```csharp
[Window(UILayer.UI, "BattleMainUI", fullScreen: true)]
public class BattleMainUI : UIWindow
{
    private AudioClip _bgmClip;

    protected override async void OnCreate()
    {
        _bgmClip = await GameModule.Resource.LoadAssetAsync<AudioClip>("battle_bgm");
        GameModule.Audio.Play(AudioType.Music, "battle_bgm", bLoop: true);
    }

    protected override void OnDestroy()
    {
        GameModule.Audio.Stop(AudioType.Music, fadeout: false);
        if (_bgmClip != null)
        {
            GameModule.Resource.UnloadAsset(_bgmClip);
            _bgmClip = null;
        }
    }
}
```

---

### 模式：LoadGameObjectAsync 实例 Destroy 即归还

`LoadGameObjectAsync` 返回的实例自带 `AssetsReference` 组件并绑定源 Prefab 的那份 spawn，销毁实例即自动归还，业务层只需 `Object.Destroy`：

```csharp
[Window(UILayer.UI, "EffectPlayUI")]
public class EffectPlayUI : UIWindow
{
    private GameObject _effectGo;

    private async void PlayEffect()
    {
        _effectGo = await GameModule.Resource.LoadGameObjectAsync("Fx_Hit", rectTransform, _cts.Token);
        if (_effectGo == null)
        {
            return; // 已取消或加载失败
        }
    }

    private void RemoveEffect()
    {
        if (_effectGo != null)
        {
            Object.Destroy(_effectGo);   // AssetsReference.OnDestroy 自动归还源 Prefab 引用
            _effectGo = null;
        }
    }
}
```

**关键点**：
- 释放链路：`Object.Destroy(instance)` → `AssetsReference.OnDestroy` → `UnloadAsset(源 Prefab)`；业务层不要再对实例手动 `UnloadAsset`（同一份 spawn 会被重复归还）
- 对实例做 `Instantiate` 克隆不继承持有权，克隆体销毁不会触发归还
- 从未激活即被销毁的实例不保证触发 `OnDestroy`，ResourceModule 会轮询"伪 null"实例做一次性兜底归还，无需业务补偿

---

## 二、UIWidget 内资源生命周期

Widget 的生命周期与父 Window 绑定，资源释放在 Widget.OnDestroy 中：

```csharp
public class ItemWidget : UIWidget
{
    private Image _imgIcon;
    private Text _textName;

    protected override void ScriptGenerator()
    {
        _imgIcon  = FindChildComponent<Image>("m_img_Icon");
        _textName = FindChildComponent<Text>("m_text_Name");
    }

    // SetSprite 内置缓存池，无需手动释放
    public void SetData(ItemConfig cfg)
    {
        _textName.text = cfg.Name;
        _imgIcon.SetSprite(cfg.IconPath);
    }

    // Widget 不持有 LoadAssetAsync 的 Asset，无需释放
    // 若持有，必须在 OnDestroy 中释放
}
```

**注意**：Widget 的 `OnDestroy` 先于父 Window 的 `OnDestroy` 执行。若父 Window 需要在 Widget 销毁后访问其资源，会出现空引用。

---

## 三、场景切换资源整理

### 完整场景切换流程

```csharp
public async UniTask SwitchToBattleScene()
{
    // 1. 关闭当前所有 UI（触发各 UIWindow.OnDestroy 释放资源）
    GameModule.UI.CloseAll();

    // 2. 加载新场景（Single 模式自动卸载旧场景）
    await GameModule.Scene.LoadSceneAsync("BattleScene", LoadSceneMode.Single,
        progressCallBack: p => { /* 显示进度条 */ });

    // 3. 打开新 UI
    GameModule.UI.ShowUIAsync<BattleMainUI>();
}
```

**关键点**：
- **主场景（Single）加载完成后框架自动整理**：`LoadSceneAsync` 内部在场景加载完成时自动调用 `ForceUnloadUnusedAssets(gcCollect)`（`gcCollect` 参数默认 `true`），由 ResourceModuleDriver 在后续 Update 中执行 `Resources.UnloadUnusedAssets()` 并跟随 `GC.Collect()`（默认 `UseSystemUnloadUnusedAssets=true` 时还会同步触发 `ResourceModule.UnloadUnusedAssets()` 归还池中未使用对象），无需手动调用。
- `ForceUnloadUnusedAssets(bool performGCCollect)` 是异步标志位触发，不是同步立即执行；仅在场景加载路径之外需要额外整理时手动调用。

### 叠加场景的资源管理

```csharp
// 叠加场景加载
await GameModule.Scene.LoadSceneAsync("MinigameScene", LoadSceneMode.Additive);

// 卸载叠加场景时显式清理
// 注意：UnloadAsync（子场景卸载）不会自动触发资源整理，与主场景加载不同
await GameModule.Scene.UnloadAsync("MinigameScene");
GameModule.Resource.UnloadUnusedAssets();   // 清理叠加场景释放出的资源
```

---

## 四、跨模块资源共享

### 反模式：静态变量持有 Asset 引用

```csharp
// ❌ 危险：静态引用导致资源永不释放，内存持续增长
public class IconManager
{
    private static Dictionary<int, Sprite> _cache = new();

    public static async UniTask<Sprite> GetIcon(int iconId)
    {
        if (!_cache.ContainsKey(iconId))
        {
            // LoadAssetAsync<Sprite> 本身就是错误用法，应用 SetSprite
            // 但即使换成正确方式，静态缓存也会泄漏
            var sprite = await GameModule.Resource.LoadAssetAsync<Sprite>($"icon_{iconId}");
            _cache[iconId] = sprite;
        }
        return _cache[iconId];
    }
    // 没有 UnloadAsset，资源永远不释放
}

// ✅ 正确：Sprite 直接用 SetSprite，框架内置缓存池自动管理
_imgIcon.SetSprite($"icon_{itemCfg.IconId}");
```

### 模式：共享配置数据通过 ConfigSystem

```csharp
// ✅ 正确：Luban 配置数据通过 ConfigSystem 统一管理
var itemCfg = ConfigSystem.Instance.Tables.TbItem.Get(itemId);

// 不要将配置数据缓存到自己的静态变量中
// 配置数据由 ConfigSystem 统一持有：内部经同步 LoadAsset 载入 .bytes 后全程持有，
// 不做 UnloadAsset，业务层也无需（不应）对其释放
```

> 注意：`ConfigSystem.cs` 不在 Assets 默认目录中，由生成脚本从仓库根 `Configs/GameConfig/CustomTemplate/ConfigSystem.cs` 复制到 `Assets/GameScripts/HotFix/GameProto/`，首次导表后才出现。详见 [luban-config.md](luban-config.md)。

---

## 五、常见资源泄漏根因

### 泄漏类型1：OnDestroy 中未释放

```
症状：内存随操作次数线性增长，资源对象池中未使用对象持续堆积
根因：LoadAssetAsync 有调用，但 UnloadAsset 缺失或条件分支遗漏
      （UnloadAsset 契约是"一份成功加载对应一次归还"，缺失即 spawn 滞留池中）
排查：Debugger 组件（ActiveWindowType 控制显隐，共 4 值：AlwaysOpen/OnlyOpenWhenDevelopment/OnlyOpenInEditor/AlwaysClose）
      → Profiler/Object Pool 窗口查看 Asset Pool 的对象数量与引用计数（SpawnCount），
      或用 Profiler/Memory/* 窗口采样定位滞留的 Unity 对象
```

### 泄漏类型2：异步加载后对象已销毁

```csharp
// ❌ 危险：await 期间窗口关闭，OnDestroy 早于 LoadAssetAsync 完成
// 导致 OnDestroy 中 _config == null 跳过释放，await 完成后赋值泄漏

protected override async void OnRefresh()
{
    _config = await GameModule.Resource.LoadAssetAsync<TextAsset>("config");
    // 若此时窗口已销毁，_config 赋值但 OnDestroy 已执行，资源泄漏
}

// ✅ 正确：使用 CancellationToken（UIWindow/UIWidget 是普通 C# 类，
// this == null 永远为 false，不能用它判断窗口是否销毁）
protected override async void OnRefresh()
{
    // 取消或失败时 LoadAssetAsync 返回 null（框架统一以 null 结束取消的请求，
    // 并自动归还该份引用，不抛 OperationCanceledException）
    var config = await GameModule.Resource.LoadAssetAsync<TextAsset>("config", _cts.Token);
    if (config == null)
    {
        return; // 已取消或加载失败：不赋值、不刷新 UI
    }

    _config = config;
    RefreshUI();
}
```

### 泄漏类型3：Widget 列表重建不释放旧资源

```csharp
// ❌ 错误：每次刷新都创建新 Widget，旧 Widget 持有的资源未释放
protected override void OnRefresh()
{
    _skillSlots.Clear();                           // 只清列表，Widget 未销毁
    for (int i = 0; i < skills.Count; i++)
    {
        var slot = CreateWidget<SkillSlotWidget>($"Slot_{i}");
        _skillSlots.Add(slot);
    }
}

// ✅ 正确：用 AdjustIconNum 复用 Widget，或手动销毁旧 Widget
// AdjustIconNum 内部会销毁多余的 Widget 并复用已有的
AdjustIconNum<SkillSlotWidget>(_skillSlots, skills.Count, _tfSkillPanel, _slotPrefab);
for (int i = 0; i < _skillSlots.Count; i++)
    _skillSlots[i].SetData(skills[i]);
```

---

## 六、多资源包场景

### packageName 参数用法

```csharp
// DLC 或分包资源需要指定 packageName
var bossAsset = await GameModule.Resource.LoadGameObjectAsync(
    "BossPrefab", parent, packageName: "DLC_Chapter2");

// 检查资源是否在包内
bool valid = GameModule.Resource.CheckLocationValid("BossPrefab", "DLC_Chapter2");
```

### 分包资源下载前置检查

```csharp
public async UniTask EnsureDLCReady(string packageName)
{
    // 检查资源状态
    // HasAssetResult 含义：Valid=location 无效 / NotExist=资源不存在 /
    // AssetOnline=需从远端下载 / AssetOnDisk=本地已有
    // （另有 AssetOnFileSystem/BinaryOnDisk/BinaryOnFileSystem 等文件系统/二进制变体）
    var result = GameModule.Resource.HasAsset("BossPrefab", packageName);
    if (result == HasAssetResult.AssetOnDisk) return;   // 本地已有，直接使用

    // 触发下载（参考 hotfix-workflow.md 的下载流程）
    // 注意：该重载创建的下载器针对整个包裹版本的差量文件，而非单个资源
    var downloader = GameModule.Resource.CreateResourceDownloader(packageName);
    if (downloader.TotalDownloadCount > 0)
    {
        downloader.BeginDownload();
        await downloader;   // UniTask 的 YooAsset 扩展（UniTask.YooAsset.asmdef 定义 UNITASK_YOOASSET_SUPPORT）提供 GetAwaiter；Failed 时会抛异常（ProcedureDownloadFile.cs 用法）
        // 若需自行处理失败状态而不抛异常，可用轮询：while (!downloader.IsDone) { await UniTask.Yield(); }（ProcedureTwoStageUpdate.cs 用法）
    }
}
```

**注意**：不要用 `HasAssetResult.Valid` 判断"本地已有"——`Valid` 表示资源定位地址无效（location 未收集进清单），与资源是否存在无关。

---

## 七、资源加载性能模式

### 并发批量加载（同类型）

```csharp
// 批量加载多个配置，并发执行
var locations = new[] { "level_1_data", "level_2_data", "level_3_data" };
var configs = await UniTask.WhenAll(
    locations.Select(loc => GameModule.Resource.LoadAssetAsync<TextAsset>(loc)));

// 释放时逐一 Unload
foreach (var config in configs)
    GameModule.Resource.UnloadAsset(config);
```

### 预加载策略

```csharp
// PRELOAD 标签资源在 ProcedurePreload 阶段统一加载（WebGL 平台另有 WEBGL_PRELOAD 标签；
// EditorSimulateMode 下跳过预热）
// 业务代码使用时可同步加载（已在内存中）：
var config = GameModule.Resource.LoadAsset<TextAsset>("hero_config_common");

// 注意：同步加载若撞上同 location 未完成的异步加载会直接抛异常，
// 仅在确认无并发异步加载同地址时使用同步入口
// 非 PRELOAD 资源必须异步加载，避免主线程卡顿
var rareAsset = await GameModule.Resource.LoadAssetAsync<TextAsset>("rare_boss_config");
```

---

## 交叉引用

| 主题 | 文档 |
|------|------|
| 资源加载核心 API（SetSprite/LoadGameObjectAsync/LoadAssetAsync）| [resource-api.md](resource-api.md) |
| UIWindow 生命周期（OnCreate/OnRefresh/OnDestroy）| [ui-lifecycle.md](ui-lifecycle.md) |
| UIWidget 创建与列表管理 | [ui-patterns.md](ui-patterns.md) |
| Luban 配置数据访问 | [luban-config.md](luban-config.md) |
| 问题排查（内存增长、location 无效）| [troubleshooting.md](troubleshooting.md) |
