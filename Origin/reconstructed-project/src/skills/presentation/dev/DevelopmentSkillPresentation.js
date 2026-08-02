'use strict';
const { LayaSkillPresentation }=require('../LayaSkillPresentation');
class DevelopmentSkillPresentation extends LayaSkillPresentation {
  constructor(options={}){super(options);this.missingResources=[];}
  loadSpine(resourcePath){this.requireResource({feature:'skill-spine',resourceType:'Spine',formalKey:resourcePath,expectedPath:resourcePath,animationNames:[],sourceRanges:[]});return Promise.resolve(null);}
  createSpine(animationKey,resourcePath){this.requireResource({feature:animationKey,resourceType:'Spine',formalKey:animationKey,expectedPath:resourcePath,animationNames:[],sourceRanges:[]});return null;}
  requireResource(record){this.missingResources.push({...record,presentationStatus:'TODO_RESOURCE_MISSING'});return null;}
}
module.exports={DevelopmentSkillPresentation};
