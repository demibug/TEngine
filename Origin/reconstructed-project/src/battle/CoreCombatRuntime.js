'use strict';
const {CombatServices}=require('./CombatServices');
const {CombatLifecycle}=require('./CombatLifecycle');
class CoreCombatRuntime{
 constructor(services={}){this.services=services instanceof CombatServices?services:new CombatServices(services);this.lifecycle=new CombatLifecycle(this.services);}
 startGame(){return this.lifecycle.start();}
 pause(){return this.lifecycle.pause();}
 resume(){return this.lifecycle.resume();}
 gameOver(isWin){return this.lifecycle.gameOver(isWin);}
}
module.exports={CoreCombatRuntime};
