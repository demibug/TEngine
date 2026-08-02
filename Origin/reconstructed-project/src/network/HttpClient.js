'use strict';

const { SingletonBase } = require('../core/SingletonBase');

/**
 * 重建模块：NET-01
 * 原始范围：
 * - bundle.strings-decoded.js:3316（静态字段）
 * - bundle.strings-decoded.js:3763-3768（单例基类）
 * - bundle.strings-decoded.js:5087-5392（网络类）
 * - bundle.strings-decoded.js:5395（运行时别名绑定）
 * 原始主要符号：tn；运行时别名 qK
 * 重建状态：COMPLETE（外部依赖绑定待后续模块替换）
 *
 * 说明：
 * 本文件根据混淆后 bundle.js 的真实运行逻辑重建。
 * 网络请求、回调、超时、云存档节流和登录响应处理已完整迁移。
 * 尚未重建的玩家数据、事件中心、云存档编解码和日期工具通过延迟绑定注入，
 * 所有推断命名均记录于 analysis/mappings/NET-01-symbol-map.json。
 */

const PRODUCTION_BASE_URL = 'https://api01.mihuangame.com/api/v2/';
const DEBUG_BASE_URL = 'https://debug.mihuangame.com/api/v2/';
const JSON_HEADERS = Object.freeze([
  'Content-Type',
  'application/json',
]);

function missingDependency(name) {
  return function unresolvedDependency() {
    throw new Error(
      `[NET-01] Missing runtime binding: ${name}. ` +
      'Bind the reconstructed dependency before invoking this code path.',
    );
  };
}

const DEFAULT_DEPENDENCIES = Object.freeze({
  getLaya() {
    if (typeof globalThis === 'undefined' || !globalThis.Laya) {
      throw new Error('[NET-01] globalThis.Laya is not available.');
    }
    return globalThis.Laya;
  },
  getPlayerData: missingDependency('getPlayerData'),
  parseCloudSaveRaw: missingDependency('parseCloudSaveRaw'),
  onCloudSaveApplied: missingDependency('onCloudSaveApplied'),
  emitAuthenticatedUserId: missingDependency('emitAuthenticatedUserId'),
  calendarDayDifference: missingDependency('calendarDayDifference'),
});

let runtimeDependencies = { ...DEFAULT_DEPENDENCIES };

/**
 * 临时兼容层：绑定尚未重建的晚绑定依赖。
 *
 * 绑定发生在启动器初始化阶段；具体依赖仍在每次业务方法调用时解析，
 * 因而不会把原 IIFE 的晚绑定行为提前到 HttpClient 构造阶段。
 */
function configureHttpClientDependencies(overrides) {
  if (!overrides || typeof overrides !== 'object') {
    throw new TypeError('HttpClient dependency overrides must be an object.');
  }

  for (const [name, value] of Object.entries(overrides)) {
    if (!(name in DEFAULT_DEPENDENCIES)) {
      throw new Error(`Unknown HttpClient dependency: ${name}`);
    }
    if (typeof value !== 'function') {
      throw new TypeError(`HttpClient dependency ${name} must be a function.`);
    }
  }

  runtimeDependencies = {
    ...runtimeDependencies,
    ...overrides,
  };
}

/**
 * 测试辅助方法；不参与生产初始化顺序。
 */
function resetHttpClientDependenciesForTests() {
  runtimeDependencies = { ...DEFAULT_DEPENDENCIES };
}

function getLaya() {
  return runtimeDependencies.getLaya();
}

function getPlayerData() {
  return runtimeDependencies.getPlayerData();
}

class HttpClient extends SingletonBase {
  constructor() {
    super(...arguments);

    // 推断命名：登录响应 data.userData 的原始缓存；随后由云存档同步流程读取。
    this.loginCloudSaveRaw = null;
    this.productionBaseUrl = PRODUCTION_BASE_URL;
    // 推断命名：为 true 时 url getter 切换到 debug.mihuangame.com。
    this.useDebugServer = false;
    this.authentication = '';
    this.userId = 0;
    this.userType = 0;
    // 推断命名：只用于 country/province 排行接口的 type 查询参数。
    this.rankingType = 3;
    this.channelAppId = 0;
  }

