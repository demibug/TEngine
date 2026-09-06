using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace GameBattle.Tests.EditMode.Enemy
{
    // ============================================================================
    // 任务 5.2/5.3/5.6（Prefab 侧）：多敌人表现资源静态契约校验
    // ----------------------------------------------------------------------------
    // 覆盖（task 5.6 验收要求）：
    //   1. 四个 Prefab 文件名与 enemy.xlsx resourceAddress（Mob0～Mob3）精确一致。
    //   2. 每个 Prefab 根节点名与地址一致；Body Sprite GUID 与对应 mob_N.png.meta 一致。
    //   3. 统一必需节点契约：根 → VisualRoot/HitEffectPoint/StunPoint，
    //      VisualRoot → Body/hpBgImg，hpBgImg → hpImg1/hpImg2。
    //   4. 血条默认隐藏：hpBgImg/hpImg1/hpImg2 的 SpriteRenderer 均禁用。
    //   5. 地址唯一：Assets/AssetRaw/Battle 下不存在与四个 Mob 同文件名的其他 Prefab。
    //
    // 校验方式：直接解析 Prefab YAML 与 mob_N.png.meta 文本，不依赖编辑器导入结果，
    // 也不调用 LayaBattlePrefabImporter（Editor 程序集不在此测试程序集引用链内）。
    // 本测试只读项目资源文本，不触碰 Scene/FUI/运行时状态。
    // ============================================================================

    /// <summary>
    /// Mob0～Mob3 敌人 Prefab 的资源契约静态校验（task 5.2/5.3/5.6）。
    /// </summary>
    [TestFixture]
    internal class MobPrefabContractTests
    {
        private const string EnemyPrefabFolder = "Assets/AssetRaw/Battle/Prefabs/Enemies";
        private const string BattleSpriteRoot = "Assets/AssetRaw/Battle/Sprites/Extracted/GameObject/enemy";
        private const string BattlePrefabRoot = "Assets/AssetRaw/Battle";

        /// <summary>enemy.xlsx 的四个 resourceAddress（唯一 Prefab 文件名寻址）。</summary>
        private static readonly string[] ExpectedAddresses =
            { "Mob0", "Mob1", "Mob2", "Mob3" };

        private static readonly Dictionary<string, string> BodySpriteFiles =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Mob0", "mob_0.png" },
                { "Mob1", "mob_1.png" },
                { "Mob2", "mob_2.png" },
                { "Mob3", "mob_3.png" },
            };

        // ====================================================================
        // 文件存在性与地址唯一性
        // ====================================================================

        [Test]
        [Description("四个 Prefab 文件名与 enemy.xlsx resourceAddress 精确一致且都存在。")]
        public void FourPrefabs_FileNames_ExactMatchConfigAddresses()
        {
            string folder = GetFullPath(EnemyPrefabFolder);
            Assert.IsTrue(Directory.Exists(folder), $"缺少敌人 Prefab 目录：{EnemyPrefabFolder}");

            // Boss 资源（例如 ZhangLiang.prefab）也位于该资源树下；本契约只约束 Mob0～Mob3。
            string[] existing = Directory.EnumerateFiles(folder, "*.prefab")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name.StartsWith("Mob", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] expected = ExpectedAddresses.OrderBy(name => name, StringComparer.Ordinal).ToArray();

            CollectionAssert.AreEqual(expected, existing,
                "敌人 Prefab 文件名必须精确等于配置地址，且不多不少。");
        }

        [Test]
        [Description("四个 Body 源 Sprite 文件与 meta 都存在（sprite 存在性校验）。")]
        public void BodySprites_MetaAndPng_Exist()
        {
            foreach (string file in BodySpriteFiles.Values.Distinct())
            {
                string png = GetFullPath($"{BattleSpriteRoot}/{file}");
                string meta = GetFullPath($"{BattleSpriteRoot}/{file}.meta");
                Assert.IsTrue(File.Exists(png), $"缺少 Body 源 Sprite：{png}");
                Assert.IsTrue(File.Exists(meta), $"缺少 Body 源 Sprite meta：{meta}");
            }
        }

        [Test]
        [Description("Assets/AssetRaw/Battle 下不存在与四个 Mob 同文件名的其他 Prefab。")]
        public void PrefabAddress_Unique_AcrossBattleTree()
        {
            string root = GetFullPath(BattlePrefabRoot);
            string[] conflicts = Directory.EnumerateFiles(root, "*.prefab", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => ExpectedAddresses.Contains(name, StringComparer.Ordinal))
                .ToArray();

            Assert.AreEqual(4, conflicts.Length,
                "四个 Mob 地址各只能有一个 Prefab 文件（YooAsset 以唯一文件名寻址）。");
        }

        // ====================================================================
        // 每个 Prefab 的统一契约
        // ====================================================================

        [Test]
        [Description("四个 Prefab 根名/节点契约/Body Sprite GUID/血条默认隐藏全部一致。")]
        public void EachMobPrefab_UnifiedContract_RootBodyAndHealthBar()
        {
            foreach (string address in ExpectedAddresses)
            {
                PrefabModel model = PrefabModel.Parse(GetFullPath($"{EnemyPrefabFolder}/{address}.prefab"));
                string expectedBodyGuid = ReadMetaGuid($"{BattleSpriteRoot}/{BodySpriteFiles[address]}.meta");

                Assert.AreEqual(address, model.RootName, $"{address}：根节点名必须与地址一致。");

                AssertChildNames(
                    model, model.RootGameObjectId, $"{address}：根节点子级契约",
                    "VisualRoot", "HitEffectPoint", "StunPoint");
                AssertChildNames(
                    model, model.FindGameObjectId("VisualRoot"), $"{address}：VisualRoot 子级契约",
                    "Body", "hpBgImg");
                AssertChildNames(
                    model, model.FindGameObjectId("hpBgImg"), $"{address}：hpBgImg 子级契约",
                    "hpImg1", "hpImg2");

                SpriteRendererModel body = model.RequireRenderer(model.FindGameObjectId("Body"));
                Assert.IsTrue(body.Enabled, $"{address}：Body 渲染器应默认可见。");
                Assert.AreEqual(expectedBodyGuid, body.SpriteGuid,
                    $"{address}：Body Sprite GUID 必须与 {BodySpriteFiles[address]}.meta 一致。");

                foreach (string healthNode in new[] { "hpBgImg", "hpImg1", "hpImg2" })
                {
                    SpriteRendererModel health = model.RequireRenderer(model.FindGameObjectId(healthNode));
                    Assert.IsFalse(health.Enabled,
                        $"{address}：{healthNode} 渲染器应默认隐藏（受击后由 EnemyHealthBarView 显示）。");
                }
            }
        }

        [Test]
        [Description("四个 Prefab 的必需节点都必须存在（缺任一节点即失败）。")]
        public void EachMobPrefab_RequiredNodes_AllPresent()
        {
            foreach (string address in ExpectedAddresses)
            {
                PrefabModel model = PrefabModel.Parse(GetFullPath($"{EnemyPrefabFolder}/{address}.prefab"));
                foreach (string node in new[] { "VisualRoot", "Body", "hpBgImg", "hpImg1", "hpImg2", "HitEffectPoint", "StunPoint" })
                {
                    Assert.Greater(model.FindGameObjectId(node), 0, $"{address}：缺少必需节点 {node}。");
                }
            }
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        private static void AssertChildNames(
            PrefabModel model,
            long parentGameObjectId,
            string message,
            params string[] expected)
        {
            string[] actual = model.GetChildNames(parentGameObjectId).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] wanted = expected.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            CollectionAssert.IsSubsetOf(wanted, actual,
                $"{message}；节点 {model.NameOf(parentGameObjectId)} 的子级契约不完整。");
        }

        private static string GetFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.IsNotNull(projectRoot, "无法从 Application.dataPath 解析 Unity 工程根目录。");
            return Path.GetFullPath(
                Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ReadMetaGuid(string metaAssetPath)
        {
            string text = File.ReadAllText(GetFullPath(metaAssetPath));
            Match match = Regex.Match(text, @"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$");
            if (!match.Success)
            {
                Assert.Fail($"meta 缺少 guid：{metaAssetPath}");
                return null;
            }

            return match.Groups[1].Value.ToLowerInvariant();
        }

        // ====================================================================
        // 极简 Prefab YAML 模型（仅解析本类 Prefab 所需字段）
        // ====================================================================

        private sealed class PrefabModel
        {
            private readonly Dictionary<long, string> _gameObjectNames =
                new Dictionary<long, string>(LongComparer.Instance);
            private readonly Dictionary<long, TransformModel> _transforms =
                new Dictionary<long, TransformModel>(LongComparer.Instance);
            private readonly Dictionary<long, SpriteRendererModel> _renderers =
                new Dictionary<long, SpriteRendererModel>(LongComparer.Instance);
            private long _rootTransformId;

            internal string RootName => _gameObjectNames[RootGameObjectId];

            internal long RootGameObjectId => _transforms[_rootTransformId].GameObjectId;

            internal static PrefabModel Parse(string prefabFullPath)
            {
                string text = File.ReadAllText(prefabFullPath);
                var model = new PrefabModel();
                model.ParseDocuments(text);
                return model;
            }

            internal long FindGameObjectId(string name)
            {
                foreach (KeyValuePair<long, string> pair in _gameObjectNames)
                {
                    if (string.Equals(pair.Value, name, StringComparison.Ordinal))
                    {
                        return pair.Key;
                    }
                }

                return 0;
            }

            internal string NameOf(long gameObjectId)
            {
                return _gameObjectNames.TryGetValue(gameObjectId, out string name) ? name : "<missing>";
            }

            internal IReadOnlyList<string> GetChildNames(long parentGameObjectId)
            {
                long parentTransformId = 0;
                foreach (KeyValuePair<long, TransformModel> pair in _transforms)
                {
                    if (pair.Value.GameObjectId == parentGameObjectId)
                    {
                        parentTransformId = pair.Key;
                        break;
                    }
                }

                var names = new List<string>();
                if (!_transforms.TryGetValue(parentTransformId, out TransformModel transform))
                {
                    return names;
                }

                foreach (long childTransformId in transform.ChildrenTransformIds)
                {
                    if (_transforms.TryGetValue(childTransformId, out TransformModel child))
                    {
                        names.Add(_gameObjectNames[child.GameObjectId]);
                    }
                }

                return names;
            }

            internal SpriteRendererModel RequireRenderer(long gameObjectId)
            {
                if (!_renderers.TryGetValue(gameObjectId, out SpriteRendererModel renderer))
                {
                    Assert.Fail($"节点 {NameOf(gameObjectId)} 缺少 SpriteRenderer。");
                }

                return renderer;
            }

            private void ParseDocuments(string text)
            {
                Regex documentPattern = new Regex(
                    @"^--- !u!(\d+) &(\d+)[ \t]*\r?\n(?<body>.*?)(?=^--- !u!|\z)",
                    RegexOptions.Singleline | RegexOptions.Multiline);

                bool hasRoot = false;
                foreach (Match match in documentPattern.Matches(text))
                {
                    int classId = int.Parse(match.Groups[1].Value);
                    long fileId = long.Parse(match.Groups[2].Value);
                    AddDocument(classId, fileId, match.Groups["body"].Value);
                }

                foreach (KeyValuePair<long, TransformModel> pair in _transforms)
                {
                    if (pair.Value.FatherTransformId == 0)
                    {
                        _rootTransformId = pair.Key;
                        hasRoot = true;
                        break;
                    }
                }

                if (!hasRoot)
                {
                    throw new InvalidDataException("Prefab 缺少根 Transform（m_Father=0）。");
                }
            }

            private void AddDocument(int classId, long fileId, string body)
            {
                switch (classId)
                {
                    case 1:
                        AddGameObject(fileId, body);
                        break;
                    case 4:
                        AddTransform(fileId, body);
                        break;
                    case 212:
                        AddSpriteRenderer(fileId, body);
                        break;
                }
            }

            private void AddGameObject(long fileId, string body)
            {
                Match nameMatch = Regex.Match(body, @"(?m)^\s*m_Name:\s*(.+?)\s*$");
                if (!nameMatch.Success)
                {
                    return;
                }

                _gameObjectNames[fileId] = nameMatch.Groups[1].Value;
            }

            private void AddTransform(long fileId, string body)
            {
                Match goMatch = Regex.Match(body, @"(?m)^\s*m_GameObject:\s*\{fileID:\s*(\d+)\}");
                Match fatherMatch = Regex.Match(body, @"(?m)^\s*m_Father:\s*\{fileID:\s*(\d+)\}");
                if (!goMatch.Success)
                {
                    return;
                }

                var children = new List<long>();
                foreach (Match child in Regex.Matches(body, @"(?m)^\s*-\s*\{fileID:\s*(\d+)\}"))
                {
                    children.Add(long.Parse(child.Groups[1].Value));
                }

                _transforms[fileId] = new TransformModel(
                    long.Parse(goMatch.Groups[1].Value),
                    fatherMatch.Success ? long.Parse(fatherMatch.Groups[1].Value) : 0,
                    children);
            }

            private void AddSpriteRenderer(long fileId, string body)
            {
                Match goMatch = Regex.Match(body, @"(?m)^\s*m_GameObject:\s*\{fileID:\s*(\d+)\}");
                if (!goMatch.Success)
                {
                    return;
                }

                long gameObjectId = long.Parse(goMatch.Groups[1].Value);
                bool enabled = Regex.Match(body, @"(?m)^\s*m_Enabled:\s*(\d)").Groups[1].Value == "1";
                Match spriteMatch = Regex.Match(
                    body, @"(?m)^\s*m_Sprite:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]{32}),\s*type:\s*3\}");
                _renderers[gameObjectId] = new SpriteRendererModel(enabled,
                    spriteMatch.Success ? spriteMatch.Groups[1].Value.ToLowerInvariant() : null);
            }

            private sealed class TransformModel
            {
                internal readonly long GameObjectId;
                internal readonly long FatherTransformId;
                internal readonly List<long> ChildrenTransformIds;

                internal TransformModel(long gameObjectId, long fatherTransformId, List<long> childrenTransformIds)
                {
                    GameObjectId = gameObjectId;
                    FatherTransformId = fatherTransformId;
                    ChildrenTransformIds = childrenTransformIds;
                }
            }

            private sealed class LongComparer : IEqualityComparer<long>
            {
                internal static readonly LongComparer Instance = new LongComparer();

                public bool Equals(long x, long y)
                {
                    return x == y;
                }

                public int GetHashCode(long obj)
                {
                    return obj.GetHashCode();
                }
            }
        }

        private sealed class SpriteRendererModel
        {
            internal readonly bool Enabled;
            internal readonly string SpriteGuid;

            internal SpriteRendererModel(bool enabled, string spriteGuid)
            {
                Enabled = enabled;
                SpriteGuid = spriteGuid;
            }
        }
    }
}
