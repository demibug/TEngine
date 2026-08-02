'use strict';

const { GameObjectEventProxy } = require('../core/GameObjectEventProxy');

/**
 * 兼容第四轮文件名。
 * 原符号 qE 已在第五轮确认是通用表现节点事件代理，而非敌人专属类。
 */
class EnemyEventProxy extends GameObjectEventProxy {}

module.exports = { EnemyEventProxy };
