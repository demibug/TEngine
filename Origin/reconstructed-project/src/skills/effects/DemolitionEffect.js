'use strict';
const { EffectHandle }=require('./EffectHandle');
class DemolitionEffect {
  constructor({mapTileManager}={}){this.mapTileManager=mapTileManager;}
  execute({boss}={}){
    if(!boss||!this.mapTileManager)return {status:'MISSING_MAP_TILE_MANAGER'};
    // Original SunShangXiang skill permanently converts an empty cell for this battle.
    const tile=this.mapTileManager.blockRandomAvailable(boss.isPlayerLane,{ownerId:boss.id,reason:'Demolition',permanent:true});
    if(!tile)return {status:'NO_AVAILABLE_TILE',ownerId:boss.id};
    return new EffectHandle({ownerId:boss.id,persistent:true,metadata:{tileKey:tile.key},dispose:reason=>{if(reason==='game-over')this.mapTileManager.unblock(tile.key);}});
  }
}
module.exports={DemolitionEffect};
