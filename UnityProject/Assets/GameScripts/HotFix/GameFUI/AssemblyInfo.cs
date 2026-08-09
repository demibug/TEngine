using System.Runtime.CompilerServices;

// 友元程序集名必须与对应 asmdef 的 name 完全一致。
// GameFUI.Tests 只用于测试注入；GameBattle 作为 UIBattle 唯一业务 owner，
// 复用已冻结的 Registry 注册契约，不扩大 GameFUI 公共接口。
[assembly: InternalsVisibleTo("GameFUI.Tests")]
[assembly: InternalsVisibleTo("GameBattle")]
