const WeaponBase=require("../WeaponBase");
const { WeaponAttackEffect } = require('../WeaponAttackEffect');

const WEAPON_DEFINITIONS = Object.freeze({
  '2:hY': { name:'虎啸战刀', attackType:'melee' }, '2:hB': { name:'短刀', attackType:'melee' },
  '3:h1': { name:'短剑', attackType:'sword' }, '1:hx': { name:'梨花枪', attackType:'pike' },
  '2:h0': { name:'青龙偃月刀', attackType:'melee' }, '1:hy': { name:'虎头湛金枪', attackType:'pike' },
  '3:hN': { name:'铁剑', attackType:'sword' }, '3:h8': { name:'龙渊剑', attackType:'sword' },
  '3:hJ': { name:'莫邪', attackType:'sword' }, '2:hv': { name:'钩镰枪', attackType:'melee' },
  '2:hF': { name:'三尖刀', attackType:'melee' }, '2:hV': { name:'古锭刀', attackType:'melee' },
  '3:ib': { name:'七星剑', attackType:'sword' }, '1:hz': { name:'丈八蛇矛', attackType:'pike' },
  '2:hL': { name:'方天画戟', attackType:'melee' }, '3:h3': { name:'巨阙剑', attackType:'sword' },
  '1:hs': { name:'大戟', attackType:'pike' }, '2:hE': { name:'狼牙棒', attackType:'melee' },
  '2:hD': { name:'铁刀', attackType:'melee' }, '3:ie': { name:'轩辕剑', attackType:'sword' },
  '3:ia': { name:'青钢剑', attackType:'sword' }, '3:h4': { name:'龙泉剑', attackType:'sword' },
  '2:hZ': { name:'七星刀', attackType:'melee', special:'meteorShower', desc:'每次攻击有10%几率触发五枚流星，造成2倍范围伤害。' },
  '3:h9': { name:'双股剑', attackType:'sword' }, '1:hq': { name:'铁枪', attackType:'pike' },
  '2:-1': { name:'木刀', attackType:'melee' }, '1:hA': { name:'龙胆亮银枪', attackType:'pike' },
  '2:hC': { name:'长刀', attackType:'melee' }, '1:hp': { name:'长枪', attackType:'pike' },
  '1:hw': { name:'点钢枪', attackType:'pike' }, '3:ic': { name:'倚天剑', attackType:'sword' },
  '2:hU': { name:'铁蒺藜骨朵', attackType:'melee' }, '3:id': { name:'干将', attackType:'sword' },
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
      });
      return { attacked:true, triggered:true, attackType:'meteor-shower', effect, ...effect.apply() };
    }

    const effect=this.createAttackEffect(context,{ type:this.attackType, target, damage:Number(context.damage ?? this.owner?.attackDamage ?? this.owner?.attackPower ?? 0) });
    return { attacked:true, triggered:false, attackType:this.attackType, effect, ...effect.apply() };
  }
}
module.exports=Weapon;
