using System.Collections.Generic;

namespace Game.Combat.Results
{
    public sealed class BattleResultDto
    {
        public bool IsWin;
        public int Star;
        public int Gold;
        public long BattleDurationMs;
        public int Round;
        public int PlayerTargetHealth;
        public int OpponentTargetHealth;
        public List<object> WeaponFragments = new();
        public int KillCount;
        public int BossKillCount;
        public int EndlessRound;
        public string GameMode = "normal";
        public string ResultState = "LOSE";
    }
}
