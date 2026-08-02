'use strict';

const { GameObjectEventProxy } = require('../core/GameObjectEventProxy');

/**
 * 重建来源：bundle.strings-decoded.js:24863-24930
 * 原始符号：rb
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 *
 * 正式触摸拖放 UI 暂缓；按原代码保留按下、阈值判定、开始拖动和释放钩子。
 */
class UnitDragBase extends GameObjectEventProxy {
  constructor() {
    super();
    this.pointerStart = { x: 0, y: 0 }; // s_
    this.pointerPressed = false;         // e_
    this.dragging = false;               // a_
    this.dragThreshold = 10;             // b.n_；具体静态值由输入管理器配置，开发环境显式注入。
  }

  configureDrag({ laya, dragThreshold } = {}) {
    if (laya) this.laya = laya;
    if (dragThreshold != null) this.dragThreshold = Number(dragThreshold);
    return this;
  }

  onMouseDown() {
    const stage = this._stage();
    this.pointerPressed = true;
    this.dragging = false;
    this.pointerStart.x = stage.mouseX || 0;
    this.pointerStart.y = stage.mouseY || 0;
  }

  onMouseMove() {
    if (!this.pointerPressed || this.dragging) return;
    const stage = this._stage();
    const dx = (stage.mouseX || 0) - this.pointerStart.x;
    const dy = (stage.mouseY || 0) - this.pointerStart.y;
    if (Math.sqrt(dx * dx + dy * dy) > this.dragThreshold) {
      this.dragging = true;
      this.onDragStarted();
    }
  }

  onMouseUp() {
    if (!this.pointerPressed) return;
    this.pointerPressed = false;
    const wasDragging = this.dragging;
    this.dragging = false;
    if (!wasDragging) this.onSelected();
    this.onDragReleased();
    this.onLayoutRefreshRequested();
  }

  // 原 rb.i_ / rb.h_ 是空钩子；子类按需要覆盖。
  onDragStarted() {}
  onDragReleased() {}

  // 原事件中心调用被隔离为可注入钩子，避免本轮恢复完整 UI 事件表。
  onSelected() {}
  onLayoutRefreshRequested() {}

  _stage() {
    const runtime = this.laya || globalThis.Laya;
    if (!runtime || !runtime.stage) throw new Error('UnitDragBase requires Laya.stage');
    return runtime.stage;
  }
}

module.exports = { UnitDragBase };
