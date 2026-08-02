const BowWeaponBase=require('./BowWeaponBase');
class IronArrowBow extends BowWeaponBase {
  static config={name:'铁弓',type:0,index:2,projectile:'rd',status:'DEFERRED_EFFECT_DEPENDENCY'};
  performAttack(t){
    const impact=this.randomSource()<.1?{knockback:{magnitude:1,durationMs:500}}:null;
    return this.createProjectile('SimpleDynamicArrow',t,{effectChance:.1,effect:'vn(1)',impact});
  }
}
module.exports={IronArrowBow};
