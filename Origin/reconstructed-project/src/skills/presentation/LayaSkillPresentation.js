'use strict';
const { SkillPresentationPort }=require('./SkillPresentationPort');
const { SKILL_VFX_MANIFEST }=require('./SkillResourceManifest');
const { createLayaSpineAnimation }=require('../../presentation/LayaSpineAnimation');

class LayaSkillPresentation extends SkillPresentationPort {
  constructor(options={}){super(options);this.prefabFactory=options.prefabFactory||null;this.spineFactory=options.spineFactory||null;this.resourcePathResolver=options.resourcePathResolver||((path)=>path);}
  configure(options={}){super.configure(options);if(options.prefabFactory)this.prefabFactory=options.prefabFactory;if(options.spineFactory)this.spineFactory=options.spineFactory;if(options.resourcePathResolver)this.resourcePathResolver=options.resourcePathResolver;return this;}
  _layer(name){const layers=this.layerResolver&&this.layerResolver();return layers&&layers[name];}
  createSpine(animationKey,resourcePath){
    this.calls.push(['createSpine',animationKey,resourcePath]);
    if(this.animationEntityPool)return this.animationEntityPool.create(animationKey);
    if(this.spineFactory)return this.spineFactory(animationKey,resourcePath);
    if(this.laya)return createLayaSpineAnimation(this.laya,this.resourcePathResolver(resourcePath));
    return this.requireResource({feature:animationKey,resourceType:'Spine',formalKey:animationKey,expectedPath:resourcePath,animationNames:[],sourceRanges:[]});
  }
  createOverlay(key,{color='#000000',alpha=0.7,zIndex=1000,layer='overlayLayer',ownerId=null,blocksInput=false,skin=''}={}){
    if(!this.laya||!this.laya.Sprite)throw new Error('LayaSkillPresentation requires Laya.Sprite');
    const manifest=SKILL_VFX_MANIFEST[key]||Object.values(SKILL_VFX_MANIFEST).find(x=>x.key===key);
    const resolvedSkinRaw=skin||(manifest&&manifest.kind==='image'&&manifest.expected)||'';const resolvedSkin=resolvedSkinRaw?this.resourcePathResolver(resolvedSkinRaw):'';
    const node=resolvedSkin&&this.laya.Image?new this.laya.Image(resolvedSkin):new this.laya.Sprite();
    node.name=key;node.alpha=alpha;node.zIndex=zIndex;node.blocksInput=blocksInput;
    const parent=this._layer(layer);const width=(parent&&parent.width)||this.laya.stage.width||640;const height=(parent&&parent.height)||this.laya.stage.height||1386;
    node.size(width,height);if(!resolvedSkin&&node.graphics&&typeof node.graphics.drawRect==='function')node.graphics.drawRect(0,0,width,height,color);
    if(resolvedSkin){node.width=width;node.height=height;node.sizeGrid='0,0,0,0,0';}
    if(parent)parent.addChild(node);this.track(ownerId,node);
    return {key,node,remove:()=>node.removeSelf(),setAlpha:value=>{node.alpha=value;}};
  }
  createTileMarker(tile,{skin='',color='#7b2d2d',alpha=0.75,ownerId=null}={}){
    const parent=this._layer('skillVfxLayer')||this._layer('overlayLayer');
    let node=null;
    if(this.prefabFactory){try{node=this.prefabFactory.createSync('mapItem');}catch(_error){node=null;}}
    if(!node)node=skin&&this.laya.Image?new this.laya.Image(skin):new this.laya.Sprite();
    node.name=`tile_${tile.side}_${tile.x}_${tile.y}`;node.alpha=alpha;node.pos(tile.pixelX,tile.pixelY);node.size(tile.width,tile.height);
    const tip=node.getChildByName&&node.getChildByName('tip2');if(tip)tip.visible=true;
    if(!skin&&!this.prefabFactory&&node.graphics)node.graphics.drawRect(0,0,tile.width,tile.height,color);
    if(parent)parent.addChild(node);this.track(ownerId,node);return {node,remove:()=>node.removeSelf()};
  }
  createEntityVfx(owner,key,{skin='',layer='skillVfxLayer'}={}){
    const parent=this._layer(layer)||(owner&&owner.visual&&owner.visual.parent);if(!parent)return null;
    const manifest=SKILL_VFX_MANIFEST[key]||Object.values(SKILL_VFX_MANIFEST).find(x=>x.key===key);
    const resourceRaw=skin||(manifest&&manifest.expected)||'';const resource=resourceRaw.endsWith('.lh')?resourceRaw:(resourceRaw?this.resourcePathResolver(resourceRaw):'');
    let node=null;
    if(resource.endsWith('.lh')&&this.prefabFactory){const match=Object.entries(require('../../resources/PrefabCatalog').PREFAB_CATALOG).find(([,entry])=>entry.path===resourceRaw);if(match){try{node=this.prefabFactory.createSync(match[0]);}catch(_error){node=null;}}}
    if(!node)node=resource&&this.laya.Image?new this.laya.Image(resource):new this.laya.Sprite();
    node.name=key;node.anchorX=.5;node.anchorY=.5;
    const x=owner&&owner.centerX!=null?owner.centerX:owner&&owner.displayObject?owner.displayObject.x:owner&&owner.visual?owner.visual.x:0;
    const y=owner&&owner.centerY!=null?owner.centerY:owner&&owner.displayObject?owner.displayObject.y:owner&&owner.visual?owner.visual.y:0;
    node.pos(x,y);parent.addChild(node);this.track(owner&&owner.id,node);return {node,remove:()=>node.removeSelf()};
  }
}
module.exports={LayaSkillPresentation};
