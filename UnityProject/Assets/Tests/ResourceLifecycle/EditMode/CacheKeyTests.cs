using NUnit.Framework;
using TEngine;

namespace TEngine.ResourceLifecycleTests
{
    /// <summary>
    /// 缓存 Key 规范化测试：所有包使用同一可唯一反解的无碰撞编码；
    /// 空包与默认包等价；跨包 Key 不得碰撞。
    /// </summary>
    public sealed class CacheKeyTests
    {
        private static ResourceModule CreateModule(string defaultPackageName = "DefaultPackage")
        {
            return new ResourceModule { DefaultPackageName = defaultPackageName };
        }

        [Test]
        public void DefaultPackage_Equivalent_EmptyAndExplicit()
        {
            var module = CreateModule();
            Assert.AreEqual(module.GetCacheKey("Assets/A/b.png"), module.GetCacheKey("Assets/A/b.png", "DefaultPackage"));
            Assert.AreEqual(module.GetCacheKey("Assets/A/b.png"), module.GetCacheKey("Assets/A/b.png", ""));
        }

        [Test]
        public void DefaultKey_NeverCollides_WithCustomPackageKey()
        {
            // 审查者给出的碰撞用例：默认包 location="3:Pkg/foo" 与自定义包 (Pkg, foo)。
            var module = CreateModule();
            string defaultKey = module.GetCacheKey("3:Pkg/foo");
            string customKey = module.GetCacheKey("foo", "Pkg");
            Assert.AreNotEqual(defaultKey, customKey, "默认包裸 location 不得与自定义包键碰撞。");

            // 默认包 location 以任意长度前缀样字符串开头均需安全。
            Assert.AreNotEqual(module.GetCacheKey("3:Pkg/foo"), module.GetCacheKey("Pkg/foo", "3"));
            Assert.AreNotEqual(module.GetCacheKey("10:xyz"), module.GetCacheKey("xyz", "0123456789"));
            Assert.AreNotEqual(module.GetCacheKey("0:abc"), module.GetCacheKey("abc", "0:"));
        }

        [Test]
        public void OldSchemeCollision_NewSchemeDisambiguates()
        {
            // 旧拼接方案碰撞用例：pkg="OtherPackage/A"，loc="b" 与 pkg="OtherPackage"，loc="A/b"。
            var module = CreateModule();
            string key1 = module.GetCacheKey("b", "OtherPackage/A");
            string key2 = module.GetCacheKey("A/b", "OtherPackage");
            Assert.AreNotEqual(key1, key2);
        }

        [Test]
        public void LocationWithSeparator_DoesNotCollideAcrossPackages()
        {
            var module = CreateModule();
            string key1 = module.GetCacheKey("a:b/c", "P1");
            string key2 = module.GetCacheKey("c", "P1/a:b");
            Assert.AreNotEqual(key1, key2);
        }

        [Test]
        public void BuildPackageCacheKey_IsParseableUniquely()
        {
            // 任意（package, location）组合都应产出可唯一反解的键；默认包键带 "0:" 前缀。
            var cases = new (string pkg, string loc)[]
            {
                ("P", "loc"),
                ("P", "1:P/loc"),
                ("1:P", "loc"),
                ("P", "P/loc"),
                ("P", ""),
                ("", "loc"),
                ("P:x", "y:z"),
                ("P", "x/3:P/y"),
                ("P", "0:looks-like-default"),
                ("0:", "abc"),
            };

            var seen = new System.Collections.Generic.Dictionary<string, (string pkg, string loc)>();
            foreach (var (pkg, loc) in cases)
            {
                string key = string.IsNullOrEmpty(pkg)
                    ? $"0:{loc}"
                    : ResourceModule.BuildPackageCacheKey(pkg, loc);

                if (seen.TryGetValue(key, out var prev))
                {
                    Assert.Fail($"Key collision between ({prev.pkg}, {prev.loc}) and ({pkg}, {loc}): {key}");
                }

                seen[key] = (pkg, loc);

                if (string.IsNullOrEmpty(pkg))
                {
                    // 默认包键："0:" 前缀 + location 原文，且不可被解析成长度 ≥1 的自定义键。
                    StringAssert.StartsWith("0:", key);
                    Assert.AreEqual($"0:{loc}", key);
                    continue;
                }

                // 自定义包键唯一反解：数字长度前缀 + ':' + pkg + '/' + location 原文。
                int colon = key.IndexOf(':');
                Assert.GreaterOrEqual(colon, 0);
                Assert.True(int.TryParse(key.Substring(0, colon), out int length));
                Assert.Greater(length, 0, "自定义包键长度前缀至少为 1，不得与默认包 '0:' 前缀混淆。");
                string pkgPart = key.Substring(colon + 1, length);
                Assert.AreEqual(pkg, pkgPart);
                Assert.AreEqual('/', key[colon + 1 + length]);
                Assert.AreEqual(loc, key.Substring(colon + 1 + length + 1));
            }
        }
    }
}
