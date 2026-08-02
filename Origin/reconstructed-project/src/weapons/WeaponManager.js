'use strict';
const WeaponFactory = require('./WeaponFactory');
class WeaponManager {
  constructor({ buffManager = null, logger = console } = {}) { this.weapons = new Map(); this.buffManager = buffManager; this.logger = logger; this.nextId = 1; this.started = false; }
  configure(options={}){Object.assign(this,options);return this;}
  init() {}
  startGame(){this.started=true;}
  create(type,index,owner=null){const weapon=WeaponFactory.create(type,index);weapon.runtimeId=this.nextId++;if(owner)this.attach(owner,weapon);this.weapons.set(weapon.runtimeId,weapon);return weapon;}
  attach(owner,weaponOrType,index){const weapon=typeof weaponOrType==='object'?weaponOrType:this.create(weaponOrType,index);weapon.projectileFactory=owner&&owner.projectileManager&&owner.projectileManager.projectileFactory;weapon.attach(owner,this.buffManager);owner.weapon=weapon;this.weapons.set(weapon.runtimeId||this.nextId++,weapon);return weapon;}
  equipDefault(unit){if(!unit)return null;if(unit.unitText==='弓')return this.create(0,1,unit);return null;}
  update(dt){for(const weapon of this.weapons.values())if(weapon.active&&typeof weapon.update==='function')weapon.update(dt);}
  remove(weapon){if(!weapon)return false;if(weapon.owner&&weapon.owner.weapon===weapon)weapon.owner.weapon=null;weapon.gameOver();return this.weapons.delete(weapon.runtimeId);}
  gameOver(){for(const weapon of [...this.weapons.values()])this.remove(weapon);this.weapons.clear();this.started=false;}
  get count(){return this.weapons.size;}
}
module.exports=WeaponManager;module.exports.WeaponManager=WeaponManager;
