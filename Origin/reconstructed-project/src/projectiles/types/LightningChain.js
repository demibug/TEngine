'use strict';
const { ProjectileBase } = require('../ProjectileBase');
class LightningChain extends ProjectileBase {
 initializeAppearance(appearance={}) { if(this.imageNode)return; const img=new this.laya.Image(appearance.resourcePath||'resources/img/weapon/bullet/lightningChain_01.png'); this.renderNode.addChild(img); this.imageNode=img; }
 applyHit(enemy){ const result=enemy.hit(this.damage,this.attacker); this.applyImpactEffects(enemy); return result; }
}
LightningChain.projectileTypeKey='LightningChain';
LightningChain.DEFAULT_APPEARANCE=Object.freeze({resourcePath:'resources/img/weapon/bullet/lightningChain_01.png'});
module.exports={LightningChain};
