'use strict';
const { ProjectileBase } = require('../ProjectileBase');
class EagleArrow extends ProjectileBase {
 initializeAppearance(appearance={}) { if(this.imageNode)return; const img=new this.laya.Image(appearance.resourcePath||'resources/img/weapon/bullet/eagleArrow_01.png'); this.renderNode.addChild(img); this.imageNode=img; }
 applyHit(enemy){ return enemy.hit(this.damage,this.attacker); }
}
EagleArrow.projectileTypeKey='EagleArrow';
EagleArrow.DEFAULT_APPEARANCE=Object.freeze({resourcePath:'resources/img/weapon/bullet/eagleArrow_01.png'});
module.exports={EagleArrow};
