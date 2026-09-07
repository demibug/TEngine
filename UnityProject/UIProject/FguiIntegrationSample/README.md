# FairyGUI integration sample

Open `FguiIntegrationSample.fairy` with FairyGUI Editor. The three packages are intentionally small:

- `Common` publishes the original SDK `Bag` package and is preloaded by both sample windows.
- `BundleUsage` and `ModalWaiting` provide two independently managed windows.

The publish path uses `{publish_file_name}`, so the `Bag`, `BundleUsage`, and `ModalWaiting` files each land in their
own directory below `Assets/AssetRaw/FGUI/Packages`. The runtime catalog keeps `Bag`'s logical dependency key as
`Common` by its stable package id. Generate C# only into
`Assets/GameScripts/HotFix/GameLogic/UI/FGUI/Gen`; controllers in `Imp` are never generated or deleted.
After publishing, run **TEngine > FairyGUI > Rebuild Package Catalog** and then validate it.

The source assets originate from the official FairyGUI Unity v5.2.0 examples (MIT license).
