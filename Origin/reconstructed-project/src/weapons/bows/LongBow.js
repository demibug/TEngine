'use strict';
const BowWeaponBase=require('./BowWeaponBase');
class LongBow extends BowWeaponBase {
  static config={name:'长弓',type:0,index:1,projectile:'rd',status:'COMPLETE_FOR_LOGIC_NO_ASSETS'};
  constructor(){super();this.rangeBuffId=-1;}
  onAttach(){ if(this.owner&&this.owner.id!=null)this.rangeBuffId=this.buffPort.applyBuff(this.owner.id,2,.5,false); }
  onDetach(){ if(this.rangeBuffId>=0&&this.owner)this.buffPort.removeBuff(this.owner.id,2,this.rangeBuffId); this.rangeBuffId=-1; }
  performAttack(t){return this.createProjectile('SimpleDynamicArrow',t,{config:'LongBow',visual:{label:'长弓普通弓箭',image:'resources/img/weapon/arrow_1.png'},speedScale:3});}
}
module.exports={LongBow};
