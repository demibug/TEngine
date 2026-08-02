const BowWeaponBase=require('./BowWeaponBase');
class ZhugeCrossbow extends BowWeaponBase {static config={name:'诸葛连弩',type:0,index:9,projectile:'qr'};performAttack(t){this.attackCount++;if(this.attackCount%10===0)return this.createProjectile('FireArrow',t,{special:'qr',burst:10,spread:true});return this.createProjectile('SimpleDynamicArrow',t,{speed:3});}}
module.exports={ZhugeCrossbow};
