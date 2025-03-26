using System.Reflection;
using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using MyBox;
using System.Linq;

namespace SoldItems;

[BepInPlugin("misak.supermarket.simulator.solditems", "SoldItems", "0.9.0")]
[BepInProcess("Supermarket Simulator.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static HarmonyLib.Harmony Harmony;
    private static int itemsSoldCount = 0;
    private static Dictionary<int, int[]> itemsSold = new Dictionary<int, int[]>();

    private void Awake()
    {
        Logger.LogInfo($"Plugin SoldItems is loaded!");
        Harmony = new("misak.supermarket.simulator.solditems");
        MethodInfo original_1 = AccessTools.Method(typeof(Checkout), "StartCheckout");
        MethodInfo patch_1 = AccessTools.Method(typeof(StartCheckout_Patch), "StartCheckout_Patch1");
        
        MethodInfo original_2 = AccessTools.Method(typeof(DayCycleManager), "FinishTheDay");
        MethodInfo patch_2 = AccessTools.Method(typeof(FinishTheDay_Patch), "FinishTheDay_Patch1");
        
        Harmony.Patch(original_1, new HarmonyMethod(patch_1));
        Harmony.Patch(original_2, new HarmonyMethod(patch_2));
    }

    [HarmonyPatch(typeof(Checkout), "StartCheckout")]
    public class StartCheckout_Patch
    {

        // Function to iterate through the shopping cart and log the items sold
        public static bool StartCheckout_Patch1(ItemQuantity shoppingCart) 
        {
            var Logger = BepInEx.Logging.Logger.CreateLogSource("SoldItems");
            Logger.LogInfo($"StartCheckout_Patch executed!");

            foreach (KeyValuePair<int, int> kvp in shoppingCart.Products)
            {
                string productName = Singleton<LocalizationManager>.Instance.LocalizedProductName(kvp.Key);
                Logger.LogInfo($"Product: {productName}, Quantity: {kvp.Value}");
                
                // Increment the global itemsSoldCount by the quantity of the current product
                if (Plugin.itemsSold.ContainsKey(kvp.Key))
                {
                    Plugin.itemsSold[kvp.Key] = new int[] {Plugin.itemsSold[kvp.Key][0] + kvp.Value, Plugin.itemsSold[kvp.Key][1], Plugin.itemsSold[kvp.Key][2]};
                }
                else
                {
                    Plugin.itemsSold.Add(kvp.Key, new int[] {kvp.Value, 0, 0});
                }
                
                Plugin.itemsSoldCount += kvp.Value;
            }

            Logger.LogInfo("-------------------");
            var sortedDict = from entry in Plugin.itemsSold orderby Singleton<LocalizationManager>.Instance.LocalizedProductName(entry.Key) ascending select entry;

            // Print ASCII table
            Logger.LogInfo("Product Name          | Today | Yesterday | Day Before");
            Logger.LogInfo("----------------------+-------+-----------+-----------");
            foreach (KeyValuePair<int, int[]> kvp in sortedDict)
            {
                string productName = Singleton<LocalizationManager>.Instance.LocalizedProductName(kvp.Key);
                var values = kvp.Value;
                Logger.LogInfo($"{productName.PadRight(22)} | {values[0].ToString().PadLeft(5)} | {values[1].ToString().PadLeft(9)} | {values[2].ToString().PadLeft(9)}");
            }
            Logger.LogInfo("-------------------");
            Logger.LogInfo($"Total items sold so far: {Plugin.itemsSoldCount}");
            return true;
        }

    }

    [HarmonyPatch(typeof(DayCycleManager), "FinishTheDay")]
    public class FinishTheDay_Patch
    {
        public static void FinishTheDay_Patch1()
        {
            var Logger = BepInEx.Logging.Logger.CreateLogSource("SoldItems");
            Logger.LogInfo($"FinishTheDay_Patch executed!");

            foreach (var key in Plugin.itemsSold.Keys.ToList())
            {
                int[] values = Plugin.itemsSold[key];
                // Rotate the array values one position to the right
                Plugin.itemsSold[key] = new int[] { 0, values[0], values[1] };
            }

            Logger.LogInfo("Items sold data rotated for the next day.");
           
        }
    }
}
