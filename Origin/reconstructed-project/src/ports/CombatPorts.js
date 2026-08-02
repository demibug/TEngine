'use strict';
/** Engine-neutral dependency contracts for the Unity migration. */
class CombatClockPort { now(){ throw new Error('CombatClockPort.now not implemented'); } }
class RandomSourcePort { next(){ throw new Error('RandomSourcePort.next not implemented'); } }
class CombatViewPort { spawn(){ throw new Error('CombatViewPort.spawn not implemented'); } remove(){ throw new Error('CombatViewPort.remove not implemented'); } }
class AudioPort { play(){ throw new Error('AudioPort.play not implemented'); } stop(){ throw new Error('AudioPort.stop not implemented'); } }
class VfxPort { create(){ throw new Error('VfxPort.create not implemented'); } remove(){ throw new Error('VfxPort.remove not implemented'); } }
class InputPort { nextCommand(){ return null; } }
class ScenePort { open(){ throw new Error('ScenePort.open not implemented'); } close(){ throw new Error('ScenePort.close not implemented'); } }
class ResourcePort { load(){ throw new Error('ResourcePort.load not implemented'); } }
module.exports={CombatClockPort,RandomSourcePort,CombatViewPort,AudioPort,VfxPort,InputPort,ScenePort,ResourcePort};
