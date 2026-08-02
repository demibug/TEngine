'use strict';
class FixedTargetMovement{attach(p){this.projectile=p;this.start={x:p.x,y:p.y};this.target=(p.config&&p.config.target)||this.start;this.progress=0;return this;}onFire(){}update(deltaMs,speed=1){if(!this.projectile)return;this.progress=Math.min(1,this.progress+deltaMs/1000*speed);const t=this.progress;this.projectile.renderNode.x=this.start.x+(this.target.x-this.start.x)*t;this.projectile.renderNode.y=this.start.y+(this.target.y-this.start.y)*t;}recover(){this.projectile=null;this.progress=0;}}
module.exports={FixedTargetMovement};
