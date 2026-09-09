using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 音频数据。
    /// </summary>
    public class AudioData : MemoryObject
    {
        /// <summary>
        /// 资源句柄。
        /// </summary>
        public AssetHandle AssetHandle { private set; get; }

        /// <summary>
        /// 是否使用对象池。
        /// </summary>
        public bool InPool { private set; get; } = false;

        public override void InitFromPool()
        {
        }

        /// <summary>
        /// 回收到对象池。
        /// </summary>
        public override void RecycleToPool()
        {
            AssetHandle handle = AssetHandle;
            bool inPool = InPool;
            AssetHandle = null;
            InPool = false;

            if (!inPool && handle != null)
            {
                try
                {
                    handle.Dispose();
                }
                catch (System.Exception exception)
                {
                    try { Log.Error("Audio data handle cleanup failed: {0}", exception); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 生成音频数据。
        /// </summary>
        /// <param name="assetHandle">资源操作句柄。</param>
        /// <param name="inPool">是否使用对象池。</param>
        /// <returns>音频数据。</returns>
        internal static AudioData Alloc(AssetHandle assetHandle, bool inPool)
        {
            AudioData ret = MemoryPool.Acquire<AudioData>();
            ret.AssetHandle = assetHandle;
            ret.InPool = inPool;
            ret.InitFromPool();
            return ret;
        }

        /// <summary>
        /// 回收音频数据。
        /// </summary>
        /// <param name="audioData"></param>
        internal static void DeAlloc(AudioData audioData)
        {
            if (audioData != null)
            {
                try
                {
                    audioData.RecycleToPool();
                }
                finally
                {
                    MemoryPool.Release(audioData);
                }
            }
        }
    }
}
