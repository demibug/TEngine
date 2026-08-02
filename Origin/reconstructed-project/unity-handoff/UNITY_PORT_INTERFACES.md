# Unity Port Interfaces

Implement C# equivalents of `src/ports/CombatPorts.js`:

- `ICombatClock.NowMilliseconds`
- `IRandomSource.Next01`
- `ICombatView.Spawn/Remove`
- `IAudioPort.Play/Stop`
- `IVfxPort.Create/Remove`
- `IInputPort` producing battle commands
- `IScenePort.Open/Close`
- `IResourcePort.Load`

Unity input should emit `PurchaseAndPlace`, drag, move, merge and refresh commands. Domain code must not inspect `PointerEventData` directly.
