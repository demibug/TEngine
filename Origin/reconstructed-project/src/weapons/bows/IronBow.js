const BowWeaponBase=require('./BowWeaponBase');
class IronBow extends BowWeaponBase {
  static config={name:'铁胎弓',type:0,index:5,projectile:'vs',status:'DEFERRED_EFFECT_DEPENDENCY'};
  performAttack(t){
    // 提案 ④b（task 8.3）：火龙经专属弹种 FireDragonArrow 实体承载（对齐 bundle:42572 type:vs=FireDragonArrow）。
    // 先前 src 退化为 FireArrow + special:'FireDragonArrow' 标签，未真正实例化 FireDragonArrow 弹种——此处校正。
    if(this.randomSource()<.1)return this.createProjectile('FireDragonArrow',t,{durationMs:5000,impact:{burn:{durationMs:5000}}});
    return this.createProjectile('SimpleDynamicArrow',t,{config:'IronBow'});
  }
}
module.exports={IronBow};
