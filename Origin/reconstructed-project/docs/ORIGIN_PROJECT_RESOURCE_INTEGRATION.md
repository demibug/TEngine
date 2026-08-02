# Origin Project Resource Integration

The cumulative project now contains the supplied original runtime assets under `origin_project/`.

## Development from the embedded folder

Instantiate `OriginAssetBootstrap` with `assetPrefix: 'origin_project/'` through `OriginRuntimeServices` when serving the repository root directly.

## Build/output layout

Run:

```bash
npm run assets:sync -- --target=dist-client
```

This copies the scenes, prefabs, data, shaders, libraries, resources, game.json and adapter into a runtime output directory. It deliberately excludes the original `game.js` and `js/` bundle unless `--include-original-js` is provided.

The reconstructed CommonJS source still requires a build/bundle entry before it can replace the original `js/bundle.js`.

## Resource-backed services

- `OriginProjectRuntime`
- `OriginAssetBootstrap`
- `LayaPrefabFactory`
- `LayaSpineAnimation`
- `LayaEnemyPresentation`
- `LayaSkillPresentation`

## Missing audio

The supplied package declares `resources/music` and `resources/sound`, but contains no files in either package. Audio calls remain valid contracts and must be supplied with the missing subpackage contents.
