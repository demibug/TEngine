using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 单条 DLL 复制计划（源文件 → .bytes 目标文件）。
    /// </summary>
    public struct DllCopyEntry
    {
        /// <summary>源 DLL 路径（按所选目标解析）。</summary>
        public string SourcePath;

        /// <summary>目标 .bytes 路径。</summary>
        public string DestPath;

        public DllCopyEntry(string sourcePath, string destPath)
        {
            SourcePath = sourcePath;
            DestPath = destPath;
        }
    }

    /// <summary>
    /// 热更/AOT DLL 产物校验与复制。
    /// <remarks>先对全部计划做整体校验（存在且非空），通过后才统一复制；缺任意必需文件都不复制，不能被旧产物掩盖。</remarks>
    /// </summary>
    public static class DllArtifactCopier
    {
        /// <summary>
        /// 校验所有源文件存在且非空。返回错误列表（空列表 = 通过）。
        /// </summary>
        public static List<string> ValidateSources(IReadOnlyList<DllCopyEntry> entries)
        {
            var errors = new List<string>();
            if (entries == null || entries.Count == 0)
            {
                errors.Add("DLL 复制计划为空，未找到任何必需产物定义");
                return errors;
            }

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.SourcePath) || string.IsNullOrEmpty(entry.DestPath))
                {
                    errors.Add($"DLL 复制计划包含空路径: src={entry.SourcePath ?? "null"} dst={entry.DestPath ?? "null"}");
                    continue;
                }

                if (!File.Exists(entry.SourcePath))
                    errors.Add($"缺少必需 DLL 源文件: {entry.SourcePath}");
                else if (new FileInfo(entry.SourcePath).Length == 0)
                    errors.Add($"必需 DLL 源文件为空: {entry.SourcePath}");
            }

            return errors;
        }

        /// <summary>
        /// 复用模式校验（BuildHotFixDll=false 时 AB 前调用）：
        /// 源文件存在且非空，且目标 .bytes 存在、非空并与所选目标源文件内容一致（哈希比较，不看时间戳）。
        /// </summary>
        public static List<string> ValidateReuse(IReadOnlyList<DllCopyEntry> entries)
        {
            var errors = ValidateSources(entries);
            if (errors.Count > 0)
                return errors;

            foreach (var entry in entries)
            {
                if (!File.Exists(entry.DestPath))
                {
                    errors.Add($"复用模式缺少已复制产物: {entry.DestPath}（需先编译复制或重新准备）");
                    continue;
                }

                if (new FileInfo(entry.DestPath).Length == 0)
                {
                    errors.Add($"复用模式产物为空: {entry.DestPath}");
                    continue;
                }

                if (!ContentEquals(entry.SourcePath, entry.DestPath))
                    errors.Add($"复用产物与所选目标的源文件内容不一致，旧产物不可复用: {entry.DestPath} != {entry.SourcePath}");
            }

            return errors;
        }

        /// <summary>
        /// 执行复制（目标目录按需创建，覆盖同名文件）；任何 IO 异常直接向上传播，由调用方停止下游。
        /// <remarks>不承诺事务回滚：中途失败可能留下部分文件，但失败方不得进入 AB；后续复用必须重新通过完整校验。</remarks>
        /// </summary>
        public static void CopyAll(IReadOnlyList<DllCopyEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                throw new InvalidOperationException("DLL 复制计划为空");

            foreach (var entry in entries)
            {
                string destDir = Path.GetDirectoryName(Path.GetFullPath(entry.DestPath));
                Directory.CreateDirectory(destDir);
                File.Copy(entry.SourcePath, entry.DestPath, true);
            }
        }

        /// <summary>
        /// 解析并校验 AssemblyTextAssetPath 输出目录，禁止越出 Assets 目录范围。
        /// </summary>
        /// <param name="createIfMissing">是否按需创建目录（复用校验场景传 false）。</param>
        /// <returns>完整路径。</returns>
        public static string ResolveAssemblyTextAssetDir(bool createIfMissing)
        {
            string relative = Settings.UpdateSetting.AssemblyTextAssetPath;
            if (string.IsNullOrWhiteSpace(relative))
                throw new InvalidOperationException("UpdateSetting.AssemblyTextAssetPath 未配置");

            string assetsRoot = Path.GetFullPath(Application.dataPath);
            string fullDir = Path.GetFullPath(Path.Combine(assetsRoot, relative));
            if (!fullDir.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"AssemblyTextAssetPath 越出 Assets 输出范围: {relative} -> {fullDir}");

            if (createIfMissing)
                Directory.CreateDirectory(fullDir);
            return fullDir;
        }

        private static bool ContentEquals(string fileA, string fileB)
        {
            string hashA = ComputeSha256(fileA);
            string hashB = ComputeSha256(fileB);
            return string.Equals(hashA, hashB, StringComparison.Ordinal);
        }

        private static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }
    }
}
