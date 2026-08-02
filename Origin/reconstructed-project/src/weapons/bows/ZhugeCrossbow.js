const BowWeaponBase=require('./BowWeaponBase');
class ZhugeCrossbow extends BowWeaponBase {
  static config={name:'诸葛连弩',type:0,index:9,projectile:'qr'};
  constructor(){super();this.normalShotCount=0;}
  performAttack(t){
    if(this.normalShotCount>=10){
      this.normalShotCount=0;
      const arrows=[];
      for(let index=0;index<10;index+=1)arrows.push(this.createProjectile('FireArrow',t,{special:'qr',burst:10,spread:true,shotIndex:index,shotCount:10}));
      return arrows;
    }
    this.normalShotCount+=1;
    return this.createProjectile('SimpleDynamicArrow',t,{speedScale:3,shotIndex:0,shotCount:1});
  }
  detach(){this.normalShotCount=0;return super.detach();}
}
module.exports={ZhugeCrossbow};
