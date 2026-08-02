#!/usr/bin/env node
'use strict';
const fs=require('fs'); const path=require('path'); const cp=require('child_process');
const root=path.resolve(__dirname,'..');
const required=['src/skills/SkillManager.js','src/bosses/BossManager.js','src/battle/WaveManager.js','src/entities/types/Mob1Enemy.js','src/entities/types/ZombieEnemy.js'];
for(const f of required)if(!fs.existsSync(path.join(root,f)))throw new Error(`Missing ${f}`);
for(const file of cp.execFileSync('find',['src','-name','*.js'],{cwd:root,encoding:'utf8'}).trim().split('\n').filter(Boolean))cp.execFileSync(process.execPath,['--check',file],{cwd:root,stdio:'pipe'});
const {SkillFactory}=require('../src/skills'); const {BossFactory}=require('../src/bosses'); const {ENEMY_TYPE_KEYS}=require('../src/battle/EnemyFactory');
const skills=new SkillFactory().keys(); const bosses=new BossFactory().keys();
if(skills.length!==19)throw new Error(`Expected 19 skills, found ${skills.length}`);
if(bosses.length!==12)throw new Error(`Expected 12 bosses, found ${bosses.length}`);
if(ENEMY_TYPE_KEYS.length!==7)throw new Error(`Expected 7 enemy types, found ${ENEMY_TYPE_KEYS.length}`);
console.log(JSON.stringify({status:'PASS',skills:skills.length,bosses:bosses.length,enemies:ENEMY_TYPE_KEYS.length},null,2));
