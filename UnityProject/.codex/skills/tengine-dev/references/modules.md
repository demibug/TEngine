# TEngine 模块 API 速查

> **适用场景**：使用 GameModule.Timer/Scene/Audio/Fsm 等模块，以及 `MemoryPool`/`Log`/`Utility` 等静态类 | **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（UI 模块）、[resource-api.md](resource-api.md)（Resource 模块）、[event-system.md](event-system.md)（事件模块）

## 核心 API：GameModule 统一访问入口

常用框架模块通过 `GameModule` 静态类访问（已缓存），禁止重复 `ModuleSystem.GetModule<T>()`（另有 ObjectPool/GameEvent/Settings 等入口不经 GameModule 暴露，见下文）：

```csharp
GameModule.Base          // RootModule          — 根模块（框架初始化入口）
GameModule.Debugger      // IDebuggerModule     — 调试器（IMGUI 悬浮图标点击开合）
GameModule.Fsm           // IFsmModule          — 有限状态机
GameModule.Procedure     // IProcedureModule    — 流程
GameModule.Resource      // IResourceModule     — 资源加载
GameModule.Audio         // IAudioModule        — 音频
GameModule.UI            // UIModule            — UI 管理
GameModule.FGUI          // FguiModule          — FairyGUI 独立模块（UGUI 仍走 GameModule.UI）
GameModule.Scene         // ISceneModule        — 场景
GameModule.Timer         // ITimerModule        — 计时器
GameModule.Localization  // ILocalizationModule — 本地化

GameModule.Shutdown()   // 先关闭 FGUI 模块，再清空所有模块缓存引用，仅在游戏退出时调用
```

> **注意**：
> - `UI` 属性类型是 `UIModule`（单例，返回 `UIModule.Instance`），不是 `IUIModule`；`FGUI` 同理是 `FguiModule.Instance`。
> - `Base` 属性通过 `FindObjectOfType<RootModule>()` 获取；`UI`/`FGUI` 通过各自单例 `Instance` 获取；其余接口模块通过 `ModuleSystem.GetModule<T>()` 获取。
> - 所有属性带 `ModuleSystem.IsRunning` 守卫：模块系统关闭后访问任何属性**直接返回 null**（而非重新查找）。

---

## 使用模式

### TimerModule 计时器

回调委托签名：`public delegate void TimerHandler(object[] args)`（args 来自 AddTimer 的可变参数，用于避免闭包）。

```csharp
// 添加计时器（回调在前，时间在后）
int tid = GameModule.Timer.AddTimer(OnTick, time: 3f);                         // 单次
int tid = GameModule.Timer.AddTimer(OnTick, time: 1f, isLoop: true);           // 循环
int tid = GameModule.Timer.AddTimer(OnTick, time: 5f, isUnscaled: true);       // 非缩放时间
int tid = GameModule.Timer.AddTimer(OnTick, time: 2f, false, false, myArg);    // 带参（避免闭包）
void OnTick(object[] args) { }                                                 // 回调签名

// 重置（可换回调/参数）
GameModule.Timer.ResetTimer(timerId, OnTick, 1f, isLoop: true);

// 控制
GameModule.Timer.Stop(timerId);         // 暂停
GameModule.Timer.Resume(timerId);       // 恢复
GameModule.Timer.Restart(timerId);      // 重置到开始状态
GameModule.Timer.RemoveTimer(timerId);  // 移除（销毁时必须调用）
GameModule.Timer.RemoveAllTimer();      // 清除所有

// 查询
float left = GameModule.Timer.GetLeftTime(timerId);
bool running = GameModule.Timer.IsRunning(timerId);

// 系统 Timer（System.Timers.Timer，固定 1 秒间隔，独立于帧驱动，模块关闭时统一销毁）
System.Timers.Timer sysTimer = GameModule.Timer.AddSystemTimer((sender, e) => { });
```

> 模块已关闭 / 框架非运行状态（`ModuleSystem.IsRunning == false`）/ 回调为 null 时 `AddTimer` 返回 0（无效 Id）。

### SceneModule 场景管理

