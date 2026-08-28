using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 单局伤害数字系统：按敌人维护 300ms 合并目标，并池化世界空间数字对象。
    /// </summary>
    internal sealed class DamageNumberSystem
    {
        internal const float MergeWindowSeconds = 0.3f;
        private const int BaseSortingOrder = 100;

        private readonly Transform _effectRoot;
        private readonly Transform _mapRoot;
        private readonly Sprite[] _digitSprites;
        private readonly Func<float> _timeProvider;
        private readonly System.Random _random;
        private readonly Stack<DamageNumberView> _pool = new Stack<DamageNumberView>();
        private readonly HashSet<DamageNumberView> _active = new HashSet<DamageNumberView>();
        private readonly Dictionary<int, MergeEntry> _mergeEntries =
            new Dictionary<int, MergeEntry>();

        private bool _disposed;
        private int _sortingSequence;

        internal DamageNumberSystem(
            Transform effectRoot,
            Transform mapRoot,
            Sprite[] digitSprites,
            bool enabled = true,
            Func<float> timeProvider = null,
            int randomSeed = 19790609)
        {
            _effectRoot = effectRoot ?? throw new ArgumentNullException(nameof(effectRoot));
            _mapRoot = mapRoot ?? throw new ArgumentNullException(nameof(mapRoot));
            _digitSprites = digitSprites ?? throw new ArgumentNullException(nameof(digitSprites));
            if (_digitSprites.Length != 10)
            {
                throw new ArgumentException("伤害数字必须提供 0~9 共十个 Sprite", nameof(digitSprites));
            }

            Enabled = enabled;
            _timeProvider = timeProvider ?? (() => Time.unscaledTime);
            _random = new System.Random(randomSeed);
        }

        internal bool Enabled { get; set; }
        internal bool IsDisposed => _disposed;
        internal int ActiveCount => _active.Count;
        internal int PooledCount => _pool.Count;
        internal int MergeTargetCount => _mergeEntries.Count;

        internal DamageNumberView Show(int runtimeId, int rawDamage, Vector3 startPosition)
        {
            if (_disposed || !Enabled || runtimeId <= 0 || rawDamage <= 0)
            {
                return null;
            }

            float now = _timeProvider();
            if (_mergeEntries.TryGetValue(runtimeId, out MergeEntry entry)
                && entry.View != null
                && _active.Contains(entry.View)
                && now - entry.CreatedAt < MergeWindowSeconds)
            {
                int accumulated = SaturatingAdd(entry.AccumulatedDamage, rawDamage);
                entry.AccumulatedDamage = accumulated;
                _mergeEntries[runtimeId] = entry;
                entry.View.SetValue(accumulated);
                entry.View.SetScale(ResolveScale(accumulated));
                return entry.View;
            }

            DamageNumberView view = Acquire();
            float horizontalOffset = Range(-0.625f, 0.625f);
            float height = Range(1.25f, 1.875f);
            Vector3 horizontal = _mapRoot.right * horizontalOffset;
            Vector3 control = startPosition + horizontal + _mapRoot.up * height;
            Vector3 end = startPosition + horizontal;
            int sortingOrder = BaseSortingOrder + _sortingSequence++ % 100;

            view.Play(
                runtimeId,
                rawDamage,
                startPosition,
                control,
                end,
                ResolveScale(rawDamage),
                sortingOrder,
                Recycle);
            _active.Add(view);
            _mergeEntries[runtimeId] = new MergeEntry(view, rawDamage, now);
            return view;
        }

        internal void Clear()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (DamageNumberView view in _active)
            {
                DestroyView(view);
            }
            _active.Clear();

            while (_pool.Count > 0)
            {
                DestroyView(_pool.Pop());
            }

            _mergeEntries.Clear();
        }

        internal static float ResolveScale(int damage)
        {
            int bucket = Math.Min(Math.Max(0, damage) / 10, 15);
            return 1f + 0.05f * bucket;
        }

        private DamageNumberView Acquire()
        {
            DamageNumberView view = null;
            while (_pool.Count > 0 && view == null)
            {
                view = _pool.Pop();
            }

            if (view != null)
            {
                view.transform.SetParent(_effectRoot, false);
                return view;
            }

            var root = new GameObject("DamageNumber");
            root.transform.SetParent(_effectRoot, false);
            view = root.AddComponent<DamageNumberView>();
            view.Configure(_digitSprites);
            return view;
        }

        private void Recycle(DamageNumberView view)
        {
            if (view == null || _disposed)
            {
                return;
            }

            _active.Remove(view);
            int runtimeId = view.RuntimeId;
            if (_mergeEntries.TryGetValue(runtimeId, out MergeEntry entry)
                && ReferenceEquals(entry.View, view))
            {
                _mergeEntries.Remove(runtimeId);
            }

            view.PrepareForPool();
            _pool.Push(view);
        }

        private float Range(float minimum, float maximum)
        {
            return minimum + (float)_random.NextDouble() * (maximum - minimum);
        }

        private static int SaturatingAdd(int left, int right)
        {
            long sum = (long)left + right;
            return sum >= int.MaxValue ? int.MaxValue : (int)sum;
        }

        private static void DestroyView(DamageNumberView view)
        {
            if (view == null)
            {
                return;
            }

            view.StopWithoutCallback();
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(view.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        private struct MergeEntry
        {
            internal MergeEntry(DamageNumberView view, int accumulatedDamage, float createdAt)
            {
                View = view;
                AccumulatedDamage = accumulatedDamage;
                CreatedAt = createdAt;
            }

            internal DamageNumberView View;
            internal int AccumulatedDamage;
            internal float CreatedAt;
        }
    }
}
