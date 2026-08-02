'use strict';

/**
 * NET-01 晚绑定依赖的可观测替身。
 * 对应原闭包依赖 uq、rj、np、oc/sS，不包含其真实实现。
 */
function createNetworkMock(Laya, options = {}) {
  const state = {
    parsedCloudSave: options.parsedCloudSave ?? { wn: 2, ls: 1 },
    cloudSaveResolution: options.cloudSaveResolution ?? true,
    cloudPayload: options.cloudPayload ?? { wn: 2, ls: 1, g: 100 },
    calendarDayDifference: options.calendarDayDifference ?? 1,
    parseCalls: [],
    resolveCloudCalls: [],
    cloudPushCalls: 0,
    cloudAppliedCalls: 0,
    emittedUserIds: [],
    calendarCalls: [],
  };

  const playerData = {
    province: options.province ?? '初始省份',
    curStar: options.curStar ?? 12,
    isGetLastRankReward: options.isGetLastRankReward ?? 1000,

    resolveCloudOnLoad(payload) {
      state.resolveCloudCalls.push(payload);
      return state.cloudSaveResolution;
    },

    cloudPush() {
      state.cloudPushCalls += 1;
      return state.cloudPayload;
    },
  };

  const dependencies = {
    getLaya: () => Laya,
    getPlayerData: () => playerData,
    parseCloudSaveRaw(raw) {
      state.parseCalls.push(raw);
      return state.parsedCloudSave;
    },
    onCloudSaveApplied() {
      state.cloudAppliedCalls += 1;
    },
    emitAuthenticatedUserId(userId) {
      state.emittedUserIds.push(userId);
    },
    calendarDayDifference(first, second) {
      state.calendarCalls.push([first, second]);
      return state.calendarDayDifference;
    },
  };

  return {
    dependencies,
    state,
    playerData,
  };
}

module.exports = {
  createNetworkMock,
};
