'use strict';
const { ProjectileBase } = require('../ProjectileBase');
class PikeSnakeBullet extends ProjectileBase {
 initializeAppearance(appearance={}) { if(this.imageNode)return; const img=new this.laya.Image(appearance.resourcePath||'resources/img/weapon/bullet/lingShe_1.png'); this.renderNode.addChild(img); this.imageNode=img; }
 applyHit(enemy){ const result=enemy.hit(this.damage,this.attacker); this.applyImpactEffects(enemy); return result; }
}
PikeSnakeBullet.projectileTypeKey='PikeSnakeBullet';
PikeSnakeBullet.DEFAULT_APPEARANCE=Object.freeze({resourcePath:'resources/img/weapon/bullet/lingShe_1.png'});
module.exports={PikeSnakeBullet};
