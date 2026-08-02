using Game.Combat.Ports;
using Game.Combat.Runtime;

namespace Game.Combat
{
    // Reference only: wire actual domain services here after porting the JS rules.
    public sealed class CombatCompositionRoot
    {
        public CombatTickDriver TickDriver { get; } = new();
        public ICombatClock Clock { get; }
        public IRandomSource Random { get; }
        public ICombatView View { get; }
        public IAudioPort Audio { get; }
        public IVfxPort Vfx { get; }
        public IScenePort Scenes { get; }

        public CombatCompositionRoot(
            ICombatClock clock,
            IRandomSource random,
            ICombatView view,
            IAudioPort audio,
            IVfxPort vfx,
            IScenePort scenes)
        {
            Clock = clock;
            Random = random;
            View = view;
            Audio = audio;
            Vfx = vfx;
            Scenes = scenes;
        }

        public void StartBattle()
        {
            // Construct repositories/managers, inject ports and configs,
            // register ICombatTickable services, then start the lifecycle order.
        }
    }
}
