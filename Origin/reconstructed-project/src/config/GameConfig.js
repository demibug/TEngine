'use strict';

/**
 * 重建来源：original/index.js:2
 * 状态：CONFIRMED
 *
 * LayaAir Player 配置的行为等价副本。game.json 与分包声明已由 origin_project 验证。
 */
const GameConfig = Object.freeze({
  resolution: Object.freeze({
    designWidth: 640,
    designHeight: 1386,
    scaleMode: 'fixedwidth',
    screenMode: 'vertical',
    alignV: 'top',
    alignH: 'left',
    backgroundColor: '#888888',
  }),
  '2D': Object.freeze({
    isAntialias: false,
    FPS: 60,
    useRetinalCanvas: false,
    isAlpha: false,
    enableUniformBufferObject: true,
    matUseUBO: true,
    webGL2D_MeshAllocMaxMem: true,
    defaultFont: 'Arial',
    defaultFontSize: 20,
  }),
  spineVersion: '3.7',
  wechatGame: Object.freeze({ deviceOrientation:'portrait', showStatusBar:false, networkTimeout:Object.freeze({request:10000,connectSocket:10000,uploadFile:10000,downloadFile:10000}), gameJson:'origin_project/game.json', entry:'origin_project/game.js' }),
  splash: Object.freeze({ fit: 'center', enabled: true, duration: 1 }),
  light2D: Object.freeze({
    ambientColor: Object.freeze({ r: 0.2, g: 0.2, b: 0.2, a: 0 }),
    ambientLayerMask: -1,
    multiSamples: 4,
  }),
  '3D': Object.freeze({
    enableDynamicBatch: true,
    defaultPhysicsMemory: 16,
    pixelRatio: 1,
    enableMultiLight: true,
    maxLightCount: 32,
    lightClusterCount: Object.freeze({ x: 12, y: 12, z: 12 }),
    maxMorphTargetCount: 32,
  }),
  physics2D: Object.freeze({
    layers: Object.freeze(['Default']),
    defaultConfig: Object.freeze({
      allowSleeping: false,
      gravity: Object.freeze({ x: 0, y: 9.8 }),
      velocityIterations: 8,
      positionIterations: 3,
      pixelRatio: 50,
      debugDraw: false,
      drawShape: true,
      drawJoint: true,
      drawAABB: false,
      drawCenterOfMass: false,
    }),
  }),
  physics3D: Object.freeze({
    fixedTimeStep: 1 / 60,
    maxSubSteps: 1,
    enableCCD: false,
    ccdThreshold: 0.0001,
    ccdSphereRadius: 0.0001,
    layers: Object.freeze(['Default']),
  }),
  UI: Object.freeze({
    alwaysIncludeDefaultSkin: true,
    horizontalScrollBar: 'res://d8f056de-a72c-49e3-b4e5-fe746c94aa04',
    verticalScrollBar: 'res://d072e8ba-6ae4-4f23-9aaf-d376f765a7ab',
    popupMenu: 'res://42f45010-7562-4115-872f-365829244e64',
    tooltipsWidget: 'res://940534fa-684b-4821-84db-23243c93a4de',
    defaultTooltipsShowDelay: 100,
    defaultComboBoxVisibleItemCount: 20,
  }),
  addons: Object.freeze({}),
  physics3dModule: 'laya.bullet',
  physics2dModule: 'laya.box2D',
  stat: false,
  vConsole: false,
  alertGlobalError: false,
  startupScene: 'scene/LoadScene.ls',
  useSafeFileExtensions: true,
  pkgs: Object.freeze([
    Object.freeze({ path: '', autoLoad: true }),
    Object.freeze({ path: 'resources/anim' }),
    Object.freeze({ path: 'resources/music' }),
    Object.freeze({ path: 'resources/sound' }),
    Object.freeze({ path: 'resources/img', autoLoad: true }),
  ]),
});

module.exports = { GameConfig };
