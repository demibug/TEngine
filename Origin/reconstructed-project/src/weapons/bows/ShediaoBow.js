const BowWeaponBase=require('./BowWeaponBase');
class ShediaoBow extends BowWeaponBase {static config={name:'射雕弓',type:0,index:4,projectile:'vp'};performAttack(t){return Math.random()<.1?this.createProjectile('EagleArrow',t,{damageMultiplier:3,speedMultiplier:.35}):this.createProjectile('SimpleDynamicArrow',t,{config:'Shediao'});}}
module.exports={ShediaoBow};
