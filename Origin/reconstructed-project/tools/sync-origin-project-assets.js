#!/usr/bin/env node
'use strict';
const fs=require('fs');const path=require('path');
const root=path.resolve(__dirname,'..');const source=path.join(root,'origin_project');
const targetArg=process.argv.find(x=>x.startsWith('--target='));const target=path.resolve(targetArg?targetArg.slice(9):path.join(root,'dist-client'));
if(!fs.existsSync(source))throw new Error(`origin_project not found: ${source}`);
fs.mkdirSync(target,{recursive:true});
const includeOriginalJs=process.argv.includes('--include-original-js');
const skip=new Set(['project.private.config.json','code.fortify.config.json']);
function copyDir(src,dst,relative=''){
 for(const entry of fs.readdirSync(src,{withFileTypes:true})){
  const rel=path.join(relative,entry.name);if(skip.has(rel))continue;
  if(!includeOriginalJs&&(rel==='js'||rel==='game.js'))continue;
  const a=path.join(src,entry.name),b=path.join(dst,entry.name);
  if(entry.isDirectory()){fs.mkdirSync(b,{recursive:true});copyDir(a,b,rel);}else fs.copyFileSync(a,b);
 }
}
copyDir(source,target);
console.log(JSON.stringify({source,target,includeOriginalJs},null,2));
