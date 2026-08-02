'use strict';
const Types=require('./types');
const { BOSS_DEFINITIONS }=require('./BossDefinitions');
const CLASS_BY_KEY=Object.freeze({ZhangLiang:Types.ZhangLiangBoss,ZhangBao:Types.ZhangBaoBoss,ZhangJiao:Types.ZhangJiaoBoss,SunShangXiang:Types.SunShangXiangBoss,ZhenFu:Types.ZhenFuBoss,DiaoChan:Types.DiaoChanBoss,HuaXiong:Types.HuaXiongBoss,LvBu:Types.LvBuBoss,DongZhuo:Types.DongZhuoBoss,DianWei:Types.DianWeiBoss,XiaHouDun:Types.XiaHouDunBoss,CaoCao:Types.CaoCaoBoss});
class BossFactory{
 constructor({objectPool=null,dependencyResolver=null}={}){this.objectPool=objectPool;this.dependencyResolver=dependencyResolver;this.registry=new Map(Object.entries(CLASS_BY_KEY));}
 create(key){const C=this.registry.get(key);if(!C)throw new Error(`Unknown boss type: ${key}`);const boss=this.objectPool?this.objectPool.takeByClass(C,()=>new C()):new C();if(this.dependencyResolver)boss.configure(this.dependencyResolver(key,boss));return boss;}
 recover(boss){return this.objectPool?this.objectPool.recoverByClass(boss):false;}
 validate(){for(const d of BOSS_DEFINITIONS)if(!this.registry.has(d.key))throw new Error(`Missing boss class: ${d.key}`);return true;}
 keys(){return [...this.registry.keys()];}
}
module.exports={BossFactory,CLASS_BY_KEY};
