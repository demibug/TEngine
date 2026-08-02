# Core Combat Architecture

See `unity-handoff/CORE_COMBAT_OVERVIEW.md`. Runtime composition remains CommonJS for source traceability. Engine-dependent Laya classes are adapters around the domain services; Unity should port the services, not the Laya controllers.
