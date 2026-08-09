using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 战场单位等级数字表现（最终方案"战场单位等级数字表现"）。
    /// </summary>
    /// <remarks>
    /// <para>挂在战场士兵 Prefab 根节点上，显示当前等级数字（1..MaxLevel）。
    /// 等级变化由 <see cref="UnityBattleViewPort.OnUnitLevelChanged"/> 调用
    /// <see cref="SetLevel"/> 更新。待上场卡的等级数字由 BattleHudPanel 管理。</para>
    /// <para>真实美术使用 <c>number5.png</c> 切片；本期先用 TextMesh 占位显示等级数字，
    /// 后续接入位图字体时替换渲染方式（不改规则状态）。</para>
    /// </remarks>
    internal sealed class SoldierLevelBadge : MonoBehaviour
    {
        private TextMesh _levelText;
        private int _level = 1;

        /// <summary>
        /// 设置并显示当前等级。
        /// </summary>
        /// <param name="level">当前等级（至少 1）。</param>
        internal void SetLevel(int level)
        {
            _level = level > 0 ? level : 1;
            EnsureLabel();
            if (_levelText != null)
            {
                _levelText.text = _level.ToString();
            }
        }

        /// <summary>获取当前等级（诊断用）。</summary>
        internal int Level => _level;

        /// <summary>
        /// 确保等级标签子物体存在（懒惰创建，挂在根节点上方偏移位置）。
        /// </summary>
        private void EnsureLabel()
        {
            if (_levelText != null)
            {
                return;
            }

            // 在士兵根节点下创建等级标签，偏移到头顶上方。
            var labelObject = new GameObject("LevelBadge");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0.55f, 0f);

            _levelText = labelObject.AddComponent<TextMesh>();
            _levelText.anchor = TextAnchor.MiddleCenter;
            _levelText.alignment = TextAlignment.Center;
            _levelText.characterSize = 0.06f;
            _levelText.fontSize = 64;
            _levelText.color = Color.yellow;
            _levelText.text = _level.ToString();
        }
    }
}
