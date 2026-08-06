using System.Runtime.CompilerServices;

// 仅向测试程序集暴露 internal 成员，避免为了测试把全部运行时类型改为 public。
// 友元程序集名必须与 GameFUI.Tests.asmdef 的 name 完全一致。
// 暴露目标：InMemoryFUIResourceProvider / InMemoryAssetHandle 等测试可注入的 internal 类型
// （见 Resource/IFUIResourceProvider.cs），供 EditMode/PlayMode 测试注入可控内存资源能力。
[assembly: InternalsVisibleTo("GameFUI.Tests")]
