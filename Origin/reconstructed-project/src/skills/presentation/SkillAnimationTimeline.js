'use strict';
class SkillAnimationTimeline {
  constructor({owner,skill,context,presentation,definition,onEffect,onComplete,onCancel}){
    this.owner=owner;this.skill=skill;this.context=context;this.presentation=presentation;this.definition=definition||{};
    this.onEffect=onEffect;this.onComplete=onComplete;this.onCancel=onCancel;
    this.elapsed=0;this.started=false;this.effectTriggered=false;this.completed=false;this.cancelled=false;this.effectHandle=null;
    this.ownerGeneration=owner&&owner.lifecycleGeneration!=null?owner.lifecycleGeneration:owner&&owner._lifecycleGeneration;
  }
  start(){if(this.started)return this;this.started=true;if(this.presentation)this.presentation.beginBossSkill(this.owner,this.skill,this.definition);if((this.definition.effectAtMs||0)<=0)this.triggerEffect();return this;}
  update(deltaMs){if(this.completed||this.cancelled)return;this.elapsed+=Number(deltaMs)||0;if(!this.effectTriggered&&this.elapsed>=(this.definition.effectAtMs||0))this.triggerEffect();if(this.elapsed>=(this.definition.completeAtMs||1000))this.complete();}
  isOwnerValid(){if(!this.owner||this.owner.inPool||this.owner.currentState===4)return false;const g=this.owner.lifecycleGeneration!=null?this.owner.lifecycleGeneration:this.owner._lifecycleGeneration;return this.ownerGeneration==null||g===this.ownerGeneration;}
  triggerEffect(){if(this.effectTriggered||!this.isOwnerValid())return;this.effectTriggered=true;this.effectHandle=this.onEffect?this.onEffect():null;if(this.presentation){this.presentation.effectPoint(this.owner,this.skill,this.definition);if(this.definition.followupAnimation)this.presentation.playAnimation(this.owner&&this.owner.animation,this.definition.followupAnimation,false);}}
  complete(){if(this.completed||this.cancelled)return;if(!this.effectTriggered)this.triggerEffect();this.completed=true;if(this.effectHandle&&this.effectHandle.disposeOnTimelineEnd&&typeof this.effectHandle.dispose==='function')this.effectHandle.dispose('timeline-complete');if(this.presentation)this.presentation.completeBossSkill(this.owner,this.skill,this.definition);if(this.onComplete)this.onComplete(this.effectHandle);}
  cancel(reason='cancelled'){if(this.completed||this.cancelled)return;this.cancelled=true;if(this.effectHandle&&typeof this.effectHandle.dispose==='function')this.effectHandle.dispose(reason);if(this.presentation)this.presentation.cancelBossSkill(this.owner,this.skill,this.definition,reason);if(this.onCancel)this.onCancel(reason);}
}
module.exports={SkillAnimationTimeline};
