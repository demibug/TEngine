const { WeaponAttackEffect } = require('./WeaponAttackEffect');

class WeaponBase {
  constructor(){
    this.weaponId=-1; this.type=-1; this.txt=""; this.rarity=0; this.intro="";
    this.addAttackPower=0; this.attackPowerBonus=0; this.attackRangeBonus=0; this.attackSpeedBonus=0;
    this.owner=null; this.active=false; this.attackCount=0; this.projectileFactory=null; this.buffService=null; this.skillService=null;
    this.buffManager=null; this.randomSource=Math.random; this.attackType='direct';
  }
  init(id,type){
    this.weaponId=id; this.type=type;
    const cfg=typeof this.getConfig === 'function' ? this.getConfig() : (this.constructor.config || {});
    this.txt=cfg.name||this.txt; this.rarity=cfg.rarity||0; this.intro=cfg.desc||"";
    this.addAttackPower=Number(cfg.addAttackPower)||0;
    this.attackPowerBonus=Number(cfg.attackPowerBonus ?? this.addAttackPower)||0;
    this.attackRangeBonus=Number(cfg.attackRangeBonus)||0;
    this.attackSpeedBonus=Number(cfg.attackSpeedBonus)||0;
    this.attackType=cfg.attackType || this.attackType;
    return this;
  }
  getConfig(){ return this.constructor.config || {}; }
  attach(owner,buffManager=null){this.owner=owner; this.buffManager=buffManager || owner?.buffManager || null; this.active=true; return this;}
  detach(){this.owner=null; this.buffManager=null; this.active=false; return this;}
  update(dt){}
  startGame(){}
  normalizeAttackContext(input={}){
    if(input && input.target) return input;
    return { target: input && input.id != null ? input : null, targets: input && input.id != null ? [input] : [] };
  }
  createAttackEffect(ctx, options={}){
    return new WeaponAttackEffect({
      attacker:this.owner,
      enemyManager:this.owner?.enemyManager || null,
      random:this.randomSource,
      ...ctx,
      ...options,
    });
  }
  attack(ctx={}){
    const context=this.normalizeAttackContext(ctx);
    const target=context.target || context.targets?.[0];
    this.attackCount++;
    if(!target) return { attacked:false, reason:'no-target', attackType:this.attackType };
    const effect=this.createAttackEffect(context,{type:this.attackType,damage:context.damage ?? this.owner?.attackDamage ?? this.owner?.attackPower ?? 0,target});
    const resolved = context.deferApply ? effect.result() : effect.apply();
    return { attacked:true, attackType:this.attackType, effect, ...resolved };
  }
  getCombatModifiers(){ return { attackPower:this.attackPowerBonus, range:this.attackRangeBonus, attackSpeed:this.attackSpeedBonus }; }
  selectTarget(){ return null; }
  gameOver(){this.active=false; this.owner=null;}
  reset(){this.weaponId=-1;this.type=-1;this.owner=null;this.buffManager=null;this.active=false;this.attackCount=0;this.attackPowerBonus=0;this.attackRangeBonus=0;this.attackSpeedBonus=0;this.attackType='direct';}
}
module.exports=WeaponBase;
