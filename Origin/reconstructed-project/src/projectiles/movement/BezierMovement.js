'use strict';
class BezierMovement{attach(p){this.projectile=p;const c=p.config||{};this.p0=c.p0||{x:p.x,y:p.y};this.p1=c.p1||this.p0;this.p2=c.p2||this.p0;this.progress=0;return this;}onFire(){}update(deltaMs,speed=1){if(!this.projectile)return;this.progress=Math.min(1,this.progress+deltaMs/1000*speed);const t=this.progress,u=1-t;this.projectile.renderNode.x=u*u*this.p0.x+2*u*t*this.p1.x+t*t*this.p2.x;this.projectile.renderNode.y=u*u*this.p0.y+2*u*t*this.p1.y+t*t*this.p2.y;}recover(){this.projectile=null;this.progress=0;}}
module.exports={BezierMovement};
