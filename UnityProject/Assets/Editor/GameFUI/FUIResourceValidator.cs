/** FUIResourceValidator.cs — GameFUI 构建前资源校验
 *
 * 对应 OpenSpec Change integrate-fairygui-module 任务 4.2。
 * 依据 design.md 决策11：构建前校验描述 location、内部包名、资源前缀、
 * 重复文件和生成注册信息，避免把寻址错误推迟到后续 Player 接入。
 *
 * 校验覆盖五个维度：
 *   1. {PackageName}_fui 描述文件存在
 *   2. 内部包名一致性（描述文件内 FairyGUI 包名与文件名前缀一致）
 *   3. 外部资源前缀正确（atlas0.png 等外部资源以所属包名为前缀）
 *   4. location 唯一性（YooAsset 使用 AddressByFileName，文件名即 location，不可重复）
 *   5. 历史命名冲突（不存在 BattleUI_* / Common_* 等历史命名产物）
 *
 * 校验失败时输出明确错误信息并阻止继续构建；可在 Editor 菜单手动调用，
 * 也可由其他构建前钩子调用 ValidateAll 返回 false 阻断流程。
 */

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameFUI.Editor
{
    /// <summary>
    /// FGUI 资源构建前校验器。所有方法均为 Editor-only。
    /// 校验目标目录：Assets/AssetRaw/FUI
    /// </summary>
    public static class FUIResourceValidator
    {
        /// <summary>FGUI 资源根目录（与 AssetBundleCollector 中 FUI Collector 一致）。</summary>
        public const string FUIAssetRoot = "Assets/AssetRaw/FUI";

        /// <summary>FGUI 包描述文件后缀（含分隔符），规范 location 为 {PackageName}_fui。</summary>
        public const string FuiDescSuffix = "_fui";

        /// <summary>FGUI 包描述文件扩展名。</summary>
        public const string FuiDescExtension = ".bytes";

        /// <summary>
        /// 历史命名前缀列表。同一逻辑包若同时存在规范命名与历史命名产物，
        /// 视为资源集不确定，必须阻止构建。
        /// 依据 design.md：历史 BattleUI/Common 产物在确认无引用后移除。
        /// </summary>
        private static readonly string[] HistoricalPackagePrefixes = new string[]
        {
            "BattleUI",
            "Common",
        };

        /// <summary>历史命名到规范命名的映射，用于冲突报告。</summary>
        private static readonly Dictionary<string, string> HistoricalToCanonical = new Dictionary<string, string>
        {
            { "BattleUI", "UIBattle" },
            { "Common", "UICommon" },
        };

        /// <summary>
        /// Editor 菜单入口：执行全部校验并输出结果。
        /// 路径：TEngine/GameFUI/校验 FGUI 资源
        /// </summary>
        [MenuItem("TEngine/GameFUI/校验 FGUI 资源 #&f", false, 100)]
        public static void ValidateFromMenu()
        {
            List<string> errors = new List<string>();
            bool ok = ValidateAll(errors);
            if (ok)
            {
                Debug.Log("[GameFUI.Editor] FGUI 资源校验通过。");
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[GameFUI.Editor] FGUI 资源校验失败，已阻止继续构建：");
                foreach (string err in errors)
                {
                    sb.AppendLine("  - " + err);
                }
                Debug.LogError(sb.ToString());
            }
        }

        /// <summary>
        /// 执行全部五个维度的校验。
        /// 供构建前钩子调用：返回 true 表示通过，false 表示存在阻断性错误。
        /// </summary>
        /// <param name="errors">校验过程中收集的错误信息（即使返回 false 也可能非空）。</param>
        /// <returns>true 通过；false 存在错误。</returns>
        public static bool ValidateAll(List<string> errors)
        {
            if (errors == null)
            {
                errors = new List<string>();
            }

            // 目录不存在视为阻断：缺少 FUI 资源目录无法完成任何校验。
            if (!AssetDatabase.IsValidFolder(FUIAssetRoot))
            {
                errors.Add($"FGUI 资源目录不存在：{FUIAssetRoot}。请先发布 FairyGUI 包到该目录。");
                return false;
            }

            // 收集目录下全部文件（相对工程路径）。
            List<string> allFiles = CollectFUIFiles();
            if (allFiles.Count == 0)
            {
                errors.Add($"FGUI 资源目录为空：{FUIAssetRoot}。未发现任何 FGUI 产物。");
                return false;
            }

            // 维度1：{PackageName}_fui 描述文件存在，并据此建立规范包名集合。
            HashSet<string> canonicalPackageNames = new HashSet<string>();
            List<string> descFiles = new List<string>();
            foreach (string file in allFiles)
            {
                string fileName = Path.GetFileName(file);
                if (IsFuiDescFile(fileName))
                {
                    descFiles.Add(file);
                    string pkgName = ExtractPackageNameFromDescFile(fileName);
                    canonicalPackageNames.Add(pkgName);
                }
            }

            if (descFiles.Count == 0)
            {
                errors.Add($"未在 {FUIAssetRoot} 发现任何 {FuiDescSuffix}{FuiDescExtension} 描述文件，无法确定逻辑包。");
            }

            // 维度2：内部包名一致性（读取描述文件二进制，比对文件名前缀）。
            ValidateInternalPackageNames(descFiles, canonicalPackageNames, errors);

            // 维度3：外部资源前缀正确性。
            ValidateExternalResourcePrefixes(allFiles, canonicalPackageNames, errors);

            // 维度4：location 唯一性（文件名去重）。
            ValidateLocationUniqueness(allFiles, errors);

            // 维度5：历史命名冲突。
            ValidateHistoricalNamingConflicts(allFiles, canonicalPackageNames, errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// 收集 FUI 资源目录下全部文件路径（含子目录），返回相对工程根的 Assets 路径。
        /// 跳过 .meta 文件与隐藏文件。
        /// </summary>
        private static List<string> CollectFUIFiles()
        {
            List<string> result = new List<string>();
            // 使用 Directory 枚举保证覆盖子目录；AssetDatabase.FindAssets 对资源命名敏感且会包含 .meta。
            string absRoot = Path.GetFullPath(FUIAssetRoot);
            if (!Directory.Exists(absRoot))
            {
                return result;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');

            foreach (string absFile in Directory.EnumerateFiles(absRoot, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(absFile);
                // 跳过 Unity meta 与隐藏文件。
                if (fileName.EndsWith(".meta") || fileName.StartsWith("."))
                {
                    continue;
                }
                // 转为 Assets/ 相对路径，统一分隔符。
                string relPath = absFile.Replace('\\', '/').Replace(projectRoot + "/", "");
                result.Add(relPath);
            }

            return result;
        }

        /// <summary>判断文件是否为 FGUI 包描述文件（{PackageName}_fui.bytes）。</summary>
        private static bool IsFuiDescFile(string fileName)
        {
            return fileName.EndsWith(FuiDescSuffix + FuiDescExtension, System.StringComparison.Ordinal);
        }

        /// <summary>从描述文件名提取规范包名：{PackageName}_fui.bytes -> {PackageName}。</summary>
        private static string ExtractPackageNameFromDescFile(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName); // {PackageName}_fui
            if (name.EndsWith(FuiDescSuffix, System.StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - FuiDescSuffix.Length);
            }
            return name;
        }

        /// <summary>
        /// 维度2：读取每个描述文件的 FairyGUI 二进制头，提取内部包名，
        /// 校验其与文件名前缀一致。不一致说明导出器包名与文件命名脱节，
        /// 会令 YooAsset location 与运行时 UIPackage 名错位。
        /// </summary>
        private static void ValidateInternalPackageNames(List<string> descFiles, HashSet<string> canonicalPackageNames, List<string> errors)
        {
            foreach (string descFile in descFiles)
            {
                string fileName = Path.GetFileName(descFile);
                string expectedPkg = ExtractPackageNameFromDescFile(fileName);

                string internalPkgName = ReadInternalPackageName(descFile);
                if (internalPkgName == null)
                {
                    errors.Add($"描述文件 {fileName} 无法解析内部包名：文件不是有效的 FairyGUI 二进制包（缺少 FGUI 头）。");
                    continue;
                }

                if (internalPkgName != expectedPkg)
                {
                    errors.Add($"描述文件 {fileName} 内部包名 '{internalPkgName}' 与文件名前缀 '{expectedPkg}' 不一致。" +
                               $"规范要求 location({expectedPkg}{FuiDescSuffix}) 与内部包名({internalPkgName}) 使用统一规则。");
                }
            }
        }

        /// <summary>
        /// 解析 FairyGUI 二进制包描述，提取内部包名。
        /// 二进制格式（与 FairyGUI UIPackage.LoadPackage 一致，大端序）：
        ///   uint magic = 0x46475549 ("FGUI")
        ///   int  version
        ///   bool compressed
        ///   string id     （ushort 长度 + UTF8 字节）
        ///   string name   （ushort 长度 + UTF8 字节） <- 内部包名
        /// </summary>
        /// <returns>内部包名；解析失败返回 null。</returns>
        private static string ReadInternalPackageName(string assetRelativePath)
        {
            string absPath = Path.GetFullPath(assetRelativePath);
            if (!File.Exists(absPath))
            {
                return null;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(absPath);
            }
            catch
            {
                return null;
            }

            if (data == null || data.Length < 13)
            {
                return null;
            }

            int p = 0;

            // magic: 4 字节大端 uint，必须等于 0x46475549 ('F','G','U','I')。
            uint magic = ReadUInt32BigEndian(data, ref p);
            if (magic != 0x46475549u)
            {
                return null;
            }

            // version: 4 字节大端 int。
            int version = ReadInt32BigEndian(data, ref p);

            // compressed: 1 字节 bool。
            if (p >= data.Length)
            {
                return null;
            }
            p += 1; // 跳过 compressed 标志。

            // id 字符串：ushort(2 大端) 长度 + UTF8。
            string id = ReadBigEndianString(data, ref p);
            if (id == null)
            {
                return null;
            }

            // name 字符串：内部包名。
            string name = ReadBigEndianString(data, ref p);
            return name;
        }

        /// <summary>大端序读取 4 字节 uint。</summary>
        private static uint ReadUInt32BigEndian(byte[] data, ref int p)
        {
            if (p + 4 > data.Length)
            {
                p = data.Length;
                return 0;
            }
            uint v = (uint)((data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3]);
            p += 4;
            return v;
        }

        /// <summary>大端序读取 4 字节 int。</summary>
        private static int ReadInt32BigEndian(byte[] data, ref int p)
        {
            return (int)ReadUInt32BigEndian(data, ref p);
        }

        /// <summary>读取 FairyGUI 长度前缀字符串：ushort(2 大端) 长度 + UTF8 字节。</summary>
        private static string ReadBigEndianString(byte[] data, ref int p)
        {
            if (p + 2 > data.Length)
            {
                p = data.Length;
                return null;
            }
            ushort len = (ushort)((data[p] << 8) | data[p + 1]);
            p += 2;
            if (p + len > data.Length)
            {
                p = data.Length;
                return null;
            }
            string s = Encoding.UTF8.GetString(data, p, len);
            p += len;
            return s;
        }

        /// <summary>
        /// 维度3：校验外部资源前缀正确性。
        /// 非描述文件（atlas0.png、音频、其他贴图等）必须以某个已识别的规范包名为前缀，
        /// 即文件名以 {PackageName}_ 开头，或位于 {PackageName}/ 子目录下。
        /// 不满足则该资源 location 无法映射到逻辑包，会在运行期寻址失败。
        /// </summary>
        private static void ValidateExternalResourcePrefixes(List<string> allFiles, HashSet<string> canonicalPackageNames, List<string> errors)
        {
            foreach (string file in allFiles)
            {
                string fileName = Path.GetFileName(file);
                if (IsFuiDescFile(fileName))
                {
                    continue; // 描述文件已由维度1、2 处理。
                }

                string pkgName = ResolveOwningPackage(file, canonicalPackageNames);
                if (pkgName == null)
                {
                    errors.Add($"外部资源 {file} 未归属任何规范包：文件名前缀与目录均不匹配已知包名" +
                               $"（{string.Join(", ", canonicalPackageNames)}）。" +
                               $"外部资源 SHALL 使用导出器生成的包名前缀。");
                }
            }
        }

        /// <summary>
        /// 解析资源归属包名：优先匹配文件名前缀 {PackageName}_，其次匹配一级子目录 {PackageName}/。
        /// 匹配失败返回 null。
        /// </summary>
        private static string ResolveOwningPackage(string assetRelativePath, HashSet<string> canonicalPackageNames)
        {
            string fileName = Path.GetFileName(assetRelativePath);

            // 1. 文件名前缀：{PackageName}_xxx
            foreach (string pkg in canonicalPackageNames)
            {
                if (fileName.StartsWith(pkg + "_", System.StringComparison.Ordinal))
                {
                    return pkg;
                }
            }

            // 2. 一级子目录：FUIAssetRoot/{PackageName}/xxx
            string relToFuiRoot = assetRelativePath;
            string rootPrefix = FUIAssetRoot + "/";
            if (relToFuiRoot.StartsWith(rootPrefix, System.StringComparison.Ordinal))
            {
                relToFuiRoot = relToFuiRoot.Substring(rootPrefix.Length);
                int slash = relToFuiRoot.IndexOf('/');
                if (slash > 0)
                {
                    string dirName = relToFuiRoot.Substring(0, slash);
                    if (canonicalPackageNames.Contains(dirName))
                    {
                        return dirName;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 维度4：location 唯一性。
        /// YooAsset Collector 对 FUI 使用 AddressByFileName，location 等于文件名（不含扩展名）。
        /// 不同文件若产生相同 location（例如同名的 .bytes 与 .png，或大小写差异），
        /// 会导致寻址冲突。此处按无扩展名文件名分组检测重复。
        /// </summary>
        private static void ValidateLocationUniqueness(List<string> allFiles, List<string> errors)
        {
            // key: 无扩展名小写文件名（location，大小写不敏感以覆盖 Windows/打包差异）
            Dictionary<string, List<string>> locationMap = new Dictionary<string, List<string>>();
            foreach (string file in allFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string key = fileName.ToLowerInvariant();
                if (!locationMap.TryGetValue(key, out List<string> bucket))
                {
                    bucket = new List<string>();
                    locationMap[key] = bucket;
                }
                bucket.Add(file);
            }

            foreach (var kv in locationMap)
            {
                if (kv.Value.Count > 1)
                {
                    errors.Add($"location 唯一性冲突：以下文件产生相同 location '{kv.Key}'：{string.Join(", ", kv.Value)}。" +
                               $"AddressByFileName 下文件名即 location，不可重复。");
                }
            }
        }

        /// <summary>
        /// 维度5：历史命名冲突。
        /// 同一逻辑包同时存在规范命名（UIBattle_* / UICommon_*）与历史命名
        /// （BattleUI_* / Common_*）产物时，资源集不确定，必须阻止构建。
        /// 同时也禁止任何历史命名残留，避免回到旧导出约定。
        /// </summary>
        private static void ValidateHistoricalNamingConflicts(List<string> allFiles, HashSet<string> canonicalPackageNames, List<string> errors)
        {
            // 收集历史命名文件。
            List<string> historicalFiles = new List<string>();
            foreach (string file in allFiles)
            {
                string fileName = Path.GetFileName(file);
                foreach (string histPrefix in HistoricalPackagePrefixes)
                {
                    // 历史命名形如 BattleUI_xxx 或恰好 BattleUI。
                    if (fileName.StartsWith(histPrefix + "_", System.StringComparison.Ordinal) ||
                        fileName.Equals(histPrefix, System.StringComparison.Ordinal) ||
                        fileName.StartsWith(histPrefix + FuiDescSuffix + FuiDescExtension, System.StringComparison.Ordinal))
                    {
                        historicalFiles.Add(file);
                        break;
                    }
                }
            }

            if (historicalFiles.Count == 0)
            {
                return; // 无历史命名，通过。
            }

            // 报告每一处历史命名；若同时存在对应规范命名，明确指出新旧并存冲突。
            foreach (string histFile in historicalFiles)
            {
                string fileName = Path.GetFileName(histFile);
                string histPkg = ResolveHistoricalPackagePrefix(fileName);
                if (histPkg != null && HistoricalToCanonical.TryGetValue(histPkg, out string canonical) &&
                    canonicalPackageNames.Contains(canonical))
                {
                    errors.Add($"新旧命名同时存在：发现历史命名 '{fileName}'，而规范包 '{canonical}' 同时存在。" +
                               $"发布校验 SHALL 报告冲突并阻止以不确定资源集继续构建。请移除历史 {histPkg} 产物。");
                }
                else
                {
                    errors.Add($"发现历史命名残留 '{fileName}'（前缀 {histPkg}）。" +
                               $"历史命名产物应在确认无引用后移除，禁止与规范命名并存。");
                }
            }
        }

        /// <summary>从文件名解析其所属的历史命名前缀，非历史命名返回 null。</summary>
        private static string ResolveHistoricalPackagePrefix(string fileName)
        {
            foreach (string histPrefix in HistoricalPackagePrefixes)
            {
                if (fileName.StartsWith(histPrefix + "_", System.StringComparison.Ordinal) ||
                    fileName.Equals(histPrefix, System.StringComparison.Ordinal) ||
                    fileName.StartsWith(histPrefix + FuiDescSuffix + FuiDescExtension, System.StringComparison.Ordinal))
                {
                    return histPrefix;
                }
            }
            return null;
        }
    }
}
