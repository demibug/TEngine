'use strict';

/**
 * 重建模块：Laya 序列化类 UUID 注册表
 * 原始范围：bundle.strings-decoded.js:50570, 56571, 64336, 66667
 * 重建状态：COMPLETE_FOR_CRITICAL_PATH
 */
const CLASS_REGISTRY = Object.freeze({
  'nFCDlT3GRD-9N62vwVVE4Q': Object.freeze({
    className: 'LoadSceneController',
    scenePath: 'scene/LoadScene.ls',
    sourceRange: 'bundle.strings-decoded.js:50996-51270',
  }),
  'dKvUsPTsTBGGfiZxHMSqtg': Object.freeze({
    className: 'MainSceneController',
    scenePath: 'scene/MainScene.ls',
    sourceRange: 'bundle.strings-decoded.js:64782-65947',
  }),
  'dxhrI-d-T2icEkklUGt-kQ': Object.freeze({
    className: 'MatchSceneController',
    scenePath: 'scene/MatchScene.ls',
    sourceRange: 'bundle.strings-decoded.js:60834-61284',
  }),
  'a1VsRozfQfKce35jblVR3w': Object.freeze({
    className: 'BattleSceneController',
    scenePath: 'scene/BattleScene.ls',
    sourceRange: 'bundle.strings-decoded.js:57007-59129',
  }),
  '36WnNn_bSKilkYpbnYn_9A': Object.freeze({
    className: 'GameOverSceneController',
    scenePath: 'scene/GameOverScene.ls',
    sourceRange: 'bundle.strings-decoded.js:51559-52842,66320-66361',
  }),
});

function registerCriticalPathClasses(Laya, classes) {
  if (!Laya || typeof Laya.regClass !== 'function') {
    throw new TypeError('Laya.regClass is required for serialized class registration');
  }
  const registered = [];
  for (const [uuid, metadata] of Object.entries(CLASS_REGISTRY)) {
    const ClassType = classes[metadata.className];
    if (typeof ClassType !== 'function') {
      throw new TypeError(`Missing ${metadata.className} for UUID ${uuid}`);
    }
    // LayaAir 3.3.10 的真实 API：regClass(uuid) 返回类装饰器。
    Laya.regClass(uuid)(ClassType);
    registered.push({ uuid, ...metadata, ClassType });
  }
  return registered;
}


function validateOriginSceneContracts(sceneCatalog) {
  const errors=[];
  for(const [uuid,metadata] of Object.entries(CLASS_REGISTRY)){
    const sceneName=metadata.scenePath.split('/').pop().replace(/\.ls$/,'');
    const scene=sceneCatalog&&sceneCatalog[sceneName];
    if(!scene)errors.push(`Missing origin scene ${sceneName}`);
    else if(scene.runtime!==uuid)errors.push(`Scene ${sceneName} runtime ${scene.runtime} does not match ${uuid}`);
  }
  if(errors.length)throw new Error(errors.join('; '));
  return true;
}

module.exports = { CLASS_REGISTRY, registerCriticalPathClasses, validateOriginSceneContracts };
