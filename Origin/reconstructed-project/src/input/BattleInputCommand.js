'use strict';
const BattleInputCommandType = Object.freeze({ PURCHASE_AND_PLACE:'PurchaseAndPlace', BEGIN_DRAG:'BeginDrag', MOVE_DRAG:'MoveDrag', COMMIT_PLACEMENT:'CommitPlacement', CANCEL_DRAG:'CancelDrag', MOVE_UNIT:'MoveUnit', MERGE_UNITS:'MergeUnits', REFRESH:'Refresh' });
class BattleInputCommand { constructor(type,payload={}){if(!Object.values(BattleInputCommandType).includes(type))throw new Error(`Unknown BattleInputCommand ${type}`);this.type=type;this.payload={...payload};} }
module.exports={BattleInputCommandType,BattleInputCommand};