```csharp
// 加载（完整签名：LoadSceneAsync(location, sceneMode, suspendLoad, priority, gcCollect, progressCallBack)）
Scene scene = await GameModule.Scene.LoadSceneAsync("SceneName");
Scene scene = await GameModule.Scene.LoadSceneAsync("SceneName", LoadSceneMode.Additive);
Scene scene = await GameModule.Scene.LoadSceneAsync("SceneName", LoadSceneMode.Single,
    progressCallBack: p => { /* 0~1 */ });

// 卸载/激活/查询
bool ok = await GameModule.Scene.UnloadAsync("SceneName");     // 仅卸载子场景
GameModule.Scene.ActivateScene("SceneName");                   // 多场景时切换激活场景
bool has = GameModule.Scene.IsContainScene("SceneName");
bool isMain = GameModule.Scene.IsMainScene("SceneName");
string main = GameModule.Scene.CurrentMainSceneName;
```

> `UnloadAsync` 文档语义为"异步卸载子场景"，主场景随流程切换卸载。另有回调版 `LoadScene`/`Unload` 与挂起解除 `UnSuspend`。

### AudioModule 音频

`AudioType` 枚举：`Sound`（音效）、`UISound`（UI 音效）、`Music`（音乐）、`Voice`（语音）、`Max`（哨兵值，勿用于播放）。

```csharp
// 播放（完整签名：Play(type, path, bLoop, volume, bAsync, bInPool)，返回 AudioAgent）
AudioAgent agent = GameModule.Audio.Play(AudioType.Music, "bgm_path", bLoop: true);   // BGM
AudioAgent agent = GameModule.Audio.Play(AudioType.Sound, "sfx_path");                  // 音效
AudioAgent agent = GameModule.Audio.Play(AudioType.UISound, "ui_click", bAsync: true);  // UI

// 停止 / 重启
GameModule.Audio.Stop(AudioType.Music, fadeout: true);
GameModule.Audio.StopAll(fadeout: false);
GameModule.Audio.Restart();              // 重启音频模块（重新初始化各分类）

// 音量（0~1）与开关
GameModule.Audio.Enable        = true;  // 总开关
GameModule.Audio.Volume        = 1.0f;  // 总音量
GameModule.Audio.MusicVolume   = 0.8f;  GameModule.Audio.MusicEnable   = true;
GameModule.Audio.SoundVolume   = 1.0f;  GameModule.Audio.SoundEnable   = true;
GameModule.Audio.UISoundVolume = 1.0f;  GameModule.Audio.UISoundEnable = true;
GameModule.Audio.VoiceVolume   = 1.0f;  GameModule.Audio.VoiceEnable   = true;

// 音频池（预加载 AudioClip，避免播放时加载卡顿）
GameModule.Audio.PutInAudioPool(new List<string> { "clip_path" });
GameModule.Audio.RemoveClipFromPool(new List<string> { "clip_path" });  // 移出部分
GameModule.Audio.CleanSoundPool();
```

> 音频模块懒加载创建时由 `OnInit` 自动调用 `Initialize(Settings.AudioSetting.audioGroupConfigs)` 完成初始化，一般无需手动调用。

### FsmModule 有限状态机

```csharp
// 定义状态（生命周期回调：OnInit/OnEnter/OnUpdate/OnLeave/OnDestroy，均为 protected internal virtual）
public class IdleState : FsmState<MyOwner>
{
    private IFsm<MyOwner> _fsm;  // 需持有 fsm 引用用于切换状态

    protected override void OnInit(IFsm<MyOwner> fsm) { _fsm = fsm; }
    protected override void OnEnter(IFsm<MyOwner> fsm) { }
    protected override void OnUpdate(IFsm<MyOwner> fsm, float elapseSeconds, float realElapseSeconds) { }
    protected override void OnLeave(IFsm<MyOwner> fsm, bool isShutdown) { }
    protected override void OnDestroy(IFsm<MyOwner> fsm) { }
}

// 创建并启动（名称可省略：CreateFsm(owner, states)）
IFsm<MyOwner> fsm = GameModule.Fsm.CreateFsm<MyOwner>("FsmName", owner,
    new IdleState(), new RunState(), new AttackState());
fsm.Start<IdleState>();

// 状态间切换：在 FsmState 派生类内部调用基类 protected 方法，传入 fsm
ChangeState<RunState>(_fsm);

// 传数据
fsm.SetData<int>("Key", value);
int val = fsm.GetData<int>("Key");

// 查询
bool has = GameModule.Fsm.HasFsm<MyOwner>("FsmName");   // 另有 GetFsm<T>/GetAllFsms
fsm.HasData("Key");                                     // 状态机数据是否存在

// 销毁（返回 bool）
bool ok = GameModule.Fsm.DestroyFsm<MyOwner>("FsmName");
```

