using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameBattle
{
    /// <summary>
    /// 单个世界空间伤害数字：负责字形排版与 500ms 二次贝塞尔运动。
    /// </summary>
    internal sealed class DamageNumberView : MonoBehaviour
    {
        internal const float DurationSeconds = 0.5f;
        private const float GlyphSpacing = 0.01f;

        private readonly List<SpriteRenderer> _glyphRenderers = new List<SpriteRenderer>();
        private Sprite[] _digitSprites;
        private SortingGroup _sortingGroup;
        private Action<DamageNumberView> _completion;
        private Vector3 _start;
        private Vector3 _control;
        private Vector3 _end;
        private float _elapsed;
        private bool _running;

        internal int RuntimeId { get; private set; }
        internal int Value { get; private set; }
        internal float ElapsedSeconds => _elapsed;
        internal bool IsRunning => _running;
        internal int VisibleGlyphCount { get; private set; }

        internal void Configure(Sprite[] digitSprites)
        {
            if (digitSprites == null || digitSprites.Length != 10)
            {
                throw new ArgumentException("伤害数字必须提供 0~9 共十个 Sprite", nameof(digitSprites));
            }

            for (int index = 0; index < digitSprites.Length; index++)
            {
                if (digitSprites[index] == null)
                {
                    throw new ArgumentException($"伤害数字 Sprite[{index}] 为空", nameof(digitSprites));
                }
            }

            _digitSprites = digitSprites;
            _sortingGroup = GetComponent<SortingGroup>();
            if (_sortingGroup == null)
            {
                _sortingGroup = gameObject.AddComponent<SortingGroup>();
            }

            PrepareForPool();
        }

        internal void Play(
            int runtimeId,
            int value,
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float scale,
            int sortingOrder,
            Action<DamageNumberView> completion)
        {
            if (_digitSprites == null)
            {
                throw new InvalidOperationException("DamageNumberView 必须先 Configure");
            }

            RuntimeId = runtimeId;
            _start = start;
            _control = control;
            _end = end;
            _elapsed = 0f;
            _completion = completion;
            _running = true;
            _sortingGroup.sortingOrder = sortingOrder;
            transform.position = start;
            transform.localScale = Vector3.one * scale;
            gameObject.SetActive(true);
            SetValue(value);
        }

        /// <summary>合并伤害时只更新数字与缩放，不重置轨迹和生命周期。</summary>
        internal void SetValue(int value)
        {
            Value = Math.Max(0, value);
            string text = Value.ToString(CultureInfo.InvariantCulture);
            EnsureGlyphCount(text.Length);

            float totalWidth = 0f;
            for (int index = 0; index < text.Length; index++)
            {
                int digit = text[index] - '0';
                SpriteRenderer renderer = _glyphRenderers[index];
                renderer.sprite = _digitSprites[digit];
                renderer.enabled = true;
                totalWidth += renderer.sprite.bounds.size.x;
            }

            totalWidth += GlyphSpacing * Math.Max(0, text.Length - 1);
            float cursor = -totalWidth * 0.5f;
            for (int index = 0; index < text.Length; index++)
            {
                SpriteRenderer renderer = _glyphRenderers[index];
                float width = renderer.sprite.bounds.size.x;
                renderer.transform.localPosition = new Vector3(cursor + width * 0.5f, 0f, 0f);
                cursor += width + GlyphSpacing;
            }

            for (int index = text.Length; index < _glyphRenderers.Count; index++)
            {
                _glyphRenderers[index].enabled = false;
            }

            VisibleGlyphCount = text.Length;
        }

        internal void SetScale(float scale)
        {
            transform.localScale = Vector3.one * scale;
        }

        internal void Tick(float unscaledDeltaSeconds)
        {
            if (!_running || unscaledDeltaSeconds <= 0f)
            {
                return;
            }

            _elapsed += unscaledDeltaSeconds;
            float t = Mathf.Clamp01(_elapsed / DurationSeconds);
            float inverse = 1f - t;
            transform.position = inverse * inverse * _start
                                 + 2f * inverse * t * _control
                                 + t * t * _end;

            if (_elapsed >= DurationSeconds)
            {
                Complete();
            }
        }

        internal void PrepareForPool()
        {
            _running = false;
            _completion = null;
            RuntimeId = 0;
            Value = 0;
            VisibleGlyphCount = 0;
            _elapsed = 0f;
            transform.localScale = Vector3.one;
            for (int index = 0; index < _glyphRenderers.Count; index++)
            {
                _glyphRenderers[index].enabled = false;
            }

            gameObject.SetActive(false);
        }

        internal void StopWithoutCallback()
        {
            _running = false;
            _completion = null;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        private void Complete()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            Action<DamageNumberView> completion = _completion;
            _completion = null;
            completion?.Invoke(this);
        }

        private void EnsureGlyphCount(int count)
        {
            while (_glyphRenderers.Count < count)
            {
                int index = _glyphRenderers.Count;
                var glyph = new GameObject($"Glyph{index}");
                glyph.transform.SetParent(transform, false);
                SpriteRenderer renderer = glyph.AddComponent<SpriteRenderer>();
                renderer.enabled = false;
                _glyphRenderers.Add(renderer);
            }
        }
    }
}