  /**
   * 原始方法符号：init
   * 原始源码范围：bundle.strings-decoded.js:5100-5102
   * 行为可信度：HIGH
   * 副作用：覆盖 channelAppId；当前网络类内没有进一步读取该字段。
   */
  initializeChannel(channelAppId) {
    this.channelAppId = channelAppId;
  }

  /**
   * 原始方法符号：request
   * 原始源码范围：bundle.strings-decoded.js:5103-5121
   * 行为可信度：HIGH
   * 副作用：创建并发送 Laya.HttpRequest；注册一次性 COMPLETE/ERROR 监听。
   *
   * 重要兼容行为：原代码先设置 timeout 并调用 send，再注册事件监听。
   * 该顺序被原样保留。
   */
  request(endpoint, data, callbacks, method = 'get', timeoutMs = HttpClient.DEFAULT_TIMEOUT_MS) {
    const Laya = getLaya();
    const request = new Laya.HttpRequest();
    const headers = [
      ...JSON_HEADERS,
      'authentication',
      this.authentication,
    ];

    request.http.timeout = timeoutMs;
    request.send(this.baseUrl + endpoint, data, method, 'json', headers);
    request.once(Laya.Event.COMPLETE, this, () => {
      const response = request.data;
      if (callbacks.success) {
        callbacks.success(response);
      }
    });
    request.once(Laya.Event.ERROR, this, (error) => {
      if (callbacks.fail) {
        callbacks.fail(error);
      }
    });
  }

  /**
   * 原始方法符号：Da
   * 原始源码范围：bundle.strings-decoded.js:5122-5139
   * 行为可信度：HIGH
   * 副作用：委托 request；将回调结果桥接为 Promise。
   */
  requestAsPromise(endpoint, data, method = 'get', timeoutMs = HttpClient.DEFAULT_TIMEOUT_MS) {
    return new Promise((resolve, reject) => {
      this.request(endpoint, data, {
        success: (response) => resolve(response),
        fail: (error) => reject(error),
      }, method, timeoutMs);
    });
  }

  /**
   * 原始方法符号：Ia
   * 原始源码范围：bundle.strings-decoded.js:5140-5152
   * 行为可信度：HIGH
   * 副作用：在 Laya.timer 注册一次性定时器；先完成时清理该定时器。
   *
   * 返回值只表示 Promise 是否在超时前成功 fulfilled；reject 和 timeout 都返回 false。
   * 本方法不会取消底层 Promise 或网络请求。
   */
  static waitForPromiseWithinTimeout(promise, timeoutMs = HttpClient.DEFAULT_TIMEOUT_MS) {
    const Laya = getLaya();

    return new Promise((resolve) => {
      let finished = false;
      let timeoutHandler;

      const finish = (result) => {
        if (!finished) {
          finished = true;
          Laya.timer.clear(HttpClient, timeoutHandler);
          resolve(result);
        }
      };

      timeoutHandler = () => finish(false);
      Laya.timer.once(timeoutMs, HttpClient, timeoutHandler);
      promise.then(
        () => finish(true),
        () => finish(false),
      );
    });
  }

  /**
   * 原始方法符号：Ca
   * 原始源码范围：bundle.strings-decoded.js:5153-5164
   * 行为可信度：HIGH
   * 副作用：POST 登录、应用响应、调用可选回调。
   *
   * 兼容细节：loginCode 只用于空值保护，实际请求体完全使用 requestPayload。
   * 网络失败被转换为 null，不继续向调用方 reject。
   */
  login(loginCode, requestPayload, callbacks, timeoutMs = HttpClient.DEFAULT_TIMEOUT_MS) {
    if (loginCode) {
      return this.requestAsPromise('sys/user/login', requestPayload, 'post', timeoutMs).then(
        (response) => {
          this.applyLoginResponse(response);
          if (callbacks?.success) {
            callbacks.success(response);
          }
          return response;
        },
        (error) => {
          console.warn('[Server] login failed or timeout', error);
          if (callbacks?.fail) {
            callbacks.fail(error);
          }
          return null;
        },
      );
    }

    if (callbacks?.fail) {
      callbacks.fail('login code is empty');
    }
    return Promise.resolve(null);
  }

