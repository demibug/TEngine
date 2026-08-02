'use strict';
const BowWeaponBase=require('./BowWeaponBase');
class HornBow extends BowWeaponBase {
  static config={name:'角弓',type:0,index:3,projectile:'rd',status:'COMPLETE_FOR_LOGIC_NO_ASSETS'};
  constructor(){super();this.attackSpeedBuffId=-1;this.lastTargetId=-1;this.stack=0;this.maxStack=20;}
  onAttach(){if(this.owner&&this.owner.id!=null)this.attackSpeedBuffId=this.buffPort.applyBuff(this.owner.id,1,0,true);}
  onDetach(){if(this.attackSpeedBuffId>=0&&this.owner)this.buffPort.removeBuff(this.owner.id,1,this.attackSpeedBuffId);this.attackSpeedBuffId=-1;this.lastTargetId=-1;this.stack=0;}
  performAttack(t){
    if(this.lastTargetId===t.id){this.stack=Math.min(this.stack+.05,this.maxStack);this.buffPort.modify(this.owner.id,1,this.attackSpeedBuffId,this.stack,true);}
    else{this.buffPort.modify(this.owner.id,1,this.attackSpeedBuffId,0,true);this.stack=0;this.lastTargetId=t.id;}
    const count=1+(Number(this.owner?.W_)||0),out=[];for(let i=0;i<count;i++)out.push(this.createProjectile('SimpleDynamicArrow',t,{config:'HornBow',visual:{label:'角弓普通弓箭',image:'resources/img/weapon/arrow_3.png'},speedScale:3,shotIndex:i,shotCount:count}));return out;
  }
}
module.exports={HornBow};
