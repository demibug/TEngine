const BowWeaponBase=require('./BowWeaponBase');
class IronBow extends BowWeaponBase {static config={name:'铁胎弓',type:0,index:5,projectile:'vs',status:'DEFERRED_EFFECT_DEPENDENCY'};performAttack(t){return Math.random()<.1?this.createProjectile('FireArrow',t,{special:'FireDragonArrow',duration:5,speed:5}):this.createProjectile('SimpleDynamicArrow',t,{config:'IronBow'});}}
module.exports={IronBow};
