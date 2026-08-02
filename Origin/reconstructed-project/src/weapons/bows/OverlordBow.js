const BowWeaponBase=require('./BowWeaponBase');
class OverlordBow extends BowWeaponBase {static config={name:'霸王弓',type:0,index:7,projectile:'s5'};performAttack(t){return this.createProjectile('EagleArrow',t,{ricochetChance:.5,maxRicochet:1,source:'s5'});}}
module.exports={OverlordBow};
