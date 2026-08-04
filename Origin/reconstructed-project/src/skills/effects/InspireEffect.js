'use strict';
const { BuffType } = require('../../buffs/BuffTypes');

/**
 * 鼓舞（Inspire，张角，bundle:31120-31186，符号 tj，效果 gw 在 bundle:31149-31183）。
 * 从 SkillEffectPort.js:23 inline lambda 迁出，逻辑逐行对齐，仅改为独立类文件包装。
 *
 * bundle 取证（work/bundle.strings-decoded.js）：
 * - bundle:31149-31183 `gw` 方法（符号 tj 类，张角技能）：
 *   - bundle:31175 `this["HE"] = vi["instance"]()["qx"](this["enemy"]["x"], this["enemy"]["y"], this["Lh"], this["nm"])`
 *     —— 取范围内友军敌（qx 为 EnemyManager 范围查询，参数 center x/y/radius/side；
 *     `this["nm"]` 即 boss.isPlayerLane）。inline lambda 以 ctx.alliedEnemies 接收已查询的目标列表。
 *   - bundle:31176 `for (let a=0; a<this["HE"]["length"]; a++) vd["instance"]()["applyBuff"](...)`
 *     对每个目标连施 3 个 buff（BuffType 数值与 src BuffTypes.js 枚举一致）：
 *       applyBuff(id, 6, .2, !0, f)  —— BuffType.SCALE(6) 值 .2，multiplicative=true
 *       applyBuff(id, 4, .5, !0, f)  —— BuffType.MAX_HP(4) 值 .5，multiplicative=true
 *       applyBuff(id, 3, .3, !0, f)  —— BuffType.MOVE_SPEED(3) 值 .3，multiplicative=true
 *     第 5 参 f = b[101]（durationMs）。inline lambda 以 ctx.durationMs 默认 5000 忠实还原。
 *   - bundle:31177 `pC["instance"]()["playSound"]("zhangJiao_skill_horn")` —— 播放张角技能号角音效。
 *
 * inline lambda（SkillEffectPort.js:23，迁出来源）：
 *   ({alliedEnemies=[],durationMs=5000})=>{
 *     const ids=[];
 *     for(const target of alliedEnemies){
 *       if(!this.buffManager)continue;
 *       ids.push(this.buffManager.applyBuff(target.id,BuffType.SCALE,.2,true,durationMs));
 *       ids.push(this.buffManager.applyBuff(target.id,BuffType.MAX_HP,.5,true,durationMs));
 *       ids.push(this.buffManager.applyBuff(target.id,BuffType.MOVE_SPEED,.3,true,durationMs));
 *     }
 *     return{status:'APPLIED',ids};
 *   }
 *
 * 音效（bundle:31177）：inline lambda 未还原音效播放，spec 验收要求 MUST 播放 zhangJiao_skill_horn，
 * 此处补齐——与 BattleShoutEffect / CavalryOrderEffect 一致，经 audioRegistry 注入播放，
 * audioRegistry 缺省时跳过不抛异常，不阻塞状态机。
 */
class InspireEffect {
  constructor({ buffManager, audioRegistry, logger = console } = {}) {
    Object.assign(this, { buffManager, audioRegistry, logger });
  }

  execute({ alliedEnemies = [], durationMs = 5000 } = {}) {
    // 逐行对齐 inline lambda：ids 累积每个目标的 3 个 buffId。
    const ids = [];
    for (const target of alliedEnemies) {
      // 逐行对齐 inline lambda：buffManager 缺失则跳过该目标，不抛异常。
      if (!this.buffManager) continue;
      // bundle:31176 施加 3 buff（忠实顺序 SCALE .2 / MAX_HP .5 / MOVE_SPEED .3，multiplicative=true）。
      ids.push(this.buffManager.applyBuff(target.id, BuffType.SCALE, .2, true, durationMs));
      ids.push(this.buffManager.applyBuff(target.id, BuffType.MAX_HP, .5, true, durationMs));
      ids.push(this.buffManager.applyBuff(target.id, BuffType.MOVE_SPEED, .3, true, durationMs));
    }
    // bundle:31177 播放张角技能号角音效（spec 验收 MUST 播放 zhangJiao_skill_horn）。
    // inline lambda 未还原此音效，此处补齐；audioRegistry 缺省时跳过，不阻塞状态机。
    if (this.audioRegistry && this.audioRegistry.play) {
      this.audioRegistry.play('zhangJiao_skill_horn');
    }
    // 逐行对齐 inline lambda：返回 APPLIED + ids。
    return { status: 'APPLIED', ids };
  }
}

module.exports = { InspireEffect };
