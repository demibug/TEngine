# TEngine FairyGUI usage

Initialization is explicit and asynchronous. `GameApp` opens the coexistence sample after the existing UGUI window
request; a failure is logged and does not close or replace UGUI. A product can replace that sample call with
`FguiSampleRegistration.InitializeAsync()` when it no longer wants the integration sample at startup.

```csharp
await FguiSampleRegistration.InitializeAsync();
var window = await GameModule.FGUI.ShowAsync<BundleUsageFguiWindow>(userData, cancellationToken);
// Or, while the existing BattleMainUI remains open:
var coexistenceWindow = await FguiSampleRegistration.ShowCoexistenceSampleAsync(cancellationToken);
window.SetExternalIcon("sample-icon"); // asset:// catalog entry; rapid replacement is generation-guarded.
GameModule.FGUI.Hide<BundleUsageFguiWindow>();
GameModule.FGUI.Close<BundleUsageFguiWindow>();
using (GameModule.FGUI.SuspendPresentation())
{
    // Show a UGUI-only system screen.
}
using FguiPackageLease commonPin = await GameModule.FGUI.PinPackageAsync("Common", cancellationToken);
```

The first caller's `userData` initializes a window while it is loading. Concurrent callers await the same generation;
cancelling one wait does not cancel the others. Calls made after the window is ready refresh and bring it to front.
`Hide` retains the view/package lease; `Close` destroys the view and then releases package/resource leases.
`PinPackageAsync` is optional and keeps a package plus its dependency closure resident only for the lease lifetime.

Package and external-asset addresses come only from `FguiPackageCatalog.asset`. Do not call `UIPackage.AddPackage`
directly for managed windows and do not pair these raw YooAsset handles with `UnloadAsset`.

FairyGUI renders on the dedicated `FairyGUI` Unity layer while the existing UGUI camera keeps the `UI` layer. The
runtime host assigns the Stage and its descendants to that layer and uses camera depth 3, above the existing UGUI
camera depth 2. Do not merge those culling masks: sharing `UI` would make the two cameras render the same meshes.

Generated code belongs only in `Gen`. Hand-written controllers belong only in `Imp`.

The pinned SDK source/commit is recorded in `Assets/ThirdParty/FairyGUI/VERSION.md`. Open
`UIProject/FguiIntegrationSample/FguiIntegrationSample.fairy` to edit the sample, publish binary assets below
`Assets/AssetRaw/FGUI/Packages`, and generate C# only below `Gen`. After publishing, run
**TEngine > FairyGUI > Rebuild Package Catalog**; the tool parses the real descriptors and rejects missing assets,
dependency cycles, and duplicate package identity.

See `VERIFICATION.md` for completed checks and the authenticated Unity/Player checks that still must be run.
