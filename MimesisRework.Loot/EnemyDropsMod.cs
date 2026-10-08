using MelonLoader;

[assembly: MelonInfo(typeof(MimesisTestMod.EnemyDropLoot.EnemyDropsMod), "MIMESIS Rework - Enemy Drops", "0.3.0", "MIMESIS Rework")]
[assembly: MelonGame(null, null)]

namespace MimesisTestMod.EnemyDropLoot
{
    public sealed class EnemyDropsMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            EnemyDropLootPreferences.Initialize();
            LoggerInstance.Msg("Threat-scaled enemy drops loaded.");
        }
    }
}
