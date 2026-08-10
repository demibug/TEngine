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
    /// </remarks>
    internal sealed class SoldierLevelBadge : MonoBehaviour
    {
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

            // 在士兵根节点下创建等级数字标签，偏移到头顶上方。
            var labelObject = new GameObject("LevelBadge");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0.55f, 0f);

            _renderer = labelObject.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = 10;
            _renderer.enabled = false;
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