> **切换状态注意**：`IFsm<T>` 接口上没有公开的 `ChangeState`（实现是 internal），只能在状态类内部通过 `FsmState<T>.ChangeState<TState>(fsm)` 切换，不能在状态类外部调用。

### MemoryPool 内存池

频繁创建/销毁的纯 C# 对象，避免 GC（`MemoryPool` 是静态类，非 GameModule 属性）：

```csharp
public class DamageInfo : IMemory   // 约束：class + IMemory + new()（需无参构造）
{
    public int Damage;
    public void Clear() { Damage = 0; }  // 归还时重置
}

var info = MemoryPool.Acquire<DamageInfo>();
info.Damage = 100;
MemoryPool.Release(info);  // Release 后禁止再访问，禁止 Release 两次
```

### ObjectPoolModule 对象池

需要引用计数/容量/过期策略的池（池元素约束 `T : ObjectBase`），不经 GameModule 暴露：

```csharp
var module = ModuleSystem.GetModule<IObjectPoolModule>();
IObjectPool<MyAssetObject> pool =
    module.CreateSingleSpawnObjectPool<MyAssetObject>("PoolName", expireTime: 60f);

MyAssetObject obj = pool.Spawn();   // 取出（引用计数 +1）
pool.Unspawn(obj);                  // 归还
bool ok = pool.ReleaseObject(obj);  // 释放单个对象
```

> 纯 C# 数据对象用 `MemoryPool`（只需 `IMemory`）；需管理 Unity 对象、容量上限或自动过期时用 `ObjectPoolModule`。

### Log 日志系统

各级别由 `ENABLE_LOG`/`ENABLE_DEBUG_LOG`/`ENABLE_INFO_AND_ABOVE_LOG` 等预编译宏控制（`[Conditional]`，打包时按宏裁剪）：

```csharp
Log.Debug("调试信息");                   // 受 ENABLE_LOG/ENABLE_DEBUG_LOG/ENABLE_DEBUG_AND_ABOVE_LOG 控制
Log.Info("普通信息");                    // 受 ENABLE_LOG/ENABLE_INFO_LOG/ENABLE_DEBUG_AND_ABOVE_LOG/ENABLE_INFO_AND_ABOVE_LOG 控制
Log.Warning("警告");                     // 受 ENABLE_LOG/ENABLE_WARNING_LOG/ENABLE_DEBUG_AND_ABOVE_LOG/ENABLE_INFO_AND_ABOVE_LOG/ENABLE_WARNING_AND_ABOVE_LOG 控制
Log.Error("错误");                       // 受 ENABLE_LOG/ENABLE_ERROR_LOG/ENABLE_INFO_AND_ABOVE_LOG/ENABLE_WARNING_AND_ABOVE_LOG/ENABLE_ERROR_AND_ABOVE_LOG 等宏控制
Log.Fatal("严重错误");                    // Fatal(string) 同样受宏控制，仅 Fatal(Exception) 重载不受
Log.Assert(condition, "断言失败提示");    // 失败时按 Fatal 输出堆栈（受 ENABLE_LOG 控制）
```

### LocalizationModule 本地化

