'use strict';
const { ProjectileBase } = require('../ProjectileBase');
class HuoFengHuang extends ProjectileBase {
 initializeAppearance(appearance={}) { if(this.imageNode)return; const img=new this.laya.Image(appearance.resourcePath||'resources/img/weapon/bullet/huoFengHuang_01.png'); this.renderNode.addChild(img); this.imageNode=img; }
 applyHit(enemy){ return enemy.hit(this.damage,this.attacker); }
}
HuoFengHuang.projectileTypeKey='HuoFengHuang';
HuoFengHuang.DEFAULT_APPEARANCE=Object.freeze({resourcePath:'resources/img/weapon/bullet/huoFengHuang_01.png'});
module.exports={HuoFengHuang};
