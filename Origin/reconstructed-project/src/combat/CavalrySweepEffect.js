'use strict';
class CavalrySweepEffect {
 constructor(){this.type='cavalrySweep';this.reset();}
 reset(){this.owner=null;this.enemyManager=null;this.damage=0;this.multiplier=1;this.radius=0;this.hitSet=new Set();this.active=false;this.delayMs=0;this.elapsed=0;return this;}
 launch({owner,enemyManager,damage=0,multiplier=1,radius=96,delayMs=0}={}){this.owner=owner;this.enemyManager=enemyManager;this.damage=damage;this.multiplier=multiplier;this.radius=radius;this.delayMs=delayMs;this.elapsed=0;this.active=true;return this;}
 update(deltaMs){if(!this.active)return false;this.elapsed+=deltaMs;if(this.elapsed>=this.delayMs)this.hit();if(this.elapsed>=this.delayMs+120)this.cleanup();return this.active;}
 hit(){const list=this.enemyManager?.queryEnemyObjects(this.owner.displayObject.x,this.owner.displayObject.y,this.radius,this.owner.side,[])||[];for(const e of list){if(this.hitSet.has(e.id))continue;this.hitSet.add(e.id);const d=this.damage*this.multiplier;if(typeof e.hit==='function')e.hit(d,this.owner);else if(typeof e.takeDamage==='function')e.takeDamage(d,this.owner);}}
 cleanup(){this.active=false;this.owner=null;this.enemyManager=null;this.hitSet.clear();}
}
module.exports={CavalrySweepEffect};
