const WeaponBase=require("../WeaponBase");
const { WeaponAttackEffect } = require('../WeaponAttackEffect');
const { applySpecial } = require('../WeaponSpecialEffects');

const WEAPON_DEFINITIONS = Object.freeze({
  '2:hY': { name:'虎啸战刀', attackType:'melee', special:'tigerRoar', chance:0.1, attackSpeedBonus:0.3, durationMs:10000, desc:'每次攻击有10%概率获得虎啸,提升周围单位30%攻速,持续10秒' }, '2:hB': { name:'短刀', attackType:'melee' },
  '3:h1': { name:'短剑', attackType:'sword' }, '1:hx': { name:'梨花枪', attackType:'pike', special:'pearBlossom', petals:8, desc:'每击杀一个敌人，飞出8朵旋转的梨花随机打击8个敌人' },
  '2:h0': { name:'青龙偃月刀', attackType:'melee', special:'dragonBladeQi', desc:'武将每斩杀一个敌人,会释放数团刀气无差别伤害所有敌人' }, '1:hy': { name:'虎头湛金枪', attackType:'pike', special:'goldenSpearArray', chance:0.2, arrays:3, exclusiveArrays:5, exclusiveGeneral:'马超', multiplier:3, stunMs:500, desc:'首次攻击某个单位时，20%几率从地下戳出3个枪阵，造成3倍伤害，0.5秒晕眩' },
  '3:hN': { name:'铁剑', attackType:'sword', addAttackPower:3 }, '3:h8': { name:'龙渊剑', attackType:'sword', special:'gentlemanVillain' },
  '3:h2': { name:'长剑', attackType:'sword', attackRangeBonus:0.5 },
  '3:hJ': { name:'莫邪', attackType:'sword', special:'gentlemanVillain' }, '1:hv': { name:'钩镰枪', attackType:'pike', special:'hookFall', chance:0.2, durationMs:2000, desc:'首次攻击某个单位时，20%几率使之跌倒，持续2秒' },
  '2:hF': { name:'三尖刀', attackType:'melee', special:'tripleBlade', interval:10, multiplier:2, desc:'每攻击10次释放刀气，两倍伤害，群体' }, '2:hV': { name:'古锭刀', attackType:'melee', special:'ancientGold', gold:1, desc:'首次攻击某单位可获得1金币' },
  '3:ib': { name:'七星剑', attackType:'sword', special:'gentlemanVillain' }, '1:hz': { name:'丈八蛇矛', attackType:'pike', special:'snakeSpear', baseSnakes:1, perLevel:1, exclusiveGeneral:'张飞', exclusiveDurationMs:6000, desc:'初始释放一条灵蛇拦路攻击敌人，英雄每升一级，会释放一条新的灵蛇' },
  '2:hL': { name:'方天画戟', attackType:'melee', special:'skyHalberd', multiplier:5, instantKillThreshold:0.2, levelChances:[0.1,0.15,0.2,0.25,0.3], desc:'每次攻击有概率将敌人挑起造成5倍伤害，并瞬杀血量低于20%的敌人' }, '3:h3': { name:'巨阙剑', attackType:'sword', special:'gentlemanVillain' },
  '1:hs': { name:'大戟', attackType:'pike', attackRangeBonus:1 }, '2:hE': { name:'狼牙棒', attackType:'melee', special:'wolfHowl', chance:0.1, attackSpeedBonus:0.2, durationMs:10000, desc:'每次攻击有10%概率获得狼啸,提升周围单位20%攻速,持续10秒' },
  '2:hD': { name:'铁刀', attackType:'melee', special:'ironKnifeSpeed', perHitBonus:0.05, desc:'攻击同一个单位时，每攻击一次，攻速+5%' }, '3:ie': { name:'轩辕剑', attackType:'sword', special:'gentlemanVillain' },
  '3:ia': { name:'青钢剑', attackType:'sword', special:'gentlemanVillain' }, '3:h4': { name:'龙泉剑', attackType:'sword', special:'gentlemanVillain' },
  '2:hZ': { name:'七星刀', attackType:'melee', special:'meteorShower', desc:'每次攻击有10%几率触发五枚流星，造成2倍范围伤害。' },
  '3:h9': { name:'双股剑', attackType:'sword', special:'gentlemanVillain' }, '1:hq': { name:'铁枪', attackType:'pike', special:'ironSpearArray', chance:0.2, arrays:1, multiplier:3, stunMs:500, desc:'首次攻击某个单位时，20%几率从地下戳出1个枪阵，造成3倍伤害，0.5秒晕眩' },
  '2:-1': { name:'木刀', attackType:'melee' }, '1:hA': { name:'龙胆亮银枪', attackType:'pike', special:'dragonSpearFly', chance:0.1, exclusiveChance:0.05, exclusiveGeneral:'赵云', multiplier:5, desc:'每次攻击有10%概率召唤飞枪，对所有敌人无差别打击，5倍伤害' },
  '2:hC': { name:'长刀', attackType:'melee', attackRangeBonus:0.5 }, '1:hp': { name:'长枪', attackType:'pike', attackRangeBonus:0.5 },
  '1:hw': { name:'点钢枪', attackType:'pike', special:'steelTipSpeed', attackSpeedBonus:0.5, durationMs:2000, desc:'每击杀一个敌人，攻速+50%，持续2秒' }, '3:ic': { name:'倚天剑', attackType:'sword', special:'gentlemanVillain' },
  '2:hU': { name:'铁蒺藜骨朵', attackType:'melee', special:'stunChance', chance:0.1, desc:'有10%概率造成眩晕' }, '3:id': { name:'干将', attackType:'sword', special:'gentlemanVillain' },
  '1:10': { name:'短枪', attackType:'pike' },
});

