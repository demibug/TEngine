# State Machines

## Battle
`IDLE → WAITING_TO_START → SPAWNING → WAITING_AFTER_WAVE → FINISHED`

## Friendly unit
`NONE/PLACING → UnitIdle → UnitAttack → UnitIdle → pooled`

## Enemy
`spawn → move → contact/skill → death → unregister → pooled`

## Boss skill
`moving/idle → skill wind-up → effect trigger → recovery → moving/idle`; death or game over cancels the timeline.

## Projectile
`pooled → initialize → fire → movement/hit-enabled → hit/remove → pooled`
