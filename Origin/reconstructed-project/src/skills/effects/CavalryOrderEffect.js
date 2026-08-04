'use strict';
/**
 * 铁骑号令（CavalryOrder，华雄，bundle:32753-32802，符号 tz，效果 gw 在 bundle:32784-32798）。
 * 从 SkillEffectPort.js:24 inline lambda 迁出，逻辑逐行对齐，仅改为独立类文件包装。
 *
 * bundle 取证（work/bundle.strings-decoded.js）：
 * - bundle:32784-32798 `gw` 方法（符号 tz 类）：
 *   - bundle:32789 `pC["instance"]()["playSound"]("summon_cavalry_skill")` —— 播放召唤骑兵音效。
 *   - bundle:32792 `vi["instance"]()["jL"](5, this["nm"])` —— 召唤 5 个骑兵单位；
 *     `vi.jL(a,b,c=false)` 等价 EnemyManager.spawnByKey(typeKey,isPlayerSide,isSpecial)，
 *     inline lambda 以字符串键 'Cavalry' 忠实还原数字 5 的 typeKey，`this["nm"]` 即 boss.isPlayerLane。
 *
 * inline lambda（SkillEffectPort.js:24，迁出来源）：
 *   ({boss,enemyManager})=>{
 *     const manager=enemyManager||this.enemyManager;
 *     if(!manager||!boss)return{status:'MISSING_CAVALRY_DEPENDENCY'};
 *     const enemies=[];
 *     for(let i=0;i<5;i++)enemies.push(manager.spawnByKey('Cavalry',boss.isPlayerLane,false));
 *     return{status:'APPLIED',enemyIds:enemies.map(e=>e.id)};
 *   }
 *
 * 音效（bundle:32789）：inline lambda 未还原音效播放，spec 验收要求 MUST 播放 summon_cavalry_skill，
 * 此处补齐——与 BattleShoutEffect 一致，经 audioRegistry 注入播放，audioRegistry 缺省时跳过不抛异常。
 */
class CavalryOrderEffect {
  constructor({ enemyManager, audioRegistry, logger = console } = {}) {
    Object.assign(this, { enemyManager, audioRegistry, logger });
  }

  execute({ boss, enemyManager } = {}) {
    // 逐行对齐 inline lambda：manager 取 ctx.enemyManager 兜底 this.enemyManager。
    const manager = enemyManager || this.enemyManager;
    // 逐行对齐 inline lambda：依赖缺失直接返回状态，不抛异常。
    if (!manager || !boss) return { status: 'MISSING_CAVALRY_DEPENDENCY' };
    // bundle:32789 播放召唤骑兵音效（spec 验收 MUST 播放 summon_cavalry_skill）。
    // inline lambda 未还原此音效，此处补齐；audioRegistry 缺省时跳过，不阻塞状态机。
    if (this.audioRegistry && this.audioRegistry.play) {
      this.audioRegistry.play('summon_cavalry_skill', { ownerId: boss.id });
    }
    // 逐行对齐 inline lambda：循环 5 次召唤骑兵（忠实 bundle:32792 vi.jL(5, nm)）。
    const enemies = [];
    for (let i = 0; i < 5; i++) {
      enemies.push(manager.spawnByKey('Cavalry', boss.isPlayerLane, false));
    }
    // 逐行对齐 inline lambda：返回 APPLIED + enemyIds。
    return { status: 'APPLIED', enemyIds: enemies.map(e => e.id) };
  }
}

module.exports = { CavalryOrderEffect };