  /**
   * 原始方法符号：Ta
   * 原始源码范围：bundle.strings-decoded.js:5165-5191
   * 行为可信度：HIGH
   * 副作用：更新鉴权、用户 ID、用户类型、云存档缓存、玩家省份并发布用户 ID 事件。
   */
  applyLoginResponse(response) {
    console.log('applyLoginResponse', response);

    const authentication = response && response.data && response.data.authentication;
    if (authentication) {
      this.authentication = authentication;
    }

    const userId = response && response.data && typeof response.data.userId === 'number'
      ? response.data.userId
      : 0;

    this.userId = userId;
    this.userType = response && response.data && typeof response.data.userType === 'number'
      ? response.data.userType
      : 0;
    this.loginCloudSaveRaw = response && response.data
      ? response.data.userData
      : null;

    let province = '';
    if (response && response.data && response.data.attach) {
      const responseProvince = response.data.attach.province;
      if (typeof responseProvince === 'string') {
        province = responseProvince;
      }
    }

    const playerData = getPlayerData();
    playerData.province = province.length > 0 ? province : '未知';

    if (userId > 0) {
      runtimeDependencies.emitAuthenticatedUserId(userId);
    }
  }

  /**
   * 原始方法符号：Ra
   * 原始源码范围：bundle.strings-decoded.js:5192-5202
   * 行为可信度：HIGH
   * 副作用：在登录后选择云端或本地存档；必要时强制回传本地存档。
   */
  synchronizeCloudSaveAfterLogin() {
    if (this.userId <= 0) {
      console.warn('[Server] 未登录，跳过云端存档同步');
      return;
    }

    const cloudSave = runtimeDependencies.parseCloudSaveRaw(this.loginCloudSaveRaw);
    if (!cloudSave) {
      console.warn('[Server] 登录未返回云端存档，使用本地数据');
      return;
    }

    const playerData = getPlayerData();
    if (playerData.resolveCloudOnLoad(cloudSave)) {
      runtimeDependencies.onCloudSaveApplied();
    } else {
      this.uploadCloudSave(true);
    }
  }

  /**
   * 原始方法符号：Oa
   * 原始源码范围：bundle.strings-decoded.js:5203-5206
   * 行为可信度：HIGH
   */
  reportGameStart(callbacks) {
    this.request('zyyad/game/start', null, callbacks, 'get');
  }

  /**
   * 原始方法符号：Ya
   * 原始源码范围：bundle.strings-decoded.js:5207-5223
   * 行为可信度：HIGH
   * 副作用：读取当前星数并上报对局结束。
   */
  reportGameEnd(didWin, callbacks) {
    const currentStar = getPlayerData().curStar;
    this.request(
      `zyyad/game/end?star=${currentStar}&win=${didWin ? 1 : 0}`,
      { skin: 1 },
      callbacks || {},
      'get',
    );
  }

  /**
   * 原始方法符号：Xa
   * 原始源码范围：bundle.strings-decoded.js:5224-5227
   * 行为可信度：HIGH
   */
  requestCountryRanking(callbacks) {
    this.request(`zyyad/game/country/list?type=${this.rankingType}`, null, callbacks, 'get');
  }

  /**
   * 原始方法符号：Ga
   * 原始源码范围：bundle.strings-decoded.js:5228-5231
   * 行为可信度：HIGH
   */
  requestProvinceRanking(callbacks) {
    this.request(`zyyad/game/province/detail/list?type=${this.rankingType}`, null, callbacks, 'get');
  }

  /**
   * 原始方法符号：Ha
   * 原始源码范围：bundle.strings-decoded.js:5232-5234
   * 行为可信度：HIGH
   * 说明：原方法仅转调国家排行接口，保留为独立入口以避免改变调用面。
   */
  requestCountryRankingAlias(callbacks) {
    this.requestCountryRanking(callbacks);
  }

  /**
   * 原始方法符号：getTime
   * 原始源码范围：bundle.strings-decoded.js:5235-5238
   * 行为可信度：HIGH
   */
  requestServerTime(callbacks) {
    this.request('sys/server/time', null, callbacks);
  }

  /**
   * 原始方法符号：Wa
   * 原始源码范围：bundle.strings-decoded.js:5239-5258
   * 行为可信度：HIGH
   * 副作用：先请求服务器时间；跨自然日时再请求 bestRank。
   *
   * 兼容细节：服务器时间请求失败时没有 fail 转发；未到期时也不调用传入回调。
   */
  requestBestRankIfDue(callbacks) {
    this.requestServerTime({
      success: (response) => {
        const serverTime = response && typeof response.data === 'number'
          ? response.data
          : 0;
        const playerData = getPlayerData();
        if (runtimeDependencies.calendarDayDifference(
          serverTime,
          playerData.isGetLastRankReward,
        ) >= 1) {
          this.request('bestRank', null, callbacks);
        }
      },
    });
  }

