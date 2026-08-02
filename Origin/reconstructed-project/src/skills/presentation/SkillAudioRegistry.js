'use strict';
const { BOSS_RESOURCE_MANIFEST } = require('./SkillResourceManifest');
class SkillAudioRegistry {
  constructor(audio=null){this.audio=audio;this.calls=[];this.activeLoops=new Map();}
  configure(audio){this.audio=audio;return this;}
  play(key,{loop=false,ownerId=null}={}){
    if(!key)return null;this.calls.push(['play',key,loop,ownerId]);
    let handle=null;
    if(this.audio){
      if(typeof this.audio.playSound==='function')handle=this.audio.playSound(key,loop);
      else if(typeof this.audio.play==='function')handle=this.audio.play(key,{loop});
    }
    if(loop&&ownerId!=null)this.activeLoops.set(`${ownerId}:${key}`,handle||key);
    return handle;
  }
  playBossSkill(bossKey,ownerId){const cfg=BOSS_RESOURCE_MANIFEST[bossKey];if(!cfg)return[];return cfg.audio.map((key,i)=>this.play(key,{loop:key.includes('cycle'),ownerId}));}
  stop(key,ownerId=null){this.calls.push(['stop',key,ownerId]);if(this.audio){if(typeof this.audio.stopSound==='function')this.audio.stopSound(key);else if(typeof this.audio.stop==='function')this.audio.stop(key);}if(ownerId!=null)this.activeLoops.delete(`${ownerId}:${key}`);}
  clearOwner(ownerId){for(const id of [...this.activeLoops.keys()])if(id.startsWith(`${ownerId}:`)){const key=id.slice(id.indexOf(':')+1);this.stop(key,ownerId);}}
  gameOver(){for(const id of [...this.activeLoops.keys()]){const [ownerId,...parts]=id.split(':');this.stop(parts.join(':'),ownerId);}this.activeLoops.clear();}
}
module.exports={SkillAudioRegistry};
