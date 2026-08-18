using GameBattle;
using NUnit.Framework;
using UnityEngine;

namespace GameBattle.Tests.EditMode.Presentation
{
    /// <summary>
    /// 敌人头顶血条表现组件测试。
    /// </summary>
    [TestFixture]
    internal sealed class EnemyHealthBarViewTests
    {
        private const float Epsilon = 0.0001f;

        private GameObject _root;
        private Texture2D _texture;
        private Sprite _sprite;
        private SpriteRenderer _background;
        private SpriteRenderer _fill;
        private SpriteRenderer _standbyFill;
        private EnemyHealthBarView _healthBar;
        private float _fillBaseWidth;
        private Vector3 _fillBasePosition;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("EnemyHealthBar");
            _texture = new Texture2D(2, 2);
            _sprite = Sprite.Create(
                _texture,
                new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f),
                100f);

            _background = CreateRenderer("hpBgImg", new Vector3(0.5f, 0f, 0f), 0.775f);
            _fill = CreateRenderer("hpImg2", new Vector3(0f, 0f, 0.022f), 0.675f);
            _standbyFill = CreateRenderer("hpImg1", new Vector3(0f, 0f, 0.021f), 0.675f);
            _fillBaseWidth = _fill.size.x;
            _fillBasePosition = _fill.transform.localPosition;

            _healthBar = _root.AddComponent<EnemyHealthBarView>();
            _healthBar.Bind(_background, _fill, _standbyFill);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            if (_sprite != null)
            {
                Object.DestroyImmediate(_sprite);
            }

            if (_texture != null)
            {
                Object.DestroyImmediate(_texture);
            }
        }

        [Test]
        public void Bind_HidesAllRenderersAndResetsToFullHealth()
        {
            Assert.IsFalse(_background.enabled, "背景默认应隐藏。");
            Assert.IsFalse(_fill.enabled, "当前红色血量条默认应隐藏。");
            Assert.IsFalse(_standbyFill.enabled, "预留血量条默认应隐藏。");
            Assert.AreEqual(1f, _healthBar.Ratio, Epsilon, "默认比例应复位为满血。");
            Assert.AreEqual(_fillBaseWidth, _fill.size.x, Epsilon, "默认宽度应为满血宽度。");
            AssertVector3(_fillBasePosition, _fill.transform.localPosition, "默认位置应为满血位置。");
        }

        [Test]
        public void ShowWithRatio_ShrinksFromRightAndKeepsLeftEdge()
        {
            float fullLeft = _fillBasePosition.x - _fillBaseWidth * 0.5f;

            _healthBar.ShowWithRatio(0.5f);

            float currentLeft = _fill.transform.localPosition.x - _fill.size.x * 0.5f;
            Assert.IsTrue(_background.enabled, "受击后背景应显示。");
            Assert.IsTrue(_fill.enabled, "受击后当前红色血量条应显示。");
            Assert.IsFalse(_standbyFill.enabled, "本期预留血量条不应显示。");
            Assert.AreEqual(_fillBaseWidth * 0.5f, _fill.size.x, Epsilon, "半血宽度不正确。");
            Assert.AreEqual(fullLeft, currentLeft, Epsilon, "血条左边缘应保持不动。");
            Assert.AreEqual(1.5f, _healthBar.HideTimerRemaining, Epsilon, "受击应重置隐藏计时。");
        }

        [Test]
        public void Tick_HidesAllRenderersAfterDelay()
        {
            _healthBar.ShowWithRatio(0.7f);
            _healthBar.Tick(1.49f);

            Assert.IsTrue(_healthBar.IsVisible, "延时未结束前不应隐藏。");

            _healthBar.Tick(0.02f);

            Assert.IsFalse(_healthBar.IsVisible, "延时结束后应隐藏。");
            Assert.IsFalse(_background.enabled, "超时后背景应隐藏。");
            Assert.IsFalse(_fill.enabled, "超时后当前红色血量条应隐藏。");
            Assert.IsFalse(_standbyFill.enabled, "超时后预留血量条应隐藏。");
        }

        [Test]
        public void ShowWithRatio_ZeroHealth_HidesImmediately()
        {
            _healthBar.ShowWithRatio(0.5f);

            _healthBar.ShowWithRatio(0f);

            Assert.IsFalse(_healthBar.IsVisible, "致死血量不应显示空血条。");
            Assert.IsFalse(_background.enabled, "致死后背景应立即隐藏。");
            Assert.IsFalse(_fill.enabled, "致死后当前红色血量条应立即隐藏。");
            Assert.IsFalse(_standbyFill.enabled, "致死后预留血量条应立即隐藏。");
            Assert.AreEqual(1f, _healthBar.Ratio, Epsilon, "致死后应复位，供对象池复用。");
        }

        [Test]
        public void ResetAndHide_HidesAllRenderersAndRestoresFullHealth()
        {
            _healthBar.ShowWithRatio(0.25f);

            _healthBar.ResetAndHide();

            Assert.IsFalse(_healthBar.IsVisible, "复位后不应保持可见。");
            Assert.AreEqual(1f, _healthBar.Ratio, Epsilon, "复位后比例应为满血。");
            Assert.AreEqual(_fillBaseWidth, _fill.size.x, Epsilon, "复位后宽度应恢复满血。");
            AssertVector3(_fillBasePosition, _fill.transform.localPosition, "复位后位置应恢复满血位置。");
            Assert.IsFalse(_background.enabled, "复位后背景应隐藏。");
            Assert.IsFalse(_fill.enabled, "复位后当前红色血量条应隐藏。");
            Assert.IsFalse(_standbyFill.enabled, "复位后预留血量条应隐藏。");
        }

        private SpriteRenderer CreateRenderer(string name, Vector3 localPosition, float width)
        {
            GameObject node = new GameObject(name);
            node.transform.SetParent(_root.transform, false);
            node.transform.localPosition = localPosition;

            SpriteRenderer renderer = node.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = new Vector2(width, 0.0625f);
            return renderer;
        }

        private static void AssertVector3(Vector3 expected, Vector3 actual, string message)
        {
            Assert.AreEqual(expected.x, actual.x, Epsilon, $"{message} x 不正确。");
            Assert.AreEqual(expected.y, actual.y, Epsilon, $"{message} y 不正确。");
            Assert.AreEqual(expected.z, actual.z, Epsilon, $"{message} z 不正确。");
        }
    }
}
