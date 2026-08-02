#!/usr/bin/env node
'use strict';
const cp=require('node:child_process'),fs=require('node:fs'),path=require('node:path');const root=path.resolve(__dirname,'..');
cp.execFileSync(process.execPath,[path.join(root,'tools/run-single-game-flow.js')],{cwd:root,stdio:'inherit'});
cp.execFileSync(process.execPath,[path.join(root,'tools/export-unity-config.js')],{cwd:root,stdio:'inherit'});
for(const dir of ['unity-handoff','unity-export/config'])if(!fs.existsSync(path.join(root,dir)))throw new Error(`Missing ${dir}`);
for(const file of ['src/deck/DeckManager.js','src/input/BattleInputController.js','src/ai/AIController.js','src/battle/BattleEconomy.js','src/battle/CoreCombatRuntime.js'])if(!fs.existsSync(path.join(root,file)))throw new Error(`Missing ${file}`);
console.log(JSON.stringify({status:'PASS',round:'08',singleGame:true,unityExport:true},null,2));
