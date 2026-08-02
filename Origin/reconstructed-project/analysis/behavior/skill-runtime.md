# Skill runtime

Core lifecycle: factory -> attach owner -> cooldown update -> activate -> effect port -> finish -> owner/gameOver recovery.

Recovered registrations: six general active skills, one passive skill, twelve boss skills. Known core effects (stun, confusion, inspire, rain debuff and cavalry summon) have executable handlers. Complex map/UI/VFX effects remain explicit deferred contracts.
