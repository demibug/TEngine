using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// YooAsset 在解析缓存、主源或备源 manifest 前调用此服务。
    /// 只有实际进入解析器且原始字节摘要匹配时，才产生可用于 ActiveManifest 的信任记录。
    /// </summary>
    public sealed class PinnedManifestIntegrityVerifier : IManifestRestoreServices
    {
        private readonly string _packageName;
        private readonly string _packageVersion;
        private readonly string _manifestSha256;
        private bool _verified;

        public PinnedManifestIntegrityVerifier(
            string packageName, string packageVersion, string manifestSha256)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Pinned package name is empty.", nameof(packageName));
            if (string.IsNullOrWhiteSpace(packageVersion))
                throw new ArgumentException("Pinned package version is empty.", nameof(packageVersion));
            if (!IsSha256(manifestSha256))
                throw new ArgumentException("Pinned manifest SHA-256 is invalid.", nameof(manifestSha256));

            _packageName = packageName;
            _packageVersion = packageVersion;
            _manifestSha256 = manifestSha256;
        }

        byte[] IManifestRestoreServices.RestoreManifest(byte[] fileData)
        {
            if (!UpdateReleaseDescriptorValidator.HashMatches(fileData, _manifestSha256))
                throw new InvalidDataException(
                    $"Pinned manifest SHA-256 mismatch for '{_packageName}@{_packageVersion}'.");

            _verified = true;
            return fileData;
        }

        public void VerifyActivated(string packageName, string packageVersion, string manifestSha256)
        {
            if (!_verified ||
                !string.Equals(_packageName, packageName, StringComparison.Ordinal) ||
                !string.Equals(_packageVersion, packageVersion, StringComparison.Ordinal) ||
                !string.Equals(_manifestSha256, manifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Active manifest '{packageName}@{packageVersion}' has no matching verified byte record.");
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 远端资源地址查询服务类
    /// </summary>
    internal class RemoteServices : IRemoteServices
    {
        private readonly string _defaultHostServer;
        private readonly string _fallbackHostServer;

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }

        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return $"{_defaultHostServer}/{fileName}";
        }

        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            return $"{_fallbackHostServer}/{fileName}";
        }
    }
    
    /// <summary>
    /// 文件流加密方式
    /// </summary>
    public class FileStreamEncryption : IEncryptionServices
    {
        public EncryptResult Encrypt(EncryptFileInfo fileInfo)
        {
            var fileData = File.ReadAllBytes(fileInfo.FileLoadPath);
            for (int i = 0; i < fileData.Length; i++)
            {
                fileData[i] ^= BundleStream.KEY;
            }

            EncryptResult result = new EncryptResult();
            result.Encrypted = true;
            result.EncryptedData = fileData;
            return result;
        }
    }

    /// <summary>
    /// 资源文件流加载解密类
    /// </summary>
    public class FileStreamDecryption : IDecryptionServices
    {
        /// <summary>
        /// 同步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo)
        {
            BundleStream bundleStream =
                new BundleStream(fileInfo.FileLoadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = bundleStream;
            decryptResult.Result =
                AssetBundle.LoadFromStream(bundleStream, 0, GetManagedReadBufferSize());
            return decryptResult;
        }

        /// <summary>
        /// 异步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo)
        {
            BundleStream bundleStream =
                new BundleStream(fileInfo.FileLoadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = bundleStream;
            decryptResult.CreateRequest =
                AssetBundle.LoadFromStreamAsync(bundleStream, 0, GetManagedReadBufferSize());
            return decryptResult;
        }

        /// <summary>
        /// 后备方式获取解密的资源包对象
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleFallback(DecryptFileInfo fileInfo)
        {
            return new DecryptResult();
        }
        
        /// <summary>
        /// 获取解密的字节数据
        /// </summary>
        byte[] IDecryptionServices.ReadFileData(DecryptFileInfo fileInfo)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// 获取解密的文本数据
        /// </summary>
        string IDecryptionServices.ReadFileText(DecryptFileInfo fileInfo)
        {
            throw new System.NotImplementedException();
        }

        private static uint GetManagedReadBufferSize()
        {
            return 1024;
        }
    }

    /// <summary>
    /// 文件偏移加密方式
    /// </summary>
    public class FileOffsetEncryption : IEncryptionServices
    {
        public EncryptResult Encrypt(EncryptFileInfo fileInfo)
        {
            int offset = 32;
            byte[] fileData = File.ReadAllBytes(fileInfo.FileLoadPath);
            var encryptedData = new byte[fileData.Length + offset];
            Buffer.BlockCopy(fileData, 0, encryptedData, offset, fileData.Length);

            EncryptResult result = new EncryptResult();
            result.Encrypted = true;
            result.EncryptedData = encryptedData;
            return result;
        }
    }

    /// <summary>
    /// 资源文件偏移加载解密类
    /// </summary>
    public class FileOffsetDecryption : IDecryptionServices
    {
        /// <summary>
        /// 同步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo)
        {
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.Result =
                AssetBundle.LoadFromFile(fileInfo.FileLoadPath, 0, GetFileOffset());
            return decryptResult;
        }

        /// <summary>
        /// 异步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo)
        {
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.CreateRequest =
                AssetBundle.LoadFromFileAsync(fileInfo.FileLoadPath, 0, GetFileOffset());
            return decryptResult;
        }
        
        /// <summary>
        /// 后备方式获取解密的资源包对象
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleFallback(DecryptFileInfo fileInfo)
        {
            return new DecryptResult();
        }

        /// <summary>
        /// 获取解密的字节数据
        /// </summary>
        byte[] IDecryptionServices.ReadFileData(DecryptFileInfo fileInfo)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// 获取解密的文本数据
        /// </summary>
        string IDecryptionServices.ReadFileText(DecryptFileInfo fileInfo)
        {
            throw new System.NotImplementedException();
        }

        private static ulong GetFileOffset()
        {
            return 32;
        }
    }
    
    
    #region WebDecryptionServices
    /// <summary>
    /// 资源文件偏移加载解密类
    /// </summary>
    public class FileOffsetWebDecryption : IWebDecryptionServices
    {
        public WebDecryptResult LoadAssetBundle(WebDecryptFileInfo fileInfo)
        {
            int offset = GetFileOffset();
            byte[] decryptedData = new byte[fileInfo.FileData.Length - offset];
            Buffer.BlockCopy(fileInfo.FileData, offset, decryptedData, 0, decryptedData.Length);
            // 从内存中加载AssetBundle
            WebDecryptResult decryptResult = new WebDecryptResult();
            decryptResult.Result = AssetBundle.LoadFromMemory(decryptedData);
            return decryptResult;
        }

        private static int GetFileOffset()
        {
            return 32;
        }
    }
    
    public class FileStreamWebDecryption : IWebDecryptionServices
    {
        public WebDecryptResult LoadAssetBundle(WebDecryptFileInfo fileInfo)
        {
            // 优化：使用Buffer批量操作替代逐字节异或
            byte[] decryptedData = new byte[fileInfo.FileData.Length];
            Buffer.BlockCopy(fileInfo.FileData, 0, decryptedData, 0, fileInfo.FileData.Length);
            
            for (int i = 0; i < decryptedData.Length; i++)
            {
                decryptedData[i] ^= BundleStream.KEY;
            }

            WebDecryptResult decryptResult = new WebDecryptResult();
            decryptResult.Result = AssetBundle.LoadFromMemory(decryptedData);
            return decryptResult;
        }
    }
    #endregion
}

/// <summary>
/// 资源文件解密流
/// </summary>
public class BundleStream : FileStream
{
    public const byte KEY = 64;

    public BundleStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access,
        share)
    {
    }

    public BundleStream(string path, FileMode mode) : base(path, mode)
    {
    }

    public override int Read(byte[] array, int offset, int count)
    {
        var index = base.Read(array, offset, count);
        for (int i = 0; i < array.Length; i++)
        {
            array[i] ^= KEY;
        }
        return index;
    }
}
