using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 枪兵武器表现组件：独立于角色本体的"木"字旁枪身显示、朝向与突刺动画。
    /// </summary>
    /// <remarks>
    /// <para><b>职责边界（最小武器表现）：</b>只负责枪身的显示、朝向（绕 WeaponPivot
    /// 旋转）与突刺动画。枪兵角色本体不因攻击目标旋转/翻转。</para>
    /// <para><b>时序对齐 <see cref="PikeAttackEffect"/>（数值一致，单位换算为秒）：</b></para>
    /// <list type="bullet">
    /// <item>0–90ms：武器从当前角度转向目标（<see cref="RotateDurationSeconds"/>）。</item>
    /// <item>90–360ms：沿自身局部上方向前突刺（<see cref="ThrustDurationSeconds"/>）。</item>
    /// <item>360ms：显示枪尖效果 PikeTip（对应逻辑命中点）。</item>
    /// <item>360–480ms：武器回收、隐藏 PikeTip（<see cref="RecoverDurationSeconds"/>）。</item>
    /// <item>进入待机后角度缓慢回到默认竖直。</item>
    /// </list>
    /// <para><b>连续攻击覆盖：</b>新攻击覆盖未完成的武器动画，先复位位置，再从当前
    /// 角度转向新目标，不允许多个武器动画状态并行。</para>
    /// <para><b>实现约束：</b>使用 <c>Update + Time.deltaTime</c> 状态机，不使用 Coroutine。</para>
    /// <para><b>可见性：</b>类与 <see cref="Bind"/> 为 public，供 Editor 程序集
    /// （LayaBattlePrefabImporter）在生成 Prefab 时绑定层级引用。</para>
    /// </remarks>
    public sealed class SpearWeaponView : MonoBehaviour
    {
        // ====================================================================
        // 枪突刺表现常量（写死，对齐原工程 Tween 链时序）。
        // TODO：接入兵种表现偏移/时序配置（Luban 或兵种表现表）后移除硬编码，
        // 后续不同兵种的武器突刺距离/时长应可按类型独立配置。
        // ====================================================================

        /// <summary>旋转段时长（秒，对应 PikeAttackEffect.PikeAttackRotateMs=90ms）。</summary>
        private const float RotateDurationSeconds = 90f / 1000f;

        /// <summary>突刺段时长（秒，对应 PikeAttackEffect.PikeAttackThrustMs=270ms）。</summary>
        private const float ThrustDurationSeconds = 270f / 1000f;

        /// <summary>回收段时长（秒，对应命中后 120ms 回收）。</summary>
        private const float RecoverDurationSeconds = 120f / 1000f;

        /// <summary>突刺位移距离（世界单位，沿 WeaponPivot 局部上方向）。</summary>
        private const float ThrustDistance = 0.4f;

        /// <summary>待机时角度回归速度（度/秒）。</summary>
        private const float IdleReturnRateDegreesPerSecond = 120f;

        /// <summary>默认竖直角度（度，对应枪尖朝上）。</summary>
        private const float DefaultAngleDegrees = 0f;

        private enum State
        {
            /// <summary>待机：武器角度缓慢回归默认竖直。</summary>
            Idle,

            /// <summary>旋转段：从当前角度转向目标角度。</summary>
            Rotating,

            /// <summary>突刺段：沿局部上方向前突刺。</summary>
            Thrusting,

            /// <summary>回收段：突刺结束，武器回到初始位置并隐藏枪尖。</summary>
            Recovering,
        }

        [SerializeField] private Transform _weaponPivot;
        [SerializeField] private Transform _pikeBody;
        [SerializeField] private Transform _pikeTip;
        [SerializeField] private Vector3 _pikeBodyLocalPos;
        private float _aimDegrees;
        private float _currentAngleDegrees;
        private float _startAngleDegrees;
        private float _elapsed;
        private State _state;

        /// <summary>
        /// 绑定武器层级引用（由 Prefab 生成器/importer 在生成时调用）。
        /// </summary>
        /// <param name="weaponPivot">武器旋转挂点（仅旋转此节点）。</param>
        /// <param name="pikeBody">枪身（pike.png，突刺时位移）。</param>
        /// <param name="pikeTip">枪尖效果（pikeEff1.png，默认隐藏）。</param>
        public void Bind(Transform weaponPivot, Transform pikeBody, Transform pikeTip)
        {
            _weaponPivot = weaponPivot;
            _pikeBody = pikeBody;
            _pikeTip = pikeTip;

            _pikeBodyLocalPos = _pikeBody.localPosition;

            _currentAngleDegrees = DefaultAngleDegrees;
            _aimDegrees = DefaultAngleDegrees;
            _state = State.Idle;
            _elapsed = 0f;

            if (_weaponPivot != null)
            {
                _weaponPivot.localRotation = Quaternion.Euler(0f, 0f, _currentAngleDegrees);
            }

            if (_pikeBody != null)
            {
                _pikeBody.localPosition = _pikeBodyLocalPos;
            }

            if (_pikeTip != null)
            {
                _pikeTip.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 设置武器瞄准角度（绕 WeaponPivot 旋转，只影响枪，不带动角色）。
        /// </summary>
        /// <param name="angleDegrees">角度（度，DisplayAngle 语义：0°朝上/90°朝右）。</param>
        internal void SetAim(float angleDegrees)
        {
            _aimDegrees = angleDegrees;
            if (_state == State.Idle)
            {
                // 待机时直接转向目标。
                _startAngleDegrees = _currentAngleDegrees;
                _elapsed = 0f;
                _state = State.Rotating;
            }
        }

        /// <summary>
        /// 播放突刺攻击表现（对齐 PikeAttackEffect 命中时机）。
        /// </summary>
        /// <remarks>
        /// <para>连续攻击时覆盖未完成的动画：先复位位置，再从当前角度转向新目标，
        /// 不允许多个武器动画状态并行。</para>
        /// </remarks>
        internal void PlayAttack()
        {
            // 覆盖未完成动画：复位位置并进入旋转段。
            if (_pikeBody != null)
            {
                _pikeBody.localPosition = _pikeBodyLocalPos;
            }

            if (_pikeTip != null)
            {
                _pikeTip.gameObject.SetActive(false);
            }

            _startAngleDegrees = _currentAngleDegrees;
            _elapsed = 0f;
            _state = State.Rotating;
        }

        /// <summary>
        /// 池化复位：角度归零、位置归位、隐藏枪尖。
        /// </summary>
        internal void ResetView()
        {
            _aimDegrees = DefaultAngleDegrees;
            _currentAngleDegrees = DefaultAngleDegrees;
            _state = State.Idle;
            _elapsed = 0f;

            if (_weaponPivot != null)
            {
                _weaponPivot.localRotation = Quaternion.Euler(0f, 0f, DefaultAngleDegrees);
            }

            if (_pikeBody != null)
            {
                _pikeBody.localPosition = _pikeBodyLocalPos;
            }

            if (_pikeTip != null)
            {
                _pikeTip.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            switch (_state)
            {
                case State.Idle:
                    UpdateIdle(Time.deltaTime);
                    break;
                case State.Rotating:
                    UpdateRotating(Time.deltaTime);
                    break;
                case State.Thrusting:
                    UpdateThrusting(Time.deltaTime);
                    break;
                case State.Recovering:
                    UpdateRecovering(Time.deltaTime);
                    break;
            }
        }

        private void UpdateIdle(float deltaSeconds)
        {
            // 待机时角度缓慢回归默认竖直。
            float targetAngle = DefaultAngleDegrees;
            if (Mathf.Abs(_currentAngleDegrees - targetAngle) <= 0.5f)
            {
                _currentAngleDegrees = targetAngle;
                return;
            }

            float step = IdleReturnRateDegreesPerSecond * deltaSeconds;
            _currentAngleDegrees = Mathf.MoveTowards(
                _currentAngleDegrees, targetAngle, step);
            ApplyPivotRotation(_currentAngleDegrees);
        }

        private void UpdateRotating(float deltaSeconds)
        {
            _elapsed += deltaSeconds;
            float progress = Mathf.Clamp01(_elapsed / RotateDurationSeconds);
            // 用 LerpAngle 沿最短弧线插值，避免 179°→-179° 等跨 0° 时绕远路旋转一整圈。
            _currentAngleDegrees = Mathf.LerpAngle(
                _startAngleDegrees, _aimDegrees, progress);
            ApplyPivotRotation(_currentAngleDegrees);

            if (progress >= 1f)
            {
                _currentAngleDegrees = _aimDegrees;
                // 旋转完成进入突刺段。
                _elapsed = 0f;
                _state = State.Thrusting;
            }
        }

        private void UpdateThrusting(float deltaSeconds)
        {
            _elapsed += deltaSeconds;
            float progress = Mathf.Clamp01(_elapsed / ThrustDurationSeconds);
            if (_pikeBody != null)
            {
                // localPosition 使用 WeaponPivot 局部坐标，沿本地 Y 轴突刺。
                // WeaponPivot 自身的旋转会将该局部位移自动映射到武器瞄准方向。
                float offset = ThrustDistance * Mathf.Sin(progress * Mathf.PI);
                _pikeBody.localPosition = _pikeBodyLocalPos + Vector3.up * offset;
            }

            // 命中点（90ms 旋转 + 270ms 突刺完成，即 Thrusting 结束）显示枪尖。
            if (progress >= 1f && _pikeTip != null)
            {
                _pikeTip.gameObject.SetActive(true);
                _elapsed = 0f;
                _state = State.Recovering;
            }
        }

        private void UpdateRecovering(float deltaSeconds)
        {
            _elapsed += deltaSeconds;
            if (_elapsed >= RecoverDurationSeconds)
            {
                // 回收完成：位置归位、隐藏枪尖、进入待机。
                if (_pikeBody != null)
                {
                    _pikeBody.localPosition = _pikeBodyLocalPos;
                }

                if (_pikeTip != null)
                {
                    _pikeTip.gameObject.SetActive(false);
                }

                _state = State.Idle;
            }
        }

        private void ApplyPivotRotation(float angleDegrees)
        {
            if (_weaponPivot != null)
            {
                _weaponPivot.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
            }
        }
    }
}
