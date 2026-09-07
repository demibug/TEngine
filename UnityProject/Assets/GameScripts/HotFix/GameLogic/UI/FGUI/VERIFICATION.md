# FairyGUI integration verification record

Date: 2026-09-07. Unity target: 2022.3.62f2, Built-in Render Pipeline, legacy input.

## Completed checks

- Unity batch compilation completed successfully after the SDK/runtime/editor/test assemblies were imported. The only
  compiler warning was the pre-existing nullable annotation warning in `Module/UIModule/UIBase.cs`.
- The first four EditMode tests passed in Unity (`Logs/fgui-tests.xml`): catalog validation, shared Common-package
  ownership, failed-load rollback/retry, and one-waiter cancellation isolation.
- An authenticated Editor Play Mode run opened the existing `BattleMainUI` and the typed
  `BundleUsageFguiWindow` together. The sword/sample content was visually observed and the log reached
  `FairyGUI integration initialized and coexistence sample opened.`, which is emitted only after package dependency
  loading, generated binding, external-icon initialization, and `OnCreateAsync` complete.
- Stop and re-enter cleanup was exercised in Editor Play Mode. FairyGUI v5.2.0 clears its global package registry
  from `StageEngine.OnApplicationQuit` before TEngine's destroy listener runs; the adapter now treats that ordering as
  an already-removed package and still releases its YooAsset leases. A repeated run exited without package-removal,
  exception, or error logs. `SdkGlobalCleanup_BeforeLeaseRelease_IsIdempotent` covers this ordering and compiles, but
  still requires execution in the final Unity EditMode rerun.
- Final C# compilation after the cleanup/race fixes passed for `GameLogic`, `TEngine.FairyGUI.Editor`, all EditMode
  tests, and the PlayMode host test source. `dotnet build` was used because the final Unity batch launches were blocked
  before project load by the local licensing client.
- `Publish.json` parses and every address currently listed in `FguiPackageCatalog.asset` exists below
  `Assets/AssetRaw`.
- The runtime host uses the dedicated `FairyGUI` Unity layer rather than the existing UGUI camera's `UI` layer; the
  PlayMode test asserts the Stage/camera culling mask contract.
- On shutdown, the SDK-owned Stage camera GameObject remains discoverable but both its `Camera` and `StageCamera`
  components and the `StageEngine` input/update component are disabled. This inert sentinel is intentional: SDK
  v5.2.0 keeps an internal scene-loaded callback that would recreate an active camera if the named object vanished.
  Reinitialization reuses that singleton and the PlayMode test covers the transition.
- The vendored SDK contains no IGG or old-project `dls.*` references. Runtime AOT assemblies are explicitly preserved
  by `Assets/TEngine/Extensions/FairyGUI/Runtime/link.xml`.

## Window test placement correction

The real-window test previously failed in EditMode at runtime-only bootstrap calls, then at
`DynamicFont.GetLineHeight` because its cached native Unity Font was destroyed. Replacing the host did not isolate
the SDK's real text construction. It is now in `PlayMode/FguiWindowModuleTests.cs`, under
`GameLogic.FairyGUI.PlayModeTests`, using the normal singleton and runtime host. It checks real text construction,
concurrent Show, caller cancellation, hook failures, Close/retry, and lease cleanup. The singleton is released in
`finally`; no singleton error-log suppression remains. This fixture uses Editor AssetDatabase assets and is excluded
from standalone test Players by `UNITY_EDITOR`.

The current source contains 10 EditMode tests (including the lifetime/event tests) and 2 Editor PlayMode tests. The relocated PlayMode test and the remaining
EditMode tests compile; their new Unity execution results are still pending. Earlier four-test XML results do not
validate this revision. Run the relocated test from the **PlayMode** tab, not EditMode.

A subsequent rescan reproduced CS1739: the PlayMode fixture passed `resourceProvider`, but the module overload no
longer accepted it. The settings overload now accepts an optional `IFguiResourceProvider`; normal callers keep
the existing YooAsset default, and the real runtime host is unchanged. Changing providers on an initialized module
is rejected. Both `GameLogic.FairyGUI.PlayModeTests.csproj` and `GameLogic.FairyGUI.Tests.csproj` compiled successfully
after this correction (zero errors; the pre-existing UIBase nullable warning remains). This is compilation evidence,
not a Unity Test Runner pass.

## Environment-blocked checks

The final Unity catalog/test rerun, real YooAsset AssetBundle build, direct pointer/input assertions, and
IL2CPP/HybridCLR Player smoke are **not completed**. EditorSimulate now proves the visible open/stop/re-enter path, but
it is not a substitute for the required bundle/Player checks. Three ordinary batch launches timed out before opening
the project while connecting to `LicenseClient-Administrator` (Unity return 199); an additional isolated-client check
had no usable Hub license (Unity return 1). Credentials from another process were not reused.

After closing the interactive Editor and confirming that `Temp/UnityLockfile` was gone, two further catalog launches
were attempted with the discovered Unity 2022.3.62f2 executable, first normally and then with `-force-free`. Both
started a licensing client but timed out on the same `LicenseClient-Administrator` channel and aborted with Unity's
reported return code 199 before loading the project. Logs: `Logs/fgui-final-catalog-verified.log` and
`Logs/fgui-final-catalog-force-free.log`. No further headless retries should be treated as useful without fixing that
host-level Unity licensing IPC.

Additionally, `ProjectSettings.asset` currently has no Standalone `ENABLE_HYBRIDCLR` define and no explicit scripting
backend, although `ProjectSettings/HybridCLRSettings.asset` has `enable: 1`. The final Player reviewer must establish the
intended target/backend instead of silently changing repository-wide build settings.

## Commands to rerun in an authenticated Unity session

```powershell
Unity.exe -batchmode -nographics -quit -projectPath <project> `
  -executeMethod TEngine.FairyGUIIntegration.Editor.FguiCatalogTools.ValidateCatalog

Unity.exe -batchmode -nographics -projectPath <project> -runTests -testPlatform EditMode `
  -testResults Logs/fgui-final-editmode.xml

Unity.exe -batchmode -nographics -projectPath <project> -runTests -testPlatform PlayMode `
  -testResults Logs/fgui-final-playmode.xml

Unity.exe -batchmode -nographics -quit -projectPath <project> `
  -executeMethod TEngine.ReleaseTools.BuildAssetBundle `
  -outputRoot Builds/FguiVerification -packageVersion fgui-verify -platform Windows
```

After establishing the intended IL2CPP target and HybridCLR define, run the repository's normal HybridCLR generation
and Player build, then verify both sample windows, the `asset://sample-icon` loader, shaders/text, UGUI coexistence,
mouse/drag/scroll/focus, device touch/multitouch, safe area, and stop/re-enter cleanup.
