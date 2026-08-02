const BowWeaponBase=require('./BowWeaponBase');
class IronBow extends BowWeaponBase {
  static config={name:'铁胎弓',type:0,index:5,projectile:'vs',status:'DEFERRED_EFFECT_DEPENDENCY'};
  performAttack(t){
    if(this.randomSource()<.1)return this.createProjectile('FireArrow',t,{special:'FireDragonArrow',durationMs:5000,impact:{burn:{durationMs:5000}}});
    return this.createProjectile('SimpleDynamicArrow',t,{config:'IronBow'});
  }
}
module.exports={IronBow};
