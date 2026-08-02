const table=new Map();
module.exports={
 register(type,index,creator){ const k=`${type}:${index}`; if(table.has(k)) throw new Error(`duplicate weapon ${k}`); table.set(k,creator);},
 get(type,index){return table.get(`${type}:${index}`);},
 list(){return Array.from(table.keys());}
};
