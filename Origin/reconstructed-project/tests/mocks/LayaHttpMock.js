'use strict';

/**
 * Laya.HttpRequest / Laya.timer / Laya.LocalStorage 的受控测试替身。
 * 不创建 XMLHttpRequest，不访问网络、文件系统或平台 API。
 */
function createLayaHttpMock() {
  const requests = [];
  const operations = [];
  const timerTasks = [];
  const storage = new Map();
  let nextSendError = null;

  const Event = Object.freeze({
    COMPLETE: 'complete',
    ERROR: 'error',
  });

  class HttpRequestMock {
    constructor() {
      this.data = null;
      this.listeners = new Map();
      this.sent = null;
      this.timeoutValue = 0;
      this.http = {};

      Object.defineProperty(this.http, 'timeout', {
        configurable: true,
        enumerable: true,
        get: () => this.timeoutValue,
        set: (value) => {
          this.timeoutValue = value;
          operations.push({ type: 'set-timeout', request: this, value });
        },
      });

      requests.push(this);
      operations.push({ type: 'construct-request', request: this });
    }

    send(url, data, method, responseType, headers) {
      operations.push({
        type: 'send',
        request: this,
        url,
        data,
        method,
        responseType,
        headers,
      });

      if (nextSendError) {
        const error = nextSendError;
        nextSendError = null;
        throw error;
      }

      this.sent = {
        url,
        data,
        method,
        responseType,
        headers,
      };
    }

    once(type, caller, listener) {
      operations.push({
        type: `once-${type}`,
        request: this,
        caller,
        listener,
      });
      this.listeners.set(type, { caller, listener });
      return this;
    }

    emit(type, payload) {
      operations.push({ type: `emit-${type}`, request: this, payload });
      const registered = this.listeners.get(type);
      if (!registered) {
        return false;
      }
      this.listeners.delete(type);
      try {
        registered.listener.call(registered.caller, payload);
      } catch (error) {
        // Laya EventDispatcher 的监听调用会捕获异常并输出。
        console.error(error);
      }
      return true;
    }

    complete(data) {
      this.data = data;
      return this.emit(Event.COMPLETE, data);
    }

    fail(error) {
      return this.emit(Event.ERROR, error);
    }
  }

  const timer = {
    once(delay, caller, method) {
      const task = {
        delay,
        caller,
        method,
        cancelled: false,
        executed: false,
      };
      timerTasks.push(task);
      operations.push({ type: 'timer-once', ...task });
      return task;
    },

    clear(caller, method) {
      operations.push({ type: 'timer-clear', caller, method });
      for (const task of timerTasks) {
        if (!task.cancelled && task.caller === caller && task.method === method) {
          task.cancelled = true;
        }
      }
    },

    runNext() {
      const task = timerTasks.find((candidate) => !candidate.cancelled && !candidate.executed);
      if (!task) {
        return false;
      }
      task.executed = true;
      operations.push({ type: 'timer-run', task });
      task.method.call(task.caller);
      return true;
    },

    runAll() {
      while (this.runNext()) {
        // Run until no active task remains.
      }
    },

    pendingCount() {
      return timerTasks.filter((task) => !task.cancelled && !task.executed).length;
    },
  };

  const LocalStorage = {
    getItem(key) {
      operations.push({ type: 'storage-get', key });
      return storage.has(key) ? storage.get(key) : null;
    },

    setItem(key, value) {
      operations.push({ type: 'storage-set', key, value });
      storage.set(key, value);
    },

    removeItem(key) {
      operations.push({ type: 'storage-remove', key });
      storage.delete(key);
    },

    clear() {
      operations.push({ type: 'storage-clear' });
      storage.clear();
    },
  };

  const Laya = {
    HttpRequest: HttpRequestMock,
    Event,
    timer,
    LocalStorage,
  };

  return {
    Laya,
    requests,
    operations,
    timerTasks,
    storage,
    get lastRequest() {
      return requests[requests.length - 1] || null;
    },
    throwOnNextSend(error) {
      nextSendError = error;
    },
    resetOperations() {
      operations.length = 0;
    },
  };
}

module.exports = {
  createLayaHttpMock,
};
