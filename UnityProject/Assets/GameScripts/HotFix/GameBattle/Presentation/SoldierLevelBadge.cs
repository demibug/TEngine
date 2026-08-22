using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 战场单位等级数字表现（最终方案"战场单位等级数字表现"）。
    /// </summary>
    /// <remarks>
    /// <para>挂在战场士兵 Prefab 根节点上，用独立单图数字 Sprite 显示当前等级
    /// （1..MaxLevel）。等级变化由 <see cref="UnityBattleViewPort.OnUnitLevelChanged"/>
    /// 调用 <see cref="SetLevel"/> 更新。待上场卡的等级数字由 BattleHudPanel 管理。</para>
    /// <para>数字 Sprite 数组由 <see cref="UnityBattleViewPort"/> 预加载
    /// <c>Sprites/LevelBadge/level_number_1</c>..<c>level_number_8</c> 后经
    /// <see cref="Configure"/> 注入（index = level - 1）。未注入时退化为隐藏。</para>
    /// <para>标签仍是士兵子节点，但每帧在 <see cref="LateUpdate"/>（角色旋转经
    /// <c>SetBodyRotation</c> 结算之后）以世界坐标固定到角色左上方并保持世界旋转
    /// <c>Quaternion.identity</c>，因此根节点 Z 旋转时数字自身始终正向、不绕行。</para>
    /// </remarks>
    internal sealed class SoldierLevelBadge : MonoBehaviour
    {
        /// <summary>标签相对角色中心的世界空间左偏移（左上角）。</summary>
        private const float WorldOffsetX = -0.4f;

        /// <summary>标签相对角色中心的世界空间上偏移（头顶上方）。</summary>
        private const float WorldOffsetY = 0.55f;

        private SpriteRenderer _renderer;
        private Sprite[] _numberSprites;
        private int _level = 1;

        /// <summary>
        /// 注入数字 Sprite 数组（index = level - 1，如 index0 对应 level_number_1）。
        /// </summary>
        /// <param name="numberSprites">独立单图数字 Sprite 数组；可为 null（退化为隐藏）。</param>
        internal void Configure(Sprite[] numberSprites)
        {
            _numberSprites = numberSprites;
            EnsureRenderer();
            Refresh();
        }

        /// <summary>
        /// 设置并显示当前等级。
        /// </summary>
        /// <param name="level">当前等级（至少 1）。</param>
        internal void SetLevel(int level)
        {
            _level = level > 0 ? level : 1;
            EnsureRenderer();
            Refresh();
        }

        /// <summary>获取当前等级（诊断用）。</summary>
        internal int Level => _level;

        /// <summary>确保数字 SpriteRenderer 子物体存在。</summary>
        private void EnsureRenderer()
        {
            if (_renderer != null)
            {
                return;
            }

            // 在士兵根节点下创建等级数字标签（初始位置仅作兜底，随后由
            // SyncWorldPlacement 以世界坐标立即覆盖）。
            var labelObject = new GameObject("LevelBadge");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(WorldOffsetX, WorldOffsetY, 0f);

            _renderer = labelObject.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = 10;
            _renderer.enabled = false;

            // 创建即同步世界位置/旋转，避免首帧跟随根节点旋转闪动。
            SyncWorldPlacement();
        }

        /// <summary>
        /// 每帧在角色旋转结算后（LateUpdate 晚于驱动 SetBodyRotation 的 OnUpdate）执行，
        /// 以世界坐标固定标签到角色左上方并保持世界旋转为正，使其不随根节点 Z 旋转绕行。
        /// </summary>
        private void LateUpdate()
        {
            SyncWorldPlacement();
        }

        /// <summary>同步标签世界位置与旋转（根节点未旋转时为恒等，无额外开销）。</summary>
        private void SyncWorldPlacement()
        {
            if (_renderer == null)
            {
                return;
            }

            Transform labelTransform = _renderer.transform;
            // 世界空间偏移：角色中心 + 固定左上偏移，不依赖根节点朝向。
            labelTransform.position = transform.position + new Vector3(WorldOffsetX, WorldOffsetY, 0f);
            labelTransform.rotation = Quaternion.identity;
        }

        /// <summary>按当前等级刷新数字 Sprite。</summary>
        private void Refresh()
        {
            if (_renderer == null)
            {
                return;
            }

            int index = _level - 1;
            if (_numberSprites != null && index >= 0 && index < _numberSprites.Length
                && _numberSprites[index] != null)
            {
                _renderer.sprite = _numberSprites[index];
                _renderer.enabled = true;
            }
            else
            {
                _renderer.sprite = null;
                _renderer.enabled = false;
            }
        }
    }
}
