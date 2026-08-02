'use strict';
const { ProjectileBase } = require('../ProjectileBase');
class ShenBiPunch extends ProjectileBase {
 initializeAppearance(appearance={}) { if(this.imageNode)return; const img=new this.laya.Image(appearance.resourcePath||'resources/img/weapon/bullet/shenBiPunch.png'); this.renderNode.addChild(img); this.imageNode=img; }
 applyHit(enemy){ return enemy.hit(this.damage,this.attacker); }
}
ShenBiPunch.projectileTypeKey='ShenBiPunch';
ShenBiPunch.DEFAULT_APPEARANCE=Object.freeze({resourcePath:'resources/img/weapon/bullet/shenBiPunch.png'});
module.exports={ShenBiPunch};
