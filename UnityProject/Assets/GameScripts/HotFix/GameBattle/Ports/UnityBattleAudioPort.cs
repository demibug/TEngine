using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TEngine;
using AudioType = TEngine.AudioType;
using UnityEngine;
using YooAsset;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.3 返工：UnityBattleAudioPort —— IBattleAudioPort 的 Unity 真实实现
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Ports/IBattleAudioPort.cs）：
    //   把逻辑层发出的音频意图（PlayBgm / PlaySfx / StopBgm）翻译成 TEngine
    //   AudioModule 的实际播放调用。本类型持有 audioId → YooAsset 资源路径的
    //   映射表，逻辑层只传稳定字符串 ID，不接触 AudioClip / AudioSource。
    //
    // 依赖说明（不受 asmdef 限制）：
    //   - GameBattle.asmdef 的 noEngineReferences=false，默认引用 UnityEngine。
    //   - asmdef references 包含 TEngine.Runtime（GUID:e34a5702dd353724aa315fb8011f08c3），
    //     因此可直接使用 TEngine.ModuleSystem.GetModule&lt;IAudioModule&gt;() 获取音频模块
    //     与 YooAsset.AssetHandle。
    //   - 不使用 GameLogic.GameModule.Audio（GameBattle 未引用 GameLogic，且 GameLogic
    //     反向引用 GameBattle，使用 GameModule.Audio 会造成循环依赖）。改为通过
    //     TEngine.ModuleSystem.GetModule&lt;IAudioModule&gt;() 获取，该 API 属 TEngine.Runtime。
    //   - 本类型不依赖 FairyGUI 程序集，符合校验报告中"IBattleAudioPort 的 Unity
    //     真实实现不受 asmdef 限制"的结论。
    //
    // 异步语义（任务 7.3）：
    //   - PreloadAsync 通过 UniTask.Yield 完成（音频由 AudioModule 共享池异步加载），
    //     不依赖取消令牌。已加载的 AssetHandle 留在 AudioModule 的
    //     AudioClipPool 中由 AudioModule 自身管理（TEngine AudioModule 的池是
    //     跨局共享的应用级资源，不由本端口单局释放）。
    //   - PlayBgm / PlaySfx / StopBgm / Clear 为同步意图调用，对应 AudioModule
    //     的同步 API。AudioModule.Play 内部按 bAsync 参数决定是否异步加载
    //     AudioClip，本实现使用 bAsync=true 避免主线程同步 IO。
    //
    // 资源所有权（design.md 决策 0.11 / task 7.6）：
    //   - AudioClip 预加载通过 AudioModule.PutInAudioPool 完成，加载后的
    //     AssetHandle 由 AudioModule.AudioClipPool 持有，属应用级共享资源。
    //     本端口不在 BattleRuntimeScope 登记 AssetHandle 所有权，避免战斗
    //     退出时释放跨局共享的音频资源（spec "Exit releases battle-owned
    //     state"：只释放战斗专属资源，不释放应用级配置/资源）。
    //   - 若后续 task 7.6 要求每局独立加载音频并随局释放，可改为
    //     ModuleSystem.GetModule<IResourceModule>().LoadAssetAsyncHandle + TrackAssetHandle 模式。
    //     当前实现采用 AudioModule 共享池模式，符合 TEngine 音频模块设计。
    //     task 7.6 已确认：音频资源属应用级共享池，不随单局释放，所有权记录在
    //     BattleRuntimeScope 中以 ResourceLease 类别标注"AudioModule 共享池（非单局释放）"。
    //
    // 不变量：
    //   1. 逻辑层单向调用：本类型只接收音频意图，不回写规则状态。
    //   2. 不持有逻辑层引用：本类型不引用 BattleState / Manager / 实体。
    //   3. 异步操作不依赖取消令牌：PreloadAsync 通过 Yield 完成。
    //   4. 线程安全不要求：所有调用在 Unity 主线程的 Runtime 串行队列中执行。
    //   5. 幂等清理：StopBgm / Clear 可重复调用。
    //
    // TODO task 7.4/7.6：
    //   - 由 BattlePresenter 在 Entering 阶段调用 PreloadAsync。
    //   - 由 BattleModule 在 Exit 时调用 Clear 停止战斗专属音频。
    //   - 资源映射表 _audioPathMap 当前使用占位路径，task 7.6 接入真实 YooAsset
    //     资源地址后替换。当前占位路径使本实现可编译且在运行时安全降级
    //     （AudioModule.Play 找不到资源时返回 null AudioAgent，不抛异常）。
    // ============================================================================

    /// <summary>
    /// <see cref="IBattleAudioPort"/> 的 Unity 真实实现，基于 TEngine <c>IAudioModule</c>。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:215）：</b>把逻辑层音频意图翻译成 TEngine AudioModule
    /// 的实际播放调用，不回写规则状态。</para>
    ///
    /// <para><b>意图式设计：</b>本类型持有 <c>audioId → YooAsset 资源路径</c> 映射表，
    /// 逻辑层只传稳定字符串 ID。具体资源路径在 task 7.6 接入真实 YooAsset 地址后替换占位。</para>
    ///
    /// <para><b>异步（任务 7.3）：</b>PreloadAsync 通过 Yield 完成，不依赖取消令牌。
    /// 迟到音频回调经 Clear 幂等停止失效。</para>
    ///
    /// <para><b>资源所有权（决策 0.11）：</b>AudioClip 由 AudioModule 共享池持有，属应用级
    /// 资源，不由本端口随单局释放。本端口不在 BattleRuntimeScope 登记 AssetHandle。</para>
    ///
    /// <para><b>模块访问：</b>通过 <c>TEngine.ModuleSystem.GetModule&lt;IAudioModule&gt;()</c>
    /// 获取音频模块，不通过 <c>GameLogic.GameModule.Audio</c>（避免 GameBattle→GameLogic
    /// 循环依赖）。</para>
    ///
    /// <para><b>线程安全：</b>不要求。所有调用在 Unity 主线程的 Runtime 串行队列中执行。</para>
    /// </remarks>
    internal sealed class UnityBattleAudioPort : IBattleAudioPort
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[UnityBattleAudioPort]";

        // ====================================================================
        // 资源映射表（audioId → YooAsset 资源路径）
        // ====================================================================

        /// <summary>
        /// BGM 标识到 YooAsset 资源路径的映射。
        /// <para>task 7.6 接入真实资源地址后替换占位路径。占位路径在 AudioModule 找不到
        /// 资源时返回 null AudioAgent，不抛异常，实现安全降级。</para>
        /// </summary>
        private readonly Dictionary<string, string> _bgmPathMap = new Dictionary<string, string>
        {
            { "battle_normal", "Audio/BGM/battle_normal" },
        };

        /// <summary>
        /// SFX 标识到 YooAsset 资源路径的映射。
        /// </summary>
        private readonly Dictionary<string, string> _sfxPathMap = new Dictionary<string, string>
        {
            { "hit_melee", "Audio/SFX/hit_melee" },
            { "hit_arrow", "Audio/SFX/hit_arrow" },
            { "hit_pike", "Audio/SFX/hit_pike" },
            { "enemy_die", "Audio/SFX/enemy_die" },
            { "unit_die", "Audio/SFX/unit_die" },
            { "unit_place", "Audio/SFX/unit_place" },
            { "refresh", "Audio/SFX/refresh" },
            { "attack_knife", "Audio/SFX/attack_knife" },
            { "attack_bow", "Audio/SFX/attack_bow" },
            { "attack_spear", "Audio/SFX/attack_spear" },
            { "attack_cavalry", "Audio/SFX/attack_cavalry" },
            { "enemy_spawn", "Audio/SFX/enemy_spawn" },
        };

        /// <summary>
        /// 当前活动 BGM 的 AudioAgent（用于 StopBgm 精确停止）。
        /// <para>AudioModule.Stop(AudioType.Music, ...) 会停止整类 BGM，本字段用于
        /// 追踪本端口启动的 BGM 以便 Clear 时停止。null 表示当前无 BGM。</para>
        /// </summary>
        private AudioAgent _activeBgmAgent;

        /// <summary>
        /// 标记是否已 Preload，避免重复预加载。
        /// </summary>
        private bool _preloaded;

        // ====================================================================
        // 模块访问辅助
        // ====================================================================

        /// <summary>
        /// 获取 TEngine 音频模块。模块未注册时返回 null（降级为空操作）。
        /// <para>通过 <c>ModuleSystem.GetModule&lt;IAudioModule&gt;()</c> 获取，不依赖
        /// GameLogic.GameModule（避免 GameBattle→GameLogic 循环依赖）。</para>
        /// </summary>
        private static IAudioModule GetAudioModule()
        {
            try
            {
                return ModuleSystem.GetModule<IAudioModule>();
            }
            catch (Exception)
            {
                // 模块未注册时 ModuleSystem.GetModule 抛 GameFrameworkException，降级返回 null。
                return null;
            }
        }

        // ====================================================================
        // 生命周期与预加载
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// Unity 实现：通过 <c>IAudioModule.PutInAudioPool</c> 预加载 BGM/SFX 资源
        /// 到 AudioModule 的共享 AudioClipPool。预加载是异步的（AudioModule 内部
        /// LoadAssetAsyncHandle），但 PutInAudioPool 本身不返回 awaitable，本方法
        /// 等待一帧 Yield 后返回，使调用方能在下一帧安全使用音频资源。
        /// <para>幂等：重复调用安全，已预加载则直接返回。</para>
        /// </remarks>
        public async UniTask PreloadAsync()
        {
            if (_preloaded)
            {
                return;
            }

            IAudioModule audio = GetAudioModule();
            if (audio == null)
            {
                Log.Warning($"{LogTag} 音频模块未初始化，PreloadAsync 降级为空操作");
                _preloaded = true;
                return;
            }

            // 收集全部需预加载的资源路径。
            var paths = new List<string>(_bgmPathMap.Count + _sfxPathMap.Count);
            foreach (var kvp in _bgmPathMap)
            {
                paths.Add(kvp.Value);
            }
            foreach (var kvp in _sfxPathMap)
            {
                paths.Add(kvp.Value);
            }

            // AudioModule.PutInAudioPool 内部对每个 path 调用 LoadAssetAsyncHandle<AudioClip>
            // 并在 Completed 回调中加入 AudioClipPool。本方法不持有这些 AssetHandle，
            // 它们由 AudioModule 共享池管理（应用级资源，不随单局释放）。
            audio.PutInAudioPool(paths);

            // 等待一帧使异步加载启动；完整加载在 AudioModule 内部异步完成。
            await UniTask.Yield();

            _preloaded = true;
            Log.Info($"{LogTag} 预加载完成，BGM={_bgmPathMap.Count} SFX={_sfxPathMap.Count}");
        }

        // ====================================================================
        // BGM 意图
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// Unity 实现：通过 <c>IAudioModule.Play(AudioType.Music, path, loop, 1f, bAsync:true, bInPool:true)</c>
        /// 播放 BGM。AudioModule 未初始化或资源路径未映射时降级为空操作。
        /// </remarks>
        public void PlayBgm(string bgmId, bool loop)
        {
            IAudioModule audio = GetAudioModule();
            if (audio == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(bgmId) || !_bgmPathMap.TryGetValue(bgmId, out string path))
            {
                Log.Warning($"{LogTag} PlayBgm 未知 bgmId={bgmId}，跳过");
                return;
            }

            // 停止当前 BGM 再播放新的，避免重叠。
            if (_activeBgmAgent != null)
            {
                audio.Stop(AudioType.Music, fadeout: false);
                _activeBgmAgent = null;
            }

            _activeBgmAgent = audio.Play(AudioType.Music, path, bLoop: loop, volume: 1f, bAsync: true, bInPool: true);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Unity 实现：通过 <c>IAudioModule.Stop(AudioType.Music, fadeOut)</c> 停止 BGM。
        /// 幂等：无活动 BGM 时为空操作。
        /// </remarks>
        public void StopBgm(bool fadeOut)
        {
            IAudioModule audio = GetAudioModule();
            if (audio == null)
            {
                return;
            }

            if (_activeBgmAgent == null)
            {
                return;
            }

            audio.Stop(AudioType.Music, fadeout: fadeOut);
            _activeBgmAgent = null;
        }

        // ====================================================================
        // SFX 意图
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// Unity 实现：通过 <c>IAudioModule.Play(AudioType.Sound, path, loop:false, volume, bAsync:true, bInPool:true)</c>
        /// 播放一次性 SFX。AudioModule 未初始化或资源路径未映射时降级为空操作。
        /// <para>限流/去重由 AudioModule 内部 max发声数 + fadeout 复用机制承担
        /// （IAudioModule.Play 备注："超过最大发声数采用 fadeout 方式复用最久播放的 AudioSource"）。</para>
        /// </remarks>
        public void PlaySfx(string sfxId, float volumeScale = 1f)
        {
            IAudioModule audio = GetAudioModule();
            if (audio == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(sfxId) || !_sfxPathMap.TryGetValue(sfxId, out string path))
            {
                Log.Warning($"{LogTag} PlaySfx 未知 sfxId={sfxId}，跳过");
                return;
            }

            // AudioModule.Play 返回 AudioAgent，超过最大发声数时内部 fadeout 复用最久的。
            // SFX 不需追踪返回的 AudioAgent，播放后由 AudioModule 管理。
            float volume = Mathf.Clamp01(volumeScale);
            audio.Play(AudioType.Sound, path, bLoop: false, volume: volume, bAsync: true, bInPool: true);
        }

        // ====================================================================
        // 清理
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// Unity 实现：停止本端口播放的全部音频（BGM + SFX）。
        /// <para>注意：AudioModule.StopAll 会停止所有类型的音频，包括其他模块播放的。
        /// 本实现只停止 BGM（Music）和 SFX（Sound）两类，避免影响 Voice/UI Sound。
        /// 幂等：重复调用安全。</para>
        /// <para>资源卸载：AudioClip 由 AudioModule 共享池持有，不在此释放
        /// （应用级资源，决策 0.11）。</para>
        /// </remarks>
        public void Clear()
        {
            IAudioModule audio = GetAudioModule();
            if (audio == null)
            {
                _activeBgmAgent = null;
                return;
            }

            // 停止 BGM。
            if (_activeBgmAgent != null)
            {
                audio.Stop(AudioType.Music, fadeout: false);
                _activeBgmAgent = null;
            }

            // 停止 SFX（Sound 类）。
            audio.Stop(AudioType.Sound, fadeout: false);

            Log.Info($"{LogTag} Clear 完成（停止 BGM + SFX）");
        }
    }
}
