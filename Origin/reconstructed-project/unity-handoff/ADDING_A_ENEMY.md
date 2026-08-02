# Adding a ENEMY

1. Add immutable data to `unity-export/config/enemies.json` (or the matching singular catalog name).
2. Implement the domain behavior as a plain class.
3. Register it in the matching Factory/Registry.
4. Keep visual and audio resource keys in presentation metadata.
5. Reset every runtime field before returning the object to the pool.
6. Add it to the Unity ScriptableObject import pipeline if desired.
