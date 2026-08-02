'use strict';

class LayaTimerMock {
  constructor() {
    this.currTimer = 0;
    this.delta = 0;
    this._paused = false;
    this._tasks = [];
    this.calls = [];
  }

  frameLoop(frameInterval, caller, method, args = null) {
    this.clear(caller, method);
    this.calls.push(['frameLoop', frameInterval, caller, method]);
    this._tasks.push({ type: 'frame', frameInterval, caller, method, args, frameCount: 0 });
  }
  loop(delay, caller, method, args = null) {
    if (delay <= 0) throw new Error('Timer loop delay must be positive');
    this.clear(caller, method);
    this.calls.push(['loop', delay, caller, method]);
    this._tasks.push({ type: 'loop', delay, due: this.currTimer + delay, caller, method, args });
  }
  once(delay, caller, method, args = null) {
    this.calls.push(['once', delay, caller, method]);
    this._tasks.push({ type: 'once', delay, due: this.currTimer + Math.max(0, delay), caller, method, args });
  }
  callLater(caller, method, args = null) { this.once(0, caller, method, args); }
  runCallLater(caller, method) {
    const task = this._tasks.find(t => t.type === 'once' && t.caller === caller && t.method === method);
    if (!task) return;
    this._remove(task);
    this._invoke(task);
  }
  clear(caller, method) {
    this.calls.push(['clear', caller, method]);
    this._tasks = this._tasks.filter(t => !(t.caller === caller && t.method === method));
  }
  clearAll(caller) {
    this.calls.push(['clearAll', caller]);
    this._tasks = this._tasks.filter(t => t.caller !== caller);
  }
  pause() { this.calls.push(['pause']); this._paused = true; }
  resume() { this.calls.push(['resume']); this._paused = false; }

  tick(deltaMs) {
    if (deltaMs < 0) throw new Error('Timer delta cannot be negative');
    this.delta = deltaMs;
    this.currTimer += deltaMs;
    if (this._paused) return;
    let safety = 0;
    while (safety++ < 10000) {
      const due = this._tasks.find(t => t.type !== 'frame' && t.due <= this.currTimer);
      if (!due) break;
      if (due.type === 'once') this._remove(due);
      else due.due += due.delay;
      this._invoke(due);
    }
    if (safety >= 10000) throw new Error('Timer task safety limit exceeded');
    for (const task of this._tasks.filter(t => t.type === 'frame').slice()) {
      if (!this._tasks.includes(task)) continue;
      task.frameCount += 1;
      if (task.frameCount % task.frameInterval === 0) this._invoke(task);
    }
  }

  taskCountFor(caller, method = null) {
    return this._tasks.filter(t => t.caller === caller && (!method || t.method === method)).length;
  }
  has(caller, method) { return this.taskCountFor(caller, method) > 0; }
  get paused() { return this._paused; }
  _invoke(task) {
    const args = task.args == null ? [] : Array.isArray(task.args) ? task.args : [task.args];
    task.method.apply(task.caller, args);
  }
  _remove(task) { const index = this._tasks.indexOf(task); if (index >= 0) this._tasks.splice(index, 1); }
}

module.exports = { LayaTimerMock };
