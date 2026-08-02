'use strict';

// 兼容第三轮早期命名；唯一实现位于 GameLoop.js。
const { GameLoop } = require('./GameLoop');
const FixedUpdateManager = GameLoop;
module.exports = { GameLoop, FixedUpdateManager };