class Weapon extends WeaponBase {
  static config={name:'短枪', desc:'短枪直接命中目标', rarity:0, type:1, index:'10'};
  static getDefinition(type,index){
    const key=`${type}:${index}`;
    return WEAPON_DEFINITIONS[key] || { name:'未知武器', attackType:Number(type)===1?'pike':Number(type)===3?'sword':'melee' };
  }
  getConfig(){ return { ...Weapon.config, ...this.definition }; }
  init(id,type){
    this.definition=Weapon.getDefinition(type,id);
    super.init(id,type);
    this.special=this.definition.special || null;
    return this;
  }
  attack(ctx={}){
    const context=this.normalizeAttackContext(ctx);
    const target=context.target || context.targets?.[0];
    this.attackCount++;
    if(!target) return { attacked:false, reason:'no-target', attackType:this.attackType };

    if(this.special==='meteorShower' && this.randomSource() < .1){
      const owner=this.owner || {};
      const center=context.center || owner.combatCenter || { x:Number(owner.x)||0, y:Number(owner.y)||0 };
      // 提案 ④b：流星雨经 StarBullet 专属弹种实体承载，经 projectileSpawner 连接
      const projectileSpawner = this._buildProjectileSpawner(owner);
      const effect=new WeaponAttackEffect({
        type:'meteor-shower',
        attacker:owner,
        target,
        targets:context.targets || [],
        enemyManager:owner.enemyManager,
        center,
        radius:Number(context.effectRadius || owner.attackRange || 96),
        damage:Number(context.damage ?? owner.attackDamage ?? owner.attackPower ?? 0),
        multiplier:2,
        random:this.randomSource,
        maxTargets:5,
        allowRepeat:true,
        projectileSpawner,
      });
      const resolved = context.deferApply ? effect.result() : effect.apply();
      return { attacked:true, triggered:true, attackType:'meteor-shower', effect, ...resolved };
    }

    // 24 把特殊非弓武器分派（meteorShower 已单独处理，其余经 applySpecial）
    if(this.special && this.special!=='meteorShower'){
      const result=applySpecial(this, context);
      if(result && result.attacked) return { attackType:this.attackType, ...result };
      // 触发未命中（chance-miss / not-first-hit）回退通用攻击
      if(result && result.triggered===false && result.reason) {
        // 继续走通用攻击
      }
    }

    const effect=this.createAttackEffect(context,{ type:this.attackType, target, damage:Number(context.damage ?? this.owner?.attackDamage ?? this.owner?.attackPower ?? 0) });
    const resolved = context.deferApply ? effect.result() : effect.apply();
    return { attacked:true, triggered:false, attackType:this.attackType, effect, ...resolved };
  }

  /**
   * 提案 ④b：构建投射物生成器，连接武器技能触发层到 Projectile 实体层。
   * 优先用 owner.projectileManager.create（弓类已用路径），次用 weapon.projectileFactory.produce。
   * 两者均无时返回 null（纯逻辑测试无投射物依赖，回退直接 hit）。
   */
  _buildProjectileSpawner(owner){
    const manager = owner && (owner.projectileManager || owner.battle?.projectileManager);
    if (manager && typeof manager.create === 'function') {
      return (opts) => {
        try {
          const projectile = manager.create({ type: opts.type, attacker: opts.attacker, target: opts.target, damage: opts.damage });
          if (projectile && typeof projectile.fire === 'function') projectile.fire();
          return projectile;
        }
        catch { return null; }
      };
    }
    if (this.projectileFactory && typeof this.projectileFactory.produce === 'function') {
      return (opts) => {
        try {
          const projectile = this.projectileFactory.produce({ type: opts.type, appearance: { label: 'weapon-skill' } });
          if (projectile && typeof projectile.fire === 'function') projectile.fire();
          return projectile;
        }
        catch { return null; }
      };
    }
    return null;
  }
}
module.exports=Weapon;