```csharp
// 设置当前语言（枚举/字符串/语言Id 三种重载；load 为 true 时立即加载语言资源）
bool ok = GameModule.Localization.SetLanguage(Language.ChineseSimplified, load: true);

// 加载语言分表（字符串为语言总表 CSV 中的语言名称；框架内部把 Language.ChineseSimplified 映射为 "Chinese"，
// setCurrent 为 true 时加载后设为当前语言）
await GameModule.Localization.LoadLanguage("Chinese");

// 查询（字符串同样需与语言总表中的名称一致）
bool loaded = GameModule.Localization.CheckLanguage("Chinese");
Language sys = GameModule.Localization.SystemLanguage;   // 系统语言
GameModule.Localization.Language = Language.ChineseSimplified;  // 直接赋值当前语言
```

> `Language` 枚举（`Language.ChineseSimplified` 等）与分表名称需与语言资源对应；本地化资源加载同样走 `GameModule.Resource`。

### GameEvent 事件

模块间解耦的事件静态类（不经 GameModule 暴露，支持 int / string 事件名与 0~6 个参数）：

```csharp
GameEvent.AddEventListener<int>("HpChangeEvent", OnHpChange);     // 订阅（返回 bool）
GameEvent.Send("HpChangeEvent", 100);                             // 触发
GameEvent.RemoveEventListener<int>("HpChangeEvent", OnHpChange);  // 退订（防泄漏必须调用）
void OnHpChange(int hp) { }
```

> 事件定义、`AddUIEvent` 与内存泄漏排查详见 [event-system.md](event-system.md) 与 [event-antipatterns.md](event-antipatterns.md)。

### Settings 全局配置

框架配置入口（`MonoBehaviour` 单例，场景挂载后通过 `FindObjectOfType` 获取，不经 GameModule 暴露），静态属性返回各 ScriptableObject 配置：

```csharp
AudioGroupConfig[] groups = Settings.AudioSetting.audioGroupConfigs;  // 音频轨道组（Audio 模块 OnInit 自动读取）
UpdateSetting update = Settings.UpdateSetting;                        // 热更/资源更新配置（流程模块读取）
ProcedureSetting procedure = Settings.ProcedureSetting;               // 流程配置
```

---

## 模块生命周期与自定义模块

模块基类 `Module`（抽象类）与轮询接口 `IUpdateModule`：

```csharp
public abstract class Module
{
    public virtual int Priority => 0;   // 优先级高先轮询、关闭后进行
    public abstract void OnInit();      // 首次 GetModule<T>() 时调用（懒加载）
    public abstract void Shutdown();    // ModuleSystem 关闭时调用（逆序）
}

public interface IUpdateModule
{
    void Update(float elapseSeconds, float realElapseSeconds);  // 每帧轮询（实现该接口才会被 Update）
}
```

自定义模块注册与访问：

```csharp
// 1. 定义接口与实现
public interface IMyModule { void DoWork(); }
internal class MyModule : Module, IMyModule, IUpdateModule
{
    public override void OnInit() { }
    public override void Shutdown() { }
    public void Update(float elapseSeconds, float realElapseSeconds) { }
    public void DoWork() { }
}

// 2. 注册（只能用接口类型注册；框架启动后尽早调用）
ModuleSystem.RegisterModule<IMyModule>(new MyModule());

// 3. 访问（推荐在 GameModule 中加缓存属性，模式同上）
var mod = ModuleSystem.GetModule<IMyModule>();   // 参数必须是接口，传实现类会抛异常
```

生命周期要点：

- 模块**懒加载**：首次 `ModuleSystem.GetModule<T>()` 时 `Activator.CreateInstance` 创建并立即调用 `OnInit()`。
- `ModuleSystem.Update` 由 `RootModule` 每帧驱动，只调用实现了 `IUpdateModule` 的模块。
- 关闭流程（`ModuleSystem.Shutdown`）分阶段执行：StopWork → StopProcedures → BeforeShutdown → ShutdownModules（逆序）→ ReturnResourceInstances → ShutdownObjectPools → FinalizeResources → ClearState；各阶段异常隔离并记录在 `ModuleSystem.ShutdownErrors`。
- `TryGetExistingModule<T>()` 只查找不创建，供关闭流程/晚回调使用。
- 会话状态可查询：`ModuleSystem.State`（`ModuleSystemState`：Running/ShuttingDown/Stopped）、`IsShuttingDown`、`ShutdownPhase`（`ModuleShutdownPhase` 各关闭阶段）。

