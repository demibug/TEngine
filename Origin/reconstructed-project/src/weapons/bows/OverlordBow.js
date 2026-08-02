const BowWeaponBase=require('./BowWeaponBase');
class OverlordBow extends BowWeaponBase {static config={name:'霸王弓',type:0,index:7,projectile:'s5'};performAttack(t){return this.createProjectile('EagleArrow',t,{source:'s5',impact:{ricochet:{chance:.5,maxTargets:1}}});}}
module.exports={OverlordBow};
