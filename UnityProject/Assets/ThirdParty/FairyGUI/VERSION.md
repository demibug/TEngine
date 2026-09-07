# FairyGUI Unity SDK

- Version: 5.2.0
- Git tag: `5.2.0`
- Commit: `7f8555dd163bd17315f77b64907e07e735cf0ed0`
- Source: https://github.com/fairygui/FairyGUI-unity
- Imported directories: `Assets/Scripts`, `Assets/Editor`, `Assets/Resources`
- License: MIT; see `LICENSE` in this directory.

The vendor source is unmodified. TEngine integration code lives outside this directory.
The AOT linker preservation required by the HybridCLR boundary is declared in
`Assets/TEngine/Extensions/FairyGUI/Runtime/link.xml` rather than modifying vendor files.