其他常用入口（不经 GameModule 暴露）：

```csharp
// 帧更新驱动：协程 + Update/FixedUpdate/LateUpdate/OnDestroy 等事件注入
IUpdateDriver driver = ModuleSystem.GetModule<IUpdateDriver>();
driver.AddUpdateListener(OnFrameUpdate);

// RootModule 全局控制
GameModule.Base.FrameRate = 60;
GameModule.Base.GameSpeed = 1.0f;
GameModule.Base.PauseGame();                    // 暂停（GameSpeed = 0，可查 IsGamePaused）
GameModule.Base.ResumeGame();                   // 恢复到暂停前的速度
GameModule.Base.ResetNormalGameSpeed();         // 恢复正常速度（GameSpeed = 1）

// 流程模块（ProcedureBase 同样基于 FsmModule 驱动）
GameModule.Procedure.StartProcedure<MyProcedure>();
```

---

## 常见错误

| 错误写法 | 正确写法 | 原因 |
|---------|---------|------|
| `ModuleSystem.GetModule<ITimerModule>()` | `GameModule.Timer` | 重复查找，未利用缓存 |
| `OnDestroy` 忘记 `RemoveTimer(tid)` | 必须调用 `RemoveTimer` | 计时器回调引用已销毁对象，导致空引用 |
| `SceneManager.LoadScene()` | `GameModule.Scene.LoadSceneAsync()` | 绕过框架资源管理，热更包无法加载 |
| `GameModule.UI` 误用接口 `IUIModule` | 类型是 `UIModule`（单例实现） | 源码中 UI 属性返回 `UIModule.Instance`，非 `GetModule<T>()` |
| `Shutdown()` 后继续访问模块属性 | `Shutdown()` 仅游戏退出时调用 | `IsRunning` 守卫使所有属性直接返回 null，再调用成员方法是空引用 |
| `MemoryPool.Release()` 后访问对象 | Release 后禁止再访问 | 对象已归还池中，状态不确定 |
| `MemoryPool.Release()` 同一对象两次 | 确保只 Release 一次 | 重复归还导致池状态异常 |
| `GameModule.LoadScene` | `GameModule.Scene.LoadSceneAsync` | 不存在 `GameModule.LoadScene`，场景加载通过 `GameModule.Scene` |
| `fsm.ChangeState<RunState>()` | 状态类内 `ChangeState<RunState>(fsm)` | `IFsm<T>` 无公开 ChangeState，须用 `FsmState<T>` 基类 protected 方法 |
| `new FsmState<>()` | 继承 `FsmState<TOwner>` | 状态必须继承基类，不能直接 new |
| `GameModule.Timer.AddTimer(time, callback)` | `GameModule.Timer.AddTimer(callback, time)` | 参数顺序：回调在前，时间在后；回调签名 `void (object[] args)` |
| `ModuleSystem.GetModule<MyModule>()`（实现类） | `ModuleSystem.GetModule<IMyModule>()`（接口） | `GetModule<T>` 仅接受接口类型，传实现类抛异常 |

---

## 交叉引用

| 关联主题 | 文档 | 说明 |
|---------|------|------|
| 资源加载/卸载 | resource-api.md | `GameModule.Resource` 的完整 API 与生命周期 |
| UI 管理 | ui-lifecycle.md | `GameModule.UI` 的窗口生命周期与层级 |
| UI 进阶 | ui-patterns.md | Widget 模板与节点绑定 |
| 事件系统 | event-system.md | `GameEvent` 模块间解耦，`AddUIEvent` UI 内部事件 |
| 事件反模式 | event-antipatterns.md | 事件内存泄漏、接口无响应与事件风暴排查 |
| 热更边界 | hotfix-workflow.md | `GameModule` 所在程序集与热更边界 |
| 资源管理模式 | resource-patterns.md | 资源生命周期与模块协作 |
