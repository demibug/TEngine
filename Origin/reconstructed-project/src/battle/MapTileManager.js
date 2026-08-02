'use strict';
/**
 * Runtime state for skill-controlled battle cells. The visual marker is optional;
 * placement legality is always enforced independently of missing assets.
 */
class MapTileManager {
  constructor({gameData=null,placementReservations=null,presentation=null,randomSource=Math.random,logger=console}={}) {
    Object.assign(this,{gameData,placementReservations,presentation,randomSource,logger});
    this.blocked=new Map();
  }
  configure(options={}){Object.assign(this,options);return this;}
  key(side,x,y){return `${side?'player':'opponent'}:${x}:${y}`;}
  isBlocked(side,x,y){return this.blocked.has(this.key(side,x,y));}
  canPlace(side,x,y){return !this.isBlocked(side,x,y);}
  blockTile(side,x,y,{ownerId=null,reason='skill',permanent=true}={}){
    const map=this.gameData&&this.gameData.map;
    if(!map)throw new Error('MapTileManager requires gameData.map');
    if(x<0||y<0||x>=map.width||y>=map.height)throw new RangeError(`Map tile outside map: ${x},${y}`);
    const key=this.key(side,x,y);
    if(this.blocked.has(key))return this.blocked.get(key);
    const tile={key,side:Boolean(side),x,y,pixelX:x*map.gridWidth,pixelY:y*map.gridHeight,width:map.gridWidth,height:map.gridHeight,reason,permanent:Boolean(permanent),ownerId,marker:null,sourceCode:map.blockAt(x,y)};
    if(this.presentation)tile.marker=this.presentation.createTileMarker(tile,{skin:'resources/img/gameObject/enemy/flower.png',ownerId});
    this.blocked.set(key,tile);
    if(this.placementReservations)this.placementReservations.add(`blocked:${key}`);
    return tile;
  }
  availableTiles(side){
    const map=this.gameData&&this.gameData.map;
    if(!map)return [];
    const candidates=[];
    for(let x=0;x<map.width;x++)for(let y=0;y<map.height;y++){
      if(this.isBlocked(side,x,y))continue;
      // Demolition chooses empty deployment cells for the affected lane, not route cells.
      if(typeof map.isBuildableForSide==='function'&&!map.isBuildableForSide(side,x,y))continue;
      candidates.push({x,y});
    }
    return candidates;
  }
  blockRandomAvailable(side,{ownerId=null,reason='Demolition',permanent=true}={}){
    const candidates=this.availableTiles(side);
    if(!candidates.length)return null;
    const index=Math.min(candidates.length-1,Math.floor(Math.max(0,Math.min(.999999999,this.randomSource()))*candidates.length));
    const chosen=candidates[index];
    return this.blockTile(side,chosen.x,chosen.y,{ownerId,reason,permanent});
  }
  unblock(key){
    const tile=this.blocked.get(key);if(!tile)return false;
    if(tile.marker&&typeof tile.marker.remove==='function')tile.marker.remove();
    if(this.placementReservations)this.placementReservations.delete(`blocked:${key}`);
    this.blocked.delete(key);return true;
  }
  clearOwner(ownerId,{includePermanent=false}={}){for(const [key,tile] of [...this.blocked])if(tile.ownerId===ownerId&&(includePermanent||!tile.permanent))this.unblock(key);}
  gameOver(){for(const key of [...this.blocked.keys()])this.unblock(key);this.blocked.clear();}
  get count(){return this.blocked.size;}
}
module.exports={MapTileManager};
