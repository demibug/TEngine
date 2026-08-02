'use strict';
class LineMovement{constructor(){this.progress=0;}attach(p){this.projectile=p;const c=p.config||{};this.dx=c.dx||1;this.dy=c.dy||0;return this;}onFire(){}update(deltaMs,speed=1){if(!this.projectile)return;const t=deltaMs/1000*speed;this.projectile.renderNode.x+=this.dx*t;this.projectile.renderNode.y+=this.dy*t;this.progress+=t;}recover(){this.projectile=null;this.progress=0;}}
module.exports={LineMovement};
