using System;
using NUnit.Framework;
using TEngine;
using UnityEngine;

namespace TEngine.TwoStageHotUpdateTests
{
    /// <summary>
    /// 固定入口 JSON 和 URL 目录协议测试。
    /// </summary>
    public sealed class TwoStageReleaseEntryUrlTests
    {
        [Test]
        public void NewUpdateSetting_DefaultsToDirectDescriptor()
        {
            UpdateSetting setting = ScriptableObject.CreateInstance<UpdateSetting>();
            try
            {
                Assert.AreEqual(TwoStageReleaseSourceMode.DirectDescriptor, setting.TwoStageReleaseSourceMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(setting);
            }
        }

        [Test]
        public void EntryJson_ValidUnknownField_PassesValidation()
        {
            const string json = "{\"EntryVersion\":1,\"ReleaseId\":\"20260911-125528-8c7efe4f\",\"FutureField\":true}";

            bool parsed = UpdateReleaseEntryValidator.TryParse(
                json,
                out UpdateReleaseEntry entry,
                out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(1, entry.EntryVersion);
            Assert.AreEqual("20260911-125528-8c7efe4f", entry.ReleaseId);
        }

        [Test]
        public void EntryJson_NullCorruptAndUnknownVersion_AreRejected()
        {
            string[] invalidJson = { "null", "{}", "{", "{\"EntryVersion\":2,\"ReleaseId\":\"release-1\"}" };
            foreach (string json in invalidJson)
            {
                Assert.IsFalse(
                    UpdateReleaseEntryValidator.TryParse(json, out _, out string error),
                    json + "\n" + error);
            }
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("release.with.dot")]
        [TestCase("release/with/slash")]
        [TestCase("release%2Fencoded")]
        [TestCase("release with space")]
        [TestCase("https://example.invalid/release")]
        public void ReleaseId_IllegalValue_IsRejected(string releaseId)
        {
            Assert.IsFalse(UpdateReleaseEntryValidator.IsValidReleaseId(releaseId));
        }

        [Test]
        public void ReleaseId_OverMaximumLength_IsRejected()
        {
            Assert.IsFalse(UpdateReleaseEntryValidator.IsValidReleaseId(new string('a', 129)));
            Assert.AreEqual(4 * 1024, UpdateReleaseEntryValidator.MaximumEntryBytes);

            string oversizedJson = "{\"EntryVersion\":1,\"ReleaseId\":\"release-1\",\"FutureField\":\"" +
                                   new string('x', UpdateReleaseEntryValidator.MaximumEntryBytes) + "\"}";
            Assert.IsFalse(UpdateReleaseEntryValidator.TryParse(oversizedJson, out _, out string error), error);
        }

        [Test]
        public void FixedUrl_TrailingSlashAndCacheBust_DoNotPolluteDescriptorUrl()
        {
            const string entryUrl = "https://cdn.example.invalid/demo-v1/Windows64/default/DefaultPackage/current.json";
            const string releaseId = "20260911-125528-8c7efe4f";

            string descriptorUrl = TwoStageReleaseUrl.BuildDescriptorUrl(entryUrl, releaseId, false);
            string releaseRoot = TwoStageReleaseUrl.BuildReleaseRootUrl(
                "https://assets.example.invalid/demo-v1/Windows64/default/DefaultPackage/",
                releaseId,
                false);
            string requestUrl = TwoStageReleaseUrl.AppendCacheBust(entryUrl, Guid.NewGuid().ToString("N"));

            Assert.AreEqual(
                "https://cdn.example.invalid/demo-v1/Windows64/default/DefaultPackage/releases/" +
                releaseId + "/TwoStageRelease_" + releaseId + ".json",
                descriptorUrl);
            Assert.AreEqual(
                "https://assets.example.invalid/demo-v1/Windows64/default/DefaultPackage/releases/" + releaseId,
                releaseRoot);
            StringAssert.Contains("?ts=", requestUrl);
            StringAssert.DoesNotContain("?", descriptorUrl);
        }

        [Test]
        public void FixedUrl_RootEntryAndRootResource_AreCombinedWithLeadingSlash()
        {
            const string releaseId = "release-root";

            Assert.IsTrue(TwoStageReleaseUrl.TryValidateFixedEntryUrl(
                "https://cdn.example.invalid/current.json", false, out string error), error);
            Assert.AreEqual(
                "https://cdn.example.invalid/releases/release-root/TwoStageRelease_release-root.json",
                TwoStageReleaseUrl.BuildDescriptorUrl(
                    "https://cdn.example.invalid/current.json", releaseId, false));
            Assert.AreEqual(
                "https://cdn.example.invalid/releases/release-root",
                TwoStageReleaseUrl.BuildReleaseRootUrl(
                    "https://cdn.example.invalid/", releaseId, false));
        }

        [Test]
        public void FixedUrl_PortMultiLevelAndEscapedPath_PreserveAuthorityAndPath()
        {
            const string releaseId = "release-escaped";
            const string entryUrl = "HTTPS://CDN.Example.Invalid:8443/game%20data/demo/current.json";
            const string resourceRoot = "https://Assets.Example.Invalid:9443/game%20data/demo/";

            Assert.AreEqual(
                "HTTPS://CDN.Example.Invalid:8443/game%20data/demo/releases/release-escaped/" +
                "TwoStageRelease_release-escaped.json",
                TwoStageReleaseUrl.BuildDescriptorUrl(entryUrl, releaseId, false));
            Assert.AreEqual(
                "https://Assets.Example.Invalid:9443/game%20data/demo/releases/release-escaped",
                TwoStageReleaseUrl.BuildReleaseRootUrl(resourceRoot, releaseId, false));
        }

        [Test]
        public void FixedUrl_QueryFragmentUserInfoAndWrongEntryPath_AreRejected()
        {
            string[] invalidEntryUrls =
            {
                "https://example.invalid/current.json?cache=1",
                "https://example.invalid/current.json#fragment",
                "https://user:password@example.invalid/current.json",
                "https://example.invalid/releases/release-1.json",
            };

            foreach (string url in invalidEntryUrls)
            {
                Assert.IsFalse(
                    TwoStageReleaseUrl.TryValidateFixedEntryUrl(url, false, out string error),
                    url + "\n" + error);
            }

            Assert.IsFalse(TwoStageReleaseUrl.TryValidateFixedResourceRootUrl(
                "https://example.invalid/assets?cache=1", false, out _));
            Assert.IsFalse(TwoStageReleaseUrl.TryValidateFixedResourceRootUrl(
                "http://example.invalid/assets", false, out _));
            Assert.IsTrue(TwoStageReleaseUrl.TryValidateFixedResourceRootUrl(
                "http://127.0.0.1:8081/assets/", true, out _));
        }

        [Test]
        public void DirectUrl_LegacyQueryAndFragmentRemainAllowed()
        {
            Assert.IsTrue(TwoStageReleaseUrl.TryValidateTrustedUrl(
                "https://example.invalid/release.json?cache=1#fragment",
                false,
                out string error), error);
        }
    }
}
