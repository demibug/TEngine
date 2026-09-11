using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 固定发布入口的最小数据结构。
    /// </summary>
    [Serializable]
    public sealed class UpdateReleaseEntry
    {
        public int EntryVersion;
        public string ReleaseId;
    }

    /// <summary>
    /// 固定发布入口校验器。入口版本独立于两阶段运行时契约版本。
    /// </summary>
    public static class UpdateReleaseEntryValidator
    {
        public const int SupportedEntryVersion = 1;
        public const int MaximumEntryBytes = 4 * 1024;
        public const int MinimumReleaseIdLength = 1;
        public const int MaximumReleaseIdLength = 128;

        /// <summary>
        /// 解析并校验 current.json。未知 JSON 字段由 Unity JsonUtility 自然忽略。
        /// </summary>
        public static bool TryParse(string json, out UpdateReleaseEntry entry, out string error)
        {
            entry = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "固定入口内容为空。";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(json) > MaximumEntryBytes)
            {
                error = $"固定入口超过 {MaximumEntryBytes} 字节上限。";
                return false;
            }

            try
            {
                entry = JsonUtility.FromJson<UpdateReleaseEntry>(json);
            }
            catch (Exception exception)
            {
                error = $"固定入口 JSON 格式错误：{exception.Message}";
                return false;
            }

            List<string> errors = Validate(entry);
            if (errors.Count > 0)
            {
                error = string.Join("\n", errors);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 校验已反序列化的入口对象。
        /// </summary>
        public static List<string> Validate(UpdateReleaseEntry entry)
        {
            List<string> errors = new List<string>();
            if (entry == null)
            {
                errors.Add("固定入口 JSON 为 null。");
                return errors;
            }

            if (entry.EntryVersion != SupportedEntryVersion)
            {
                errors.Add(
                    $"固定入口版本不支持：实际为 {entry.EntryVersion}，仅支持 {SupportedEntryVersion}。");
            }

            if (!IsValidReleaseId(entry.ReleaseId))
            {
                errors.Add(
                    "固定入口 ReleaseId 非法：长度必须为 1～128，且只能包含 ASCII 字母、数字、短横线或下划线。");
            }

            return errors;
        }

        /// <summary>
        /// 固定入口 ReleaseId 的严格目录协议校验。
        /// </summary>
        public static bool IsValidReleaseId(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.Length < MinimumReleaseIdLength ||
                value.Length > MaximumReleaseIdLength)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool asciiLetter = character >= 'A' && character <= 'Z' ||
                                   character >= 'a' && character <= 'z';
                bool digit = character >= '0' && character <= '9';
                if (!asciiLetter && !digit && character != '-' && character != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
