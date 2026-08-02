'use strict';
class PikeAttackEffect {
  constructor(){this.type='pike';this.reset();}
  reset(){this.owner=null;this.target=null;this.enemyManager=null;this.damage=0;this.radius=0;this.hitSet=new Set();this.active=false;this.elapsed=0;this.durationMs=0;this.hitEnabled=false;return this;}
  launch({owner,target,enemyManager,damage=0,radius=48,durationMs=180}={}){this.owner=owner;this.target=target;this.enemyManager=enemyManager;this.damage=damage;this.radius=radius;this.durationMs=durationMs;this.elapsed=0;this.active=true;this.hitEnabled=false;return this;}
  update(deltaMs){if(!this.active)return false;this.elapsed+=deltaMs;if(this.elapsed>=this.durationMs*0.25)this.hitEnabled=true;if(this.hitEnabled)this.hit();if(this.elapsed>=this.durationMs)this.cleanup();return this.active;}
  hit(){if(!this.active||!this.enemyManager)return;const x=this.owner.displayObject.x,y=this.owner.displayObject.y;const targets=this.enemyManager.queryEnemyObjects(x,y,this.radius,this.owner.side,[]);for(const e of targets){if(this.hitSet.has(e.id))continue;this.hitSet.add(e.id);if(typeof e.hit==='function')e.hit(this.damage,this.owner);else if(typeof e.takeDamage==='function')e.takeDamage(this.damage,this.owner);}}
  cleanup(){this.active=false;this.owner=null;this.target=null;this.enemyManager=null;this.hitSet.clear();}
}
module.exports={PikeAttackEffect};
