'use strict';

/**
 * 重建来源：bundle.strings-decoded.js:24863-24930
 * 原始符号：rb
 * 重建状态：COMPLETE_FOR_POINTER_THRESHOLD_CONTRACT
 */
class DragEventBase {
  constructor() {
    this.pointerDown = false;
    this.dragging = false;
    this.pointerOrigin = { x: 0, y: 0 };
  }

  configureDragRuntime({ laya, eventBus = null, eventNames = null } = {}) {
    this.laya = laya || this.laya;
    this.dragEventBus = eventBus;
    this.dragEventNames = eventNames;
    return this;
  }

  onMouseDown() {
    if (!this.laya || !this.laya.stage) throw new Error('DragEventBase requires Laya.stage');
    this.pointerDown = true;
    this.dragging = false;
    this.pointerOrigin.x = this.laya.stage.mouseX || 0;
    this.pointerOrigin.y = this.laya.stage.mouseY || 0;
  }

  onMouseMove() {
    if (!this.pointerDown || this.dragging) return;
    const dx = (this.laya.stage.mouseX || 0) - this.pointerOrigin.x;
    const dy = (this.laya.stage.mouseY || 0) - this.pointerOrigin.y;
    if (Math.sqrt(dx * dx + dy * dy) > DragEventBase.DRAG_THRESHOLD_PX) {
      this.dragging = true;
      this.onDragStarted();
    }
  }

  onMouseUp() {
    if (!this.pointerDown) return;
    this.pointerDown = false;
    if (!this.dragging && this.dragEventBus && this.dragEventNames) {
      this.dragEventBus.event(this.dragEventNames.unitClickedById, this.id);
      this.dragEventBus.event(this.dragEventNames.unitClicked, this);
    }
    this.dragging = false;
    this.onDragEnded();
    if (this.dragEventBus && this.dragEventNames) this.dragEventBus.event(this.dragEventNames.pointerReleased);
  }

  onDragStarted() {}
  onDragEnded() {}
}

DragEventBase.DRAG_THRESHOLD_PX = 5;
module.exports = { DragEventBase };
