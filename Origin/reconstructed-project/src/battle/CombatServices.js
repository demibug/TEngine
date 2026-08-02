'use strict';
/** Named service container for engine-neutral combat composition. */
class CombatServices{
 constructor(services={}){Object.assign(this,services);}
 require(name){const value=this[name];if(!value)throw new Error(`Combat service ${name} is not configured`);return value;}
 snapshot(){return Object.keys(this).sort();}
}
module.exports={CombatServices};
