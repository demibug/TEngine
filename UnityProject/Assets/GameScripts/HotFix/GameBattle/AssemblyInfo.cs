using System.Runtime.CompilerServices;

// 仅向测试程序集暴露 internal 成员，避免为了测试把全部运行时类型改为 public。
// 友元程序集名必须与 GameBattle.Tests.asmdef 的 name 完全一致。
[assembly: InternalsVisibleTo("GameBattle.Tests")]
