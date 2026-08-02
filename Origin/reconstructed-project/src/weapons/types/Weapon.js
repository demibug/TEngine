const WeaponBase=require("../WeaponBase");
class Weapon extends WeaponBase {
  static config={name:'短枪', desc:'短枪（待恢复具体攻击逻辑）', rarity:0, type:1, index:'10'};
  attack(ctx){
    this.attackCount++;
    if(!this.owner) return;
    // ROUND-07F: concrete weapon entry preserved; attack branch awaits exact bundle recovery.
    return null;
  }
}
module.exports=Weapon;
