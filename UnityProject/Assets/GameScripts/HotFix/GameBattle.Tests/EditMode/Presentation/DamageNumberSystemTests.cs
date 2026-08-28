using NUnit.Framework;
using UnityEngine;

namespace GameBattle.Tests.EditMode.Presentation
{
    [TestFixture]
    internal sealed class DamageNumberSystemTests
    {
        private GameObject _mapRoot;
        private GameObject _effectRoot;
        private Texture2D _texture;
        private Sprite[] _digits;
        private DamageNumberSystem _system;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _mapRoot = new GameObject("MapRoot");
            _effectRoot = new GameObject("EffectRoot");
            _effectRoot.transform.SetParent(_mapRoot.transform, false);
            _texture = new Texture2D(140, 23);
            _digits = new Sprite[10];
            for (int digit = 0; digit < _digits.Length; digit++)
            {
                _digits[digit] = Sprite.Create(
                    _texture,
                    new Rect(digit * 14f, 0f, 14f, 23f),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect);
            }

            _now = 0f;
            _system = new DamageNumberSystem(
                _effectRoot.transform,
                _mapRoot.transform,
                _digits,
                timeProvider: () => _now,
                randomSeed: 1);
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Clear();
            if (_digits != null)
            {
                for (int index = 0; index < _digits.Length; index++)
                {
                    if (_digits[index] != null)
                    {
                        Object.DestroyImmediate(_digits[index]);
                    }
                }
            }

            if (_texture != null)
            {
                Object.DestroyImmediate(_texture);
            }
            if (_mapRoot != null)
            {
                Object.DestroyImmediate(_mapRoot);
            }
        }

        [Test]
        public void Show_WithinFixedWindow_MergesWithoutRestartingAnimation()
        {
            DamageNumberView first = _system.Show(10, 6, Vector3.zero);
            first.Tick(0.2f);
            float elapsedBeforeMerge = first.ElapsedSeconds;

            _now = 0.2f;
            DamageNumberView merged = _system.Show(10, 7, Vector3.zero);

            Assert.AreSame(first, merged);
            Assert.AreEqual(13, merged.Value);
            Assert.AreEqual(elapsedBeforeMerge, merged.ElapsedSeconds, 0.0001f,
                "合并不得重启 500ms 动画。");
            Assert.AreEqual(1.05f, merged.transform.localScale.x, 0.0001f);
            Assert.AreEqual(1, _system.ActiveCount);
        }

        [Test]
        public void Show_AfterWindow_CreatesNewViewAndOldRecycleKeepsNewMergeTarget()
        {
            DamageNumberView first = _system.Show(10, 6, Vector3.zero);
            first.Tick(0.2f);

            _now = 0.31f;
            DamageNumberView second = _system.Show(10, 3, Vector3.zero);
            Assert.AreNotSame(first, second);
            Assert.AreEqual(2, _system.ActiveCount);

            first.Tick(0.3f);
            Assert.AreEqual(1, _system.ActiveCount);
            Assert.AreEqual(1, _system.MergeTargetCount,
                "旧 View 回收不得删除新 View 的合并映射。");

            _now = 0.4f;
            DamageNumberView merged = _system.Show(10, 5, Vector3.zero);
            Assert.AreSame(second, merged);
            Assert.AreEqual(8, merged.Value);
        }

        [Test]
        public void Tick_AtDuration_RecyclesView()
        {
            DamageNumberView view = _system.Show(1, 10, Vector3.zero);

            view.Tick(DamageNumberView.DurationSeconds);

            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(1, _system.PooledCount);
            Assert.AreEqual(0, _system.MergeTargetCount);
            Assert.IsFalse(view.gameObject.activeSelf);
        }

        [TestCase(9, 1f)]
        [TestCase(10, 1.05f)]
        [TestCase(149, 1.7f)]
        [TestCase(150, 1.75f)]
        [TestCase(10000, 1.75f)]
        public void ResolveScale_UsesOriginalBucketsAndCapsAtOnePointSevenFive(
            int damage,
            float expected)
        {
            Assert.AreEqual(expected, DamageNumberSystem.ResolveScale(damage), 0.0001f);
        }

        [Test]
        public void Disabled_DoesNotCreateView()
        {
            _system.Enabled = false;

            DamageNumberView view = _system.Show(1, 10, Vector3.zero);

            Assert.IsNull(view);
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(0, _system.PooledCount);
            Assert.AreEqual(0, _effectRoot.transform.childCount);
        }

        [Test]
        public void Clear_IsIdempotentAndClearsState()
        {
            DamageNumberView view = _system.Show(1, 10, Vector3.zero);

            _system.Clear();
            _system.Clear();
            Assert.IsTrue(_system.IsDisposed);
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(0, _system.PooledCount);
            Assert.AreEqual(0, _system.MergeTargetCount);
        }
    }
}