  /**
   * 原始方法符号：Fa
   * 原始源码范围：bundle.strings-decoded.js:5259-5290
   * 行为可信度：HIGH
   * 副作用：更新 playGameCount；首局和每 5 局上传一次云存档，force=true 时立即上传。
   */
  uploadCloudSave(force = false) {
    const Laya = getLaya();

    if (this.userId <= 0) {
      return;
    }

    if (!force) {
      let playGameCount = Number(
        Laya.LocalStorage.getItem(HttpClient.CLOUD_SAVE_UPLOAD_COUNT_KEY) || '0',
      );
      playGameCount += 1;
      Laya.LocalStorage.setItem(
        HttpClient.CLOUD_SAVE_UPLOAD_COUNT_KEY,
        String(playGameCount),
      );

      if (playGameCount !== 1 && playGameCount % 5 !== 0) {
        console.log(`[Server] 云端存档跳过，当前局数：${playGameCount}`);
        return;
      }
    }

    const payload = getPlayerData().cloudPush();
    this.request('sys/user/data', payload, {
      success: () => {
        console.log('[Server] 用户存档上传成功');
      },
      fail: (error) => {
        console.warn('[Server] 用户存档上传失败', error);
      },
    }, 'post');
  }

  /**
   * 原始方法符号：za
   * 原始源码范围：bundle.strings-decoded.js:5291-5312
   * 行为可信度：HIGH
   */
  uploadUserInfo(payload) {
    this.request('sys/user/info', payload, {
      success: (response) => {
        console.log('上传用户数据成功', response);
      },
      fail: (error) => {
        console.log('上传用户数据失败', error);
      },
    }, 'post');
  }

  /**
   * 原始方法符号：track
   * 原始源码范围：bundle.strings-decoded.js:5313-5332
   * 行为可信度：HIGH
   *
   * 兼容细节：空值或 length===0 时静默跳过；成功回调不接收服务端响应参数。
   */
  track(payload, callbacks) {
    if (payload && payload.length !== 0) {
      this.request('sys/oa/point/add/new', payload, {
        success: () => {
          if (callbacks?.success) {
            callbacks.success();
          }
        },
        fail: (error) => {
          if (callbacks?.fail) {
            callbacks.fail(error);
          }
        },
      }, 'post');
    }
  }

  /**
   * 原始方法符号：Na
   * 原始源码范围：bundle.strings-decoded.js:5333-5354
   * 行为可信度：HIGH
   */
  uploadErrorLog(payload) {
    this.request('sys/oa/errorUpload/add', payload, {
      success: () => {
        console.log('上传错误日志成功');
      },
      fail: (error) => {
        console.log('上传错误日志失败', error);
      },
    }, 'post');
  }

  /**
   * 原始 getter：url
   * 原始源码范围：bundle.strings-decoded.js:5366-5373
   * 行为可信度：HIGH
   */
  get baseUrl() {
    return this.useDebugServer ? DEBUG_BASE_URL : this.productionBaseUrl;
  }

  /**
   * 原始方法符号：Aa
   * 原始源码范围：bundle.strings-decoded.js:5374-5381
   * 行为可信度：HIGH
   */
  getUserId() {
    return this.userId;
  }

  /**
   * 原始方法符号：Ea
   * 原始源码范围：bundle.strings-decoded.js:5382-5389
   * 行为可信度：HIGH
   */
  getUserType() {
    return this.userType;
  }
}

// 原静态字段赋值位于 bundle.strings-decoded.js:3316，执行顺序由扁平化调度数组决定。
HttpClient.DEFAULT_TIMEOUT_MS = 5000;
HttpClient.CLOUD_SAVE_UPLOAD_COUNT_KEY = 'playGameCount';
// UNKNOWN：原代码赋值但 NET-01 内及全 bundle 中均未读取。
HttpClient.LEGACY_CODE_1201 = 1201;
HttpClient.LEGACY_CODE_1203 = 1203;

module.exports = {
  HttpClient,
  configureHttpClientDependencies,
  resetHttpClientDependenciesForTests,
};
