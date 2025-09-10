using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Reflection;

namespace DynBoneWrangler
{
    public class DynBoneWrangler : ResoniteMod
    {
        public override string Name => "DynBoneWrangler";
        public override string Author => "isotach";
        public override string Version => "2.0.0";
        public override string Link => "https://github.com/isovel/DynBoneWrangler";

        [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> enabled =
            new ModConfigurationKey<bool>("enabled",
                "Should the mod be enabled", () => true);

        [AutoRegisterConfigKey] private static readonly ModConfigurationKey<float> disableThreshold =
            new ModConfigurationKey<float>("disableThreshold",
                "Disable DynamicBoneChain updates when your FPS is below this value", () => 17.0f);

        [AutoRegisterConfigKey] private static readonly ModConfigurationKey<float> enableThreshold =
            new ModConfigurationKey<float>("enableThreshold",
                "Re-enable DynamicBoneChain updates when your FPS is above this value", () => 22.0f);

        private static ModConfiguration Config = null!; // assigned in OnEngineInit

        public override void OnEngineInit()
        {
            var harmony = new Harmony("ch.isota.DynBoneWrangler");
            Config = GetConfiguration()!;   // the "!" tells the compiler we know it's not null
            Config.Save(true);

            harmony.PatchAll();
            Msg("Initialized!");
        }

        [HarmonyPatch(typeof(DynamicBoneChainManager))]
        class DynamicBoneChainManager_Patch
        {
            private static bool _shouldUpdate = true;

            [HarmonyPrefix]
            [HarmonyPatch("Update")]
            static bool Prefix(Worker __instance)
            {
                if (!Config.GetValue(enabled)) return true;

                CheckShouldUpdate(__instance);

                return _shouldUpdate;
            }

            private static void CheckShouldUpdate(Worker worker)
            {
                float localUserFPS = GetFPS(worker);

                if (_shouldUpdate)
                {
                    if (localUserFPS < Config.GetValue(disableThreshold))
                        _shouldUpdate = false;
                }
                else
                {
                    if (localUserFPS > Config.GetValue(enableThreshold))
                        _shouldUpdate = true;
                }
            }

            // --- New: resilient FPS getter for post-split builds ---
            private static float GetFPS(Worker worker)
            {
                try
                {
                    var sys = worker.Engine.SystemInfo;
                    var t = sys.GetType();

                    // Try a few known names across versions
                    PropertyInfo? p =
                        t.GetProperty("ImmediateFPS", BindingFlags.Public | BindingFlags.Instance)
                        ?? t.GetProperty("SmoothedFPS", BindingFlags.Public | BindingFlags.Instance)
                        ?? t.GetProperty("FPS", BindingFlags.Public | BindingFlags.Instance);

                    if (p != null && p.PropertyType == typeof(float))
                        return (float)(p.GetValue(sys) ?? 0f);

                    // Fallback: estimate from delta-time if available
                    // Prefer world delta if present, then engine update delta
                    try
                    {
                        // World delta
                        var world = worker.World;
                        var wtProp = world?.GetType().GetProperty("DeltaTime", BindingFlags.Public | BindingFlags.Instance);
                        if (wtProp != null)
                        {
                            var dt = Convert.ToSingle(wtProp.GetValue(world) ?? 0f);
                            if (dt > 0f) return 1f / dt;
                        }
                    }
                    catch { /* ignore and try next */ }

                    try
                    {
                        // Engine delta
                        var eType = worker.Engine.GetType();
                        var dtProp = eType.GetProperty("DeltaTime", BindingFlags.Public | BindingFlags.Instance);
                        if (dtProp != null)
                        {
                            var dt = Convert.ToSingle(dtProp.GetValue(worker.Engine) ?? 0f);
                            if (dt > 0f) return 1f / dt;
                        }
                    }
                    catch { /* ignore */ }
                }
                catch { /* ignore and fall through */ }

                // Final safety: high FPS so we don't unnecessarily disable updates
                return 120f;
            }
        }
    }
}
