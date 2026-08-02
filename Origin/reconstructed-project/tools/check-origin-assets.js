#!/usr/bin/env node
'use strict';
const fs=require('fs');const path=require('path');
const root=path.resolve(__dirname,'..');
const {SCENE_CATALOG}=require('../src/resources/SceneCatalog');
const {PREFAB_CATALOG}=require('../src/resources/PrefabCatalog');
const {SPINE_CATALOG}=require('../src/resources/SpineCatalog');
const {BOSS_RESOURCE_MANIFEST,SKILL_VFX_MANIFEST}=require('../src/skills/presentation/SkillResourceManifest');
const {validateOriginSceneContracts}=require('../src/core/ClassRegistry');
validateOriginSceneContracts(SCENE_CATALOG);
const errors=[];
for(const item of Object.values(SCENE_CATALOG))if(!fs.existsSync(path.join(root,'origin_project',item.path)))errors.push(item.path);
for(const item of Object.values(PREFAB_CATALOG))if(!fs.existsSync(path.join(root,'origin_project',item.path)))errors.push(item.path);
for(const item of Object.values(SPINE_CATALOG))for(const key of ['path','atlasPath','texturePath'])if(!fs.existsSync(path.join(root,'origin_project',item[key])))errors.push(item[key]);
for(const [key,item] of Object.entries(BOSS_RESOURCE_MANIFEST))if(!item.animationNamesVerified)errors.push(`boss animation:${key}`);
if(errors.length){console.error(errors);process.exit(1);}console.log(JSON.stringify({scenes:Object.keys(SCENE_CATALOG).length,prefabs:Object.keys(PREFAB_CATALOG).length,spines:Object.keys(SPINE_CATALOG).length,bosses:Object.keys(BOSS_RESOURCE_MANIFEST).length,vfx:Object.fromEntries(Object.entries(SKILL_VFX_MANIFEST).map(([k,v])=>[k,v.presentationStatus]))},null,2));
