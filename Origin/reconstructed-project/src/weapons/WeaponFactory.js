const WeaponBase=require("./WeaponBase");
const WeaponRegistry=require("./WeaponRegistry");
class WeaponFactory {
 static create(type,index){
   const c=WeaponRegistry.get(type,index);
   if(!c) throw new Error(`[WeaponFactory] 未注册武器 type=${type} index=${index}`);
   const w=new c(); w.init(index,type); return w;
 }
 static register(type,index,creator){ WeaponRegistry.register(type,index,creator); }
}
/* ROUND07F concrete registrations */
const WeaponTypes=require("./types");
for(const k of Object.keys(WeaponTypes)){ const [t,i]=k.split(":"); if(!WeaponRegistry.get(t,i)) WeaponFactory.register(t,i,WeaponTypes[k]); }
// ROUND07F
module.exports=WeaponFactory;
