'use strict';
const fs=require('fs'),path=require('path'),child=require('child_process');
const root=path.resolve(__dirname,'..');let checked=0;
function walk(dir){for(const ent of fs.readdirSync(dir,{withFileTypes:true})){const p=path.join(dir,ent.name);if(ent.isDirectory())walk(p);else if(ent.isFile()&&p.endsWith('.js')){child.execFileSync(process.execPath,['--check',p]);checked++;}}}
walk(path.join(root,'src'));
const skills=require(path.join(root,'src/skills'));
const bosses=require(path.join(root,'src/bosses'));
const scenes=require(path.join(root,'src/scenes'));
const requiredHandlers=['SoulCapture','SoulSummon','Inspire','Demolition','RainStorm','Enthrall','CavalryOrder','FangTianHalberd','Devour','Madness','DevourEyes','WarlordSeal'];
const {SkillEffectPort}=skills;const port=new SkillEffectPort();
for(const key of requiredHandlers)if(!port.handlers.has(key))throw new Error(`Missing boss skill handler: ${key}`);
if(!bosses.BOSS_DEFINITIONS||bosses.BOSS_DEFINITIONS.length!==12)throw new Error('Expected 12 boss definitions');
if(typeof scenes.GameOverSceneController!=='function')throw new Error('GameOverSceneController export missing');
console.log(JSON.stringify({status:'PASS',checked,skillHandlers:requiredHandlers,bossCount:bosses.BOSS_DEFINITIONS.length,gameOverUuid:'36WnNn_bSKilkYpbnYn_9A'},null,2));
