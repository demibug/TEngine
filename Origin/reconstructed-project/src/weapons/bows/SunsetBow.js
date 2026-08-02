'use strict';
const BowWeaponBase=require('./BowWeaponBase');
class SunsetBow extends BowWeaponBase {
  static config={name:'落日弓',type:0,index:8,projectile:'tB',status:'PARTIAL_WITH_EXACT_GAPS'};
  constructor(){super();this.rangeBuffId=-1;}
  onAttach(){if(this.owner&&this.owner.id!=null)this.rangeBuffId=this.buffPort.applyBuff(this.owner.id,2,1,true);}
  onDetach(){if(this.rangeBuffId>=0&&this.owner)this.buffPort.removeBuff(this.owner.id,2,this.rangeBuffId);this.rangeBuffId=-1;}
  performAttack(t){return this.createProjectile('HuoFengHuang',t,{special:'HuoFengHuang',distanceScale:true});}
}
module.exports={SunsetBow};
