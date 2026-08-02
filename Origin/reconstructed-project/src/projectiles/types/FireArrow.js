'use strict';
const { ProjectileBase } = require('../ProjectileBase');
class FireArrow extends ProjectileBase {
 initializeAppearance(appearance={}) { if(this.imageNode)return; const img=new this.laya.Image(appearance.resourcePath||'resources/img/weapon/bullet/fireArrowEff_01.jpg'); this.renderNode.addChild(img); this.imageNode=img; }
 applyHit(enemy){ return enemy.hit(this.damage,this.attacker); }
}
FireArrow.projectileTypeKey='FireArrow';
FireArrow.DEFAULT_APPEARANCE=Object.freeze({resourcePath:'resources/img/weapon/bullet/fireArrowEff_01.jpg'});
module.exports={FireArrow};
