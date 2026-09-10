# 资源加载核心 API

> **适用场景**：SetSprite/LoadGameObjectAsync/LoadAssetAsync 加载、UnloadAsset/UnloadUnusedAssets 卸载、热更下载 API | **底层**：YooAsset 2.3.17 | **关联文档**：[resource-patterns.md](resource-patterns.md)（生命周期模式）、[ui-lifecycle.md](ui-lifecycle.md)（窗口内资源释放时机）

## 核心原则

1. **禁止 `Resources.Load()`**：所有资源通过 YooAsset 加载，放在 `Assets/AssetRaw/` 下
2. **Sprite 用 `SetSprite` 扩展方法**：内置缓存池管理，无需手动释放
3. **GameObject 用 `LoadGameObjectAsync`**：自动管理引用计数，Destroy 时自动卸载
4. **其他 Asset 加载/释放必须配对**：`LoadAssetAsync<T>` → 用完后 → `UnloadAsset`
5. **异步优先**：禁止同步加载大资源

禁止模式详见 [naming-rules.md](naming-rules.md#禁止的代码模式)。

---

## 一、核心 API

### 资源寻址

YooAsset 通过 **location**（收集规则 `AddressByFileName` = 文件名，不含路径和扩展名）寻址：

```
Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab  →  location：BattleMainUI
Assets/AssetRaw/Audios/BGM/MainTheme.mp3        →  location：MainTheme
```

location 在整个资源包内必须唯一：同名文件会导致**构建报错**（`The address is existed`），无法用相对路径去重。当前收集器使用 `AddressByFileName` 规则（地址=文件名，不含扩展名），此规则下无法以完整路径区分同名文件；确需同名时应调整目录规划保证文件名唯一，或改用其他 AddressRule 配置（需修改收集器 `AddressRule` 设置，非开箱即用）。

### 加载方式选择

| 资源类型 | 推荐 API | 需要手动释放 |
|---------|---------|-------------|
| Sprite / 图集子图 | `SetSprite` / `SetSubSprite` | 否 |
| 需实例化的 Prefab | `LoadGameObjectAsync` | 否（Destroy 自动）|
| TextAsset / SO 等 | `LoadAssetAsync<T>` | **是** |

### SetSprite 扩展方法（4 个签名）

```csharp
// 1. Image — 基础
void SetSprite(this Image image, string location,
    bool setNativeSize = false, Action<Image> callback = null,
    CancellationToken cancellationToken = default)

// 2. SpriteRenderer
void SetSprite(this SpriteRenderer spriteRenderer, string location,
    Action<SpriteRenderer> callback = null,
    CancellationToken cancellationToken = default)

// 3. Image 图集子图（无回调重载）
void SetSubSprite(this Image image, string location, string spriteName,
    bool setNativeSize = false, CancellationToken cancellationToken = default)

// 4. SpriteRenderer 图集子图（无回调重载）
void SetSubSprite(this SpriteRenderer spriteRenderer, string location,
    string spriteName, CancellationToken cancellationToken = default)
```

> **要点**：SetSprite 的 callback 类型是 `Action<Image>` / `Action<SpriteRenderer>`（不是 `Action<Sprite>`）。SetSubSprite **没有** Action 回调重载。扩展类 `SetSpriteExtensions` 定义在**全局命名空间**，无需 using 即可调用。

使用示例：

```csharp
_imgIcon.SetSprite("item_icon_001");                                    // 基础
_imgIcon.SetSprite("item_icon_001", setNativeSize: true);               // 自适应尺寸
_imgIcon.SetSprite("item_icon_001", cancellationToken: _cts.Token);    // 取消支持
_imgIcon.SetSprite("item_icon_001", callback: img => { /* 加载完成 */ }); // 完成回调（Action<Image>）
_spriteRenderer.SetSprite("hero_sprite");                               // SpriteRenderer
_spriteRenderer.SetSprite("hero_sprite", callback: sr => { /* 加载完成 */ }); // Action<SpriteRenderer>
_imgIcon.SetSubSprite("ItemAtlas", "item_sword_01");                    // 图集子图
_imgIcon.SetSubSprite("ItemAtlas", "item_sword_01", setNativeSize: true); // 子图 + 自适应
_spriteRenderer.SetSubSprite("ItemAtlas", "hero_sword");                // SpriteRenderer 图集子图
```

禁止用 `LoadAssetAsync<Sprite>` 加载图片（无缓存池且需手动释放）。

### GameObject 加载

```csharp
// 异步（推荐）
UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null,
    CancellationToken cancellationToken = default, string packageName = "")

// 同步（资源需已在本地；首次调用会直接同步加载并入池，无需预加载）
GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")

// 回收：直接 Destroy
Destroy(go);  // 框架自动归还引用计数
```

> 同步加载失败情形：资源不在本地（HostPlayMode 未下载）返回 null；与进行中的同 key 异步加载冲突会抛 `GameFrameworkException`（应改用 await 异步版）。`LoadAsset` 同理。

禁止 `LoadAssetAsync<GameObject>` + `Instantiate` 组合（需手动追踪和 UnloadAsset）。

### Asset 加载与释放

```csharp
// 异步加载
UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default,
    string packageName = "") where T : UnityEngine.Object

// 异步加载（非泛型）
UniTask<UnityEngine.Object> LoadAssetAsync(string location, Type assetType,
    CancellationToken cancellationToken = default, string packageName = "")

// 同步加载
T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object

// 同步加载（非泛型）
UnityEngine.Object LoadAsset(string location, Type assetType, string packageName = "")

// 回调式异步加载
UniTaskVoid LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object

// 释放
void UnloadAsset(object asset)
```

### 资源卸载

```csharp
UnloadAsset(asset)                                    // 归还一份 spawn（引用计数-1）
UnloadUnusedAssets()                                  // 回收引用计数为0的资源
ForceUnloadUnusedAssets(performGCCollect: true)       // 强制整理 + 可选 GC（延迟到下一帧执行）
ForceUnloadAllAssets()                                // 强制回收所有资源（WebGL 不支持）
```

> **UnloadAsset 契约**："一份成功加载对应一次归还"——每次 `LoadAssetAsync` 成功交付一份持有，需调用一次 `UnloadAsset` 归还。它不识别调用者：同一对象被释放两次会多减一次计数，因此严禁对同一资产重复调用。

### 资源信息查询

```csharp
bool valid = CheckLocationValid(string location, string packageName = "")
HasAssetResult result = HasAsset(string location, string packageName = "")
AssetInfo info = GetAssetInfo(string location, string packageName = "")
AssetInfo[] infos = GetAssetInfos(string tag, string packageName = "")      // 按标签
AssetInfo[] infos = GetAssetInfos(string[] tags, string packageName = "")   // 按标签数组
```

`HasAssetResult` 枚举共 7 个值（按声明顺序）：`NotExist`（不存在）、`AssetOnline`（需从远端下载）、`AssetOnDisk`（在磁盘上）、`AssetOnFileSystem`（在文件系统里）、`BinaryOnDisk`、`BinaryOnFileSystem`、`Valid`（资源定位地址无效）。

### 回调式加载

传统 GameFramework 风格的回调式异步加载（支持进度回调），与 UniTask 版并存：

```csharp
void LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks,
    object userData, string packageName = "")

void LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks,
    object userData, string packageName = "")
```

`LoadAssetCallbacks` 构造时成功回调必填，可选失败/进度回调：

```csharp
GameModule.Resource.LoadAssetAsync("level_data", 0, new LoadAssetCallbacks(
    (name, asset, duration, userData) => { /* 成功 */ },
    (name, status, error, userData) => { /* 失败 */ },
    (name, progress, userData) => { /* 进度 */ }), null);
```

回调签名：成功 `(string assetName, object asset, float duration, object userData)`；失败 `(string assetName, LoadResourceStatus status, string errorMessage, object userData)`；进度 `(string assetName, float progress, object userData)`。注意：若未提供失败回调，加载失败会直接抛出 `GameFrameworkException`（框架在无失败接收者时保持异常可见）。新代码优先用 UniTask 版 `LoadAssetAsync<T>`（支持 await 与取消）。

### 句柄式加载

返回 `AssetHandle` 的加载方式，适合需要精细控制加载过程的场景：

```csharp
// 同步句柄（泛型 + 非泛型）
AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
AssetHandle LoadAssetSyncHandle(string location, Type assetType, string packageName = "")

// 异步句柄（泛型 + 非泛型）
AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
AssetHandle LoadAssetAsyncHandle(string location, Type type, string packageName = "")
```

使用示例：

```csharp
using var syncHandle = GameModule.Resource.LoadAssetSyncHandle<Sprite>("icon");
if (syncHandle.IsValid) { var sprite = syncHandle.AssetObject as Sprite; }

using var asyncHandle = GameModule.Resource.LoadAssetAsyncHandle<Sprite>("icon");
await asyncHandle;          // UniTask 的 HandleBaseExtensions 扩展支持直接 await；await asyncHandle.Task（标准 System.Threading.Tasks.Task）仅等待完成，失败不抛异常
if (asyncHandle.IsValid) { var sprite = asyncHandle.AssetObject as Sprite; }
```

> 句柄用完需 `Dispose()`（`using` 自动执行）释放底层引用；也可用 `GetAssetObject<TAsset>()` 强类型取值。注意：直接 `await` 句柄时，底层加载失败会抛出携带 `LastError` 的异常（与 `LoadAssetAsync<T>` 失败返回 null 的语义不同）。

### 预加载执行器（PreloadRequestRunner）

批量预热资源：地址去重后并发加载，每次成功回调自动归还预热自己持有的引用，资产留在资源池按正常容量/过期策略回收。

```csharp
var runner = new PreloadRequestRunner(GameModule.Resource, () => { /* 全部请求到达终态后回调一次 */ });
runner.Begin(new[] { "hero_config", "hero_voice", "hero_voice" });  // 地址集合可重复，内部去重
runner.Invalidate();  // 离开当前流程时使本代失效；晚到的旧回调仍会归还资源，但不再更新状态

// 状态：runner.Progress（0~1，空清单视为 1）、runner.AllTerminal、runner.IsEmpty、runner.FailedLocations（当前代失败地址）
```

### 场景加载（GameModule.Scene）

```csharp
// 异步加载（推荐）；gcCollect 默认 true：主场景加载完成后自动 ForceUnloadUnusedAssets
UniTask<Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single,
    bool suspendLoad = false, uint priority = 100, bool gcCollect = true,
    Action<float> progressCallBack = null)

// 回调式加载
void LoadScene(string location, LoadSceneMode sceneMode = LoadSceneMode.Single,
    bool suspendLoad = false, uint priority = 100, Action<Scene> callBack = null,
    bool gcCollect = true, Action<float> progressCallBack = null)

// 卸载子场景
UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null)
```

### 热更/下载 API

```csharp
// 获取本地资源包版本号
string GetPackageVersion(string customPackageName = "")

// 请求远端版本号
RequestPackageVersionOperation RequestPackageVersionAsync(
    bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")

// 更新资源清单
UpdatePackageManifestOperation UpdatePackageManifestAsync(
    string packageVersion, int timeout = 60, string customPackageName = "")

// 创建差量下载器
ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "")

// 按标签创建差量下载器（标签依赖闭包由当前 manifest 解析）
ResourceDownloaderOperation CreateResourceDownloaderByTags(string[] tags, string customPackageName = "")

// 清理冗余缓存文件
ClearCacheFilesOperation ClearCacheFilesAsync(
    EFileClearMode clearMode = EFileClearMode.ClearUnusedBundleFiles, string customPackageName = "")

// 清理沙盒路径所有缓存
void ClearAllBundleFiles(string customPackageName = "")

// 设置远端资源服务地址（需同时提供默认和备用地址）
void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
```

---

## 二、使用模式

### 自动管理（无需手动释放）

```csharp
// Sprite：SetSprite 内置缓存池
_imgIcon.SetSprite("item_icon_001");
_imgIcon.SetSubSprite("ItemAtlas", "item_sword_01");

// GameObject：LoadGameObjectAsync 自动引用计数
var go = await GameModule.Resource.LoadGameObjectAsync("HeroPrefab", parent);
Destroy(go);  // 框架自动归还

// 禁止 LoadAssetAsync<Sprite> 或 LoadAssetAsync<GameObject> + Instantiate
```

### 手动管理（必须配对释放）

```csharp
private TextAsset _configData;

protected override async void OnRefresh()
{
    _configData = await GameModule.Resource.LoadAssetAsync<TextAsset>("level_data");
}

protected override void OnDestroy()
{
    if (_configData != null) { GameModule.Resource.UnloadAsset(_configData); _configData = null; }
}
```

### CancellationToken 取消加载

**取消时 `LoadAssetAsync` 返回 null，不抛 `OperationCanceledException`**（取消与加载失败都表现为 null）：

```csharp
private CancellationTokenSource _cts = new();

private async UniTaskVoid LoadAsync()
{
    var asset = await GameModule.Resource.LoadAssetAsync<TextAsset>("config", _cts.Token);
    if (asset == null)
    {
        if (_cts.IsCancellationRequested) { /* 调用者取消 */ }
        else { /* 加载失败，失败原因已有日志 */ }
        return;
    }
    // 正常使用 asset ...
}

protected override void OnDestroy() { _cts.Cancel(); _cts.Dispose(); }
```

> 同一 location 的并发加载会自动合并：后到的等待者复用先到的加载，取消等待者不影响同 key 的加载者与其他等待者。

### 并发与批量加载

```csharp
// 多类型并发
var (config, go, audio) = await UniTask.WhenAll(
    GameModule.Resource.LoadAssetAsync<TextAsset>("hero_config"),
    GameModule.Resource.LoadGameObjectAsync("HeroModel"),
    GameModule.Resource.LoadAssetAsync<AudioClip>("hero_voice")
);

// 同类型批量
var configs = await UniTask.WhenAll(
    locations.Select(loc => GameModule.Resource.LoadAssetAsync<TextAsset>(loc)));
```

### 场景切换资源整理

```csharp
// LoadSceneAsync 自带 gcCollect 参数（默认 true）：加载主场景后自动 ForceUnloadUnusedAssets
await GameModule.Scene.LoadSceneAsync("BattleScene");

// 也可手动触发整理（通常无需同时使用）
GameModule.Resource.UnloadUnusedAssets();  // 回收引用计数为0的资源
GameModule.Resource.ForceUnloadUnusedAssets(performGCCollect: true);  // 强制整理+GC
```

### 多资源包

所有资源加载 API 均支持 `packageName` 可选参数（不传使用默认包 `DefaultPackage`），用于多资源包场景：

```csharp
var asset = await GameModule.Resource.LoadAssetAsync<TextAsset>("config", packageName: "DLC1");
var go = await GameModule.Resource.LoadGameObjectAsync("BossPrefab", parent, packageName: "DLC1");
```

非默认资源包使用前需先初始化（默认包由框架启动流程自动处理）：

```csharp
// 初始化指定资源包；needInitMainFest=true 时直接请求并更新清单（单机模式自定义包使用）
await GameModule.Resource.InitPackage("DLC1", needInitMainFest: true);
```

```csharp
// 等待资源模块 bootstrap 初始化完成（只等待模块自身，不代表任何资源包或清单已 ready）
await GameModule.Resource.WaitUntilInitializedAsync();
```

---

## 三、常见错误

| 错误写法 | 正确写法 | 原因 |
|---------|---------|------|
| `SetSprite("icon", callback: sprite => {})` | `SetSprite("icon", callback: img => {})` | callback 是 `Action<Image>`，不是 `Action<Sprite>` |
| `SetSubSprite("atlas", "sub", callback: ...)` | `SetSubSprite("atlas", "sub")` | SetSubSprite 无回调重载 |
| `LoadAssetAsync<Sprite>("icon")` | `_img.SetSprite("icon")` | Sprite 应使用 SetSprite，自带缓存池 |
| `LoadAssetAsync<GameObject>` + `Instantiate` | `LoadGameObjectAsync` | 后者自动管理引用计数 |
| 忘记 `UnloadAsset` | 加载/释放必须配对 | 非 GameObject 的 Asset 需手动释放 |
| `ForceUnloadUnusedAssets(gcCollection: true)` | `ForceUnloadUnusedAssets(performGCCollect: true)` | 参数名是 `performGCCollect` |
| `SetRemoteServicesUrl("https://...")` | `SetRemoteServicesUrl("https://...", "https://fallback...")` | 需同时提供默认和备用地址 |
| `OnClose()` 释放资源 | `OnDestroy()` 释放资源 | UIWindow 无 `OnClose` 方法，销毁回调是 `OnDestroy` |
| `Resources.Load<T>(path)` | `GameModule.Resource.LoadAssetAsync<T>(location)` | 禁止 Resources.Load，必须走 YooAsset |
| `StartCoroutine(LoadRoutine())` | `await GameModule.Resource.LoadAssetAsync<T>(...)` | 禁止 Coroutine，必须用 UniTask |
| `SetSpriteAsync("icon")` | `SetSprite("icon")` | 不存在 SetSpriteAsync，SetSprite 本身内部异步 |
| `ReleaseSprite("icon")` | 无需手动释放 | SetSprite 内置缓存池，无需 ReleaseSprite |
| `LoadAssetAsync<Sprite>` + 手动释放 | `_img.SetSprite("icon")` | Sprite 加载必须用 SetSprite，禁止 LoadAssetAsync<Sprite> |
| `catch (OperationCanceledException)` 处理取消 | 判断返回值是否为 null + token 状态 | 取消时 LoadAssetAsync 返回 null，不抛异常 |
| 同名文件用相对路径 `UI/BattleMainUI` 寻址 | 保证文件名全包唯一，或用完整资源路径 | location 全包内必须唯一，重复会构建报错 |

---

## 四、交叉引用

| 相关文档 | 内容 |
|---------|------|
| [ui-lifecycle.md](ui-lifecycle.md) | UIWindow 生命周期内资源释放时机 |
| [resource-patterns.md](resource-patterns.md) | 资源管理模式与生命周期进阶 |
| [event-system.md](event-system.md) | 资源加载完成的事件通知 |
| [naming-rules.md](naming-rules.md) | 禁止的代码模式 |
| [troubleshooting.md](troubleshooting.md) | 资源加载问题排查 |
