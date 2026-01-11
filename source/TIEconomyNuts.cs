using HarmonyLib;
using PavonisInteractive.TerraInvicta;
using PavonisInteractive.TerraInvicta.Ship;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityModManagerNet;


namespace TIEconomyNuts
{
    static class Main
    {
        public static bool enabled;
        public static UnityModManager.ModEntry mod;
        public static Settings settings;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            settings = Settings.Load<Settings>(modEntry);
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            mod = modEntry;
            modEntry.OnToggle = OnToggle;
            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            return true;
        }

        //Boilerplate code, draws the configurable settings in the UMM
        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Draw(modEntry);
        }

        //Boilerplate code, saves settings changes to the xml file
        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }
    }

    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("IP Power Multiplier", Collapsible = true, Tooltip = ".35 by default (vanilla), \ntry .38, \n or .404, \n do not try .553, \n and anything above is complete and utter nuts, beyond any redemption, repair or reasoning.")] public float IPPowerMultipier = 0.35f;
        //[Draw("Emissions Multiplier", Collapsible = true, Tooltip = "1.0 by default (vanilla). \nDepending on what kind of IP Power you use, this needs to be changed to avoid crazy global warming.")] public float EmissionsMultiplier = 1f;
        //[Draw("Billions per IP (fixed priorities)", Collapsible = true, Tooltip = "150 by default (hello Dmitri big boss), \nbut you can change it to anything you want. Have fun!")] public double BillionsPerIP = 150.0;
        //[Draw("Give PopGrowth based on sustainability score", Collapsible = true, Tooltip = "Based on sustainability score, give nations \npopulation growth bonus (0.011% per sustainability point by default mod value)")] public bool SustainabilityBonusFlag = false;
        //[Draw("PopGrowth per Sustainability Score multiplier", Collapsible = true, Tooltip = "0.011 by default")] public double PopGrowthSustainabilityMultiplier = 0.011;
        //[Draw("MissionDifficultyEconomyScore - DO NOT CHANGE IT (LIKELY REMOVED LATER)", Collapsible = true, Tooltip = "Better not change it.")] public float MissionDifficultyEconomyScore = 0.333333343f;

        // Boilerplate code to save your settings to a Settings.xml file when changed
        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        //Hook to allow to do things when a value is changed, if you want
        public void OnChange()
        {
        }

    }

    [HarmonyPatch(typeof(TIRegionState), nameof(TIRegionState.NationsWithClaim))]
    class Patch_RegionState_NationsWithClaim
    {
        public static void Postfix(TIRegionState __instance, 
                                                ref List<TINationState> __result,
                                                bool requireExtantNation,
                                                bool requireExtantClaim,
                                                bool includeCurrentOwner,
                                                bool capitalsOnly)
        {
            
            if (!requireExtantClaim)
            {
                return;
            }

            var tr = Traverse.Create(__instance);

            List<TINationState> _claimsOnRegion; 

            _claimsOnRegion = tr.Field("_claimsOnRegion").GetValue<List<TINationState>>();

            if (_claimsOnRegion == null)
            {
                return;
            }

            var newResult = __result ?? new List<TINationState>();

            foreach (TINationState nationState in _claimsOnRegion)
            {

                if (nationState == null)
                {
                    continue;
                }

                if (requireExtantNation)
                {
                    if (!nationState.extant)
                    {
                        continue;
                    }
                }
                else
                {
                    var alien = GameStateManager.AlienNation();
                    if (nationState == alien && !nationState.extant)
                    {
                        continue;
                    }
                }

                if (capitalsOnly)
                {
                    var requiredCapital = nationState.extant ? nationState.capital : nationState.originalCapital;
                    if (requiredCapital != __instance)
                    {
                        continue;
                    }
                }

                if (!includeCurrentOwner && nationState.regions.Contains(__instance))
                {
                    continue;
                }


                if (!newResult.Contains(nationState)) // avoid duplicates
                {
                    newResult.Add(nationState);
                }
            }
            __result = newResult;
        }

    }

    [HarmonyPatch(typeof(TINationState), nameof(TINationState.ModifyGDP))]
    class Patch
    {
        
        static bool Prefix(TINationState __instance, double value, TINationState.GDPChangeReason reason)
        {
            // IN TERRA INVICTA SOURCE CODE:

            // GDP IS A PROPERTY (DOUBLE)
            // POPULATION IS A FIELD (I BELIEVE IT'S FLOAT?)
            // ECONOMYSCORE IS A PROPERTY (FLOAT)
            // MissionDifficultyEconomyScore is a Property (float)

            // tracker_GDPChangeReason_CurrentTrackingPeriod is a field (Dictionary)
            // tracker_GDPChangeReason_AllTime is a field (Dictionary)

            // !!! For Traverse, you need to know whether it's a Field or Property that you need to get, so always pay attention to the source code !!!

            var tr = Traverse.Create(__instance);

            double gdp = tr.Property("GDP").GetValue<double>();
            gdp += value;

            float pop = tr.Field("population").GetValue<float>();
            double minGDP = (double)pop * 100.0;
            if (gdp < minGDP) gdp = minGDP;

            tr.Property("GDP").SetValue(gdp);

            float economyScore = (float)Mathd.Pow(gdp / 1000000000.0, (double)Main.settings.IPPowerMultipier);

            float missionDifficultyEconomyScore = (float)Mathd.Pow(gdp / 1000000000.0, (double)TIGlobalConfig.globalConfig.TIMissionModifier_NationEconomyPower);

            tr.Property("economyScore").SetValue(economyScore);
            tr.Property("missionDifficultyEconomyScore").SetValue(missionDifficultyEconomyScore);

            __instance.SetDataDirty();

            var factions = tr.Property("FactionsWithControlPoint").GetValue<List<TIFactionState>>();
            factions.ForEach(x => x.SetResourceIncomeDataDirty(FactionResource.Research));

            var cur = tr.Field("tracker_GDPChangeReason_CurrentTrackingPeriod").GetValue<Dictionary<TINationState.GDPChangeReason, float>>();
            var all = tr.Field("tracker_GDPChangeReason_AllTime").GetValue<Dictionary<TINationState.GDPChangeReason, float>> ();

            cur[reason] += (float)value;
            all[reason] += (float)value;

            return false; // skip the original
        }
    }

    public sealed class ClaimRegionOption : TIPolicyOption
    {
        // I can't add enums to the TI source codes. 
        // However, when Terra Invicta asks this PolicyOption its enum, all we need is to give her the 19 (or any other int number)
        // since enums are just basically integers
        // so we fool the TI infrastructure, and mask as an honest-to-god TIPolicyOption!
        // heist of the year, yeah?
        public override PolicyType GetPolicyType() => (PolicyType)19; 

        public override bool Allowed(TINationState nationState)
        {
            if (nationState == null) return false;
            if (nationState.alienNation) return false;
            if (!nationState.extant) return false;

            return GetPossibleTargets(nationState).Count > 0;
        }

        public override bool RequiresTargets() => true;

        public override bool RequiresTargetConfirm() => false;

        public override IList<TIGameState> GetPossibleTargets(TINationState policyNation)
        {
            return policyNation.rivals.Cast<TIGameState>().ToList();

        }

        public override void OnPassage(TINationState enactingNation, TIGameState policyTarget)
        {

            foreach (TIRegionState region in policyTarget.ref_nation.regions)
            {
                enactingNation.SetClaim(region, false, false);
                region.AddClaim(enactingNation);

            }
        }
        public override int Importance(TINationState policyNation, TIGameState target) => 1;
    }

    internal static class CustomPolicies
    {
        public const int ClaimRegionId = 19;
        public static readonly PolicyType ClaimRegion = (PolicyType)ClaimRegionId;
    }

    [HarmonyPatch(typeof(PolicyManager), nameof(PolicyManager.Initialize))]
    public static class Patch_PolicyManager_Initialize
    {

        // we postfix, pretty straightforward
        static void Postfix() 
        {
            // Register handler, make sure it's not already in there
            if (!PolicyManager.policies.ContainsKey(CustomPolicies.ClaimRegion))
            {
                PolicyManager.policies.Add(CustomPolicies.ClaimRegion, new ClaimRegionOption());
            }

            // Make it show up under "Set National Policy" (this list controls menu contents)
            if (!PolicyManager.RegularPolicyNames_SetPolicy.Contains(CustomPolicies.ClaimRegion))
            {
                PolicyManager.RegularPolicyNames_SetPolicy.Add(CustomPolicies.ClaimRegion);
            }
        }
    }

}
