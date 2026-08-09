using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 敌人头顶血条表现组件：承载血条节点缓存、填充更新与"显示—延时隐藏"计时。
    /// </summary>
    /// <remarks>
    /// <para><b>职责边界（不进入纯逻辑层）：</b>本组件挂在敌人 Prefab 根节点，
    /// 只负责血条的视觉状态（填充比例、显隐、隐藏计时），不持有/不修改敌人逻辑
    /// 血量。逻辑血量由 <see cref="EnemyBase"/> 维护，本组件只消费表现层传入的
    /// 当前/最大血量比例（design 决策 4 / design.md:9：逻辑层不依赖表现组件）。</para>
    /// <para><b>填充实现：</b>血条填充使用 <c>SpriteRenderer</c>（<c>drawMode=Sliced</c>），
    /// 通过缩放 <c>size.x</c> 实现"左侧不动、右侧收缩"。默认期望层级
    /// <c>VisualRoot/hpBgImg/hpImg1</c>（可选 hpImg2 作底）。</para>
    /// <para><b>显示—延时隐藏：</b>首次受击显示血条，之后每次受击更新比例并重置
    /// 隐藏计时；倒计时（<see cref="HideDelaySeconds"/>）结束自动隐藏。死亡/回池/
    /// 重生由调用方调用 <see cref="ResetAndHide"/> 立即隐藏并复位，保证复用不残留
    /// 旧比例。</para>
    /// <para><b>可见性：</b>类与 <see cref="Bind"/> 为 public，供 Editor 程序集
    /// （LayaBattlePrefabImporter）在生成 Prefab 时绑定层级引用。</para>
    /// </remarks>
    public sealed class EnemyHealthBarView : MonoBehaviour
    {
        /// <summary>受击后无新受击的隐藏延时（秒）。</summary>
        private const float HideDelaySeconds = 1.5f;

        /// <summary>血条背景节点（hpBgImg）。</summary>
        private SpriteRenderer _background;

        /// <summary>血条填充节点（hpImg1，血量比例作用于此）。</summary>
        private SpriteRenderer _fill;

        /// <summary>填充节点初始 Sliced 宽度（世界单位），用于按比例缩放。</summary>
        private float _fillBaseWidth;

        /// <summary>当前填充比例（0~1）。</summary>
        private float _ratio;

        /// <summary>是否已显示血条。</summary>
        private bool _visible;

        /// <summary>隐藏计时剩余时间（秒）。</summary>
        private float _hideTimer;

        /// <summary>
        /// 绑定血条节点层级引用。由运行时表现层（UnityBattleViewPort.OnEnemySpawned）
        /// 在敌人生成时调用。
        /// </summary>
        /// <param name="background">血条背景 SpriteRenderer（不可为 null）。</param>
        /// <param name="fill">血条填充 SpriteRenderer（不可为 null，drawMode=Sliced）。</param>
        /// <exception cref="System.ArgumentNullException">任一参数为 null。</exception>
        /// <remarks>
        /// <para>调用时机：敌人生成时由 UnityBattleViewPort 动态 AddComponent 后 Bind，
        /// Prefab 中不序列化绑定数据。</para>
        /// <para>填充初始宽度取自 <c>renderer.size.x</c>，运行时按比例缩放。
        /// 若填充使用 Simple drawMode（固定尺寸素材），缩放 <c>size.x</c> 不生效，
        /// 需改缩放 <c>transform.localScale.x</c> 或改用 Sliced 素材。</para>
        /// </remarks>
        public void Bind(SpriteRenderer background, SpriteRenderer fill)
        {
            if (background == null)
            {
                throw new System.ArgumentNullException(nameof(background));
            }

            if (fill == null)
            {
                throw new System.ArgumentNullException(nameof(fill));
            }

            _background = background;
            _fill = fill;
            _fillBaseWidth = fill.size.x;
            ResetAndHide();
        }

        /// <summary>
        /// 只更新填充比例，不显示血条、不重置隐藏计时。
        /// </summary>
        /// <param name="ratio">血量比例（当前/最大，调用方保证 0~1）。</param>
        /// <remarks>
        /// <para>供表现同步器每帧调用：即使血条处于隐藏状态也保持填充宽度准确，
        /// 但不会因此刷新隐藏计时（否则血条永不隐藏）。</para>
        /// <para>隐藏时更新填充宽度不会重新显示（<see cref="_visible"/> 不受影响）。</para>
        /// </remarks>
        public void SetRatio(float ratio)
        {
            _ratio = Mathf.Clamp01(ratio);
            if (_fill != null)
            {
                Vector2 size = _fill.size;
                size.x = _fillBaseWidth * _ratio;
                _fill.size = size;
            }
        }

        /// <summary>
        /// 按真实血量比例更新填充并显示血条，重置隐藏计时。
        /// </summary>
        /// <param name="ratio">血量比例（当前/最大，调用方保证 0~1）。</param>
        /// <remarks>
        /// <para>只更新填充与显隐计时，不负责重新显示血条以外的表现。</para>
        /// <para>比例直接写入填充宽度：<c>fill.size.x = baseWidth * ratio</c>，
        /// 保持左边缘不动、右边缘收缩（Sliced 拉伸）。</para>
        /// <para>每次有效受击调用本方法都会重置隐藏计时，连续受击不闪烁。</para>
        /// </remarks>
        public void ShowWithRatio(float ratio)
        {
            SetRatio(ratio);

            if (_background != null)
            {
                _background.enabled = true;
            }

            if (_fill != null)
            {
                _fill.enabled = true;
            }

            _visible = true;
            _hideTimer = HideDelaySeconds;
        }

        /// <summary>
        /// 每帧推进隐藏计时，超时自动隐藏血条。
        /// </summary>
        /// <param name="deltaSeconds">本帧时长（秒）。</param>
        /// <remarks>
        /// <para>由 Unity 帧驱动（本组件自身 <see cref="Update"/> 调用），也可由外部
        /// 表现同步器统一驱动（幂等）。超时后隐藏背景与填充，但不改变已记录的
        /// <see cref="Ratio"/>（下次 ShowWithRatio 直接恢复）。</para>
        /// </remarks>
        public void Tick(float deltaSeconds)
        {
            if (!_visible || deltaSeconds <= 0f)
            {
                return;
            }

            _hideTimer -= deltaSeconds;
            if (_hideTimer <= 0f)
            {
                HideNow();
            }
        }

        /// <summary>
        /// 立即隐藏并复位填充比例，供死亡/回池/重生调用。
        /// </summary>
        /// <remarks>
        /// <para>清空隐藏计时、隐藏背景与填充，并将填充宽度重置为初始全宽，
        /// 保证池复用不残留旧比例或显示状态。幂等。</para>
        /// </remarks>
        public void ResetAndHide()
        {
            _hideTimer = 0f;
            _visible = false;
            _ratio = 1f;

            if (_background != null)
            {
                _background.enabled = false;
            }

            if (_fill != null)
            {
                Vector2 size = _fill.size;
                size.x = _fillBaseWidth;
                _fill.size = size;
                _fill.enabled = false;
            }
        }

        /// <summary>当前填充比例（诊断用，0~1）。</summary>
        public float Ratio => _ratio;

        /// <summary>血条是否显示中（诊断用）。</summary>
        public bool IsVisible => _visible;

        /// <summary>隐藏计时剩余秒数（诊断用）。</summary>
        public float HideTimerRemaining => _hideTimer;

        /// <summary>
        /// Unity 帧驱动：推进隐藏计时。无新受击时自动隐藏血条。
        /// </summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>立即隐藏血条（清空计时）。</summary>
        private void HideNow()
        {
            _visible = false;
            _hideTimer = 0f;

            if (_background != null)
            {
                _background.enabled = false;
            }

            if (_fill != null)
            {
                _fill.enabled = false;
            }
        }
    }
}
