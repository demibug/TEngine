'use strict';
class UnitMergeService {
  constructor({unitRegistry,levelService,logger=console}={}){if(!unitRegistry||!levelService)throw new TypeError('UnitMergeService requires UnitRegistry and UnitLevelService');Object.assign(this,{unitRegistry,levelService,logger});}
  canMerge(source,target){return source&&target&&source!==target&&source.side===target.side&&source.unitText===target.unitText&&source.level===target.level&&!source.mergeDisabled&&!target.mergeDisabled;}
  merge(sourceId,targetId){const source=this.unitRegistry.getUnit(sourceId),target=this.unitRegistry.getUnit(targetId);if(!this.canMerge(source,target))return{success:false,reason:'单位类型、等级或合成状态不匹配'};const result=this.levelService.upgrade(target,1);if(!result.success)return result;this.unitRegistry.removeUnit(source.id);return{success:true,targetId:target.id,removedId:source.id,level:target.level};}
  swap(aId,bId){const a=this.unitRegistry.getUnit(aId),b=this.unitRegistry.getUnit(bId);if(!a||!b)return{success:false,reason:'单位不存在'};const pa={x:a.gridPosition.x,y:a.gridPosition.y},pb={x:b.gridPosition.x,y:b.gridPosition.y};a.setPlacement(a.containerType,pb.x,pb.y);b.setPlacement(b.containerType,pa.x,pa.y);this.unitRegistry.reposition(a);this.unitRegistry.reposition(b);return{success:true};}
}
module.exports={UnitMergeService};
