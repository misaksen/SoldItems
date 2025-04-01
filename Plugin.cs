using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using MyBox;
using System.Linq;
using Newtonsoft.Json;
using System.IO;
using System.Diagnostics.Eventing.Reader;

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

        MethodInfo original_3 = AccessTools.Method(typeof(DayCycleManager), "StartNextDay");
        MethodInfo patch_3 = AccessTools.Method(typeof(StartNextDay_Patch), "StartNextDay_Patch1");


        // Load json into itemsSold dictionary
        string jsonPath = Path.Combine(Paths.PluginPath, "SoldItemsData.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                string jsonData = File.ReadAllText(jsonPath);
                itemsSold = JsonConvert.DeserializeObject<Dictionary<int, int[]>>(jsonData);
                Logger.LogInfo($"Loaded items sold data from {jsonPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load items sold data: {ex.Message}");
            }
        }
        else
        {
            Logger.LogInfo($"No previous data found at {jsonPath}. Starting fresh.");
        }


        Harmony.Patch(original_1, new HarmonyMethod(patch_1));
        Harmony.Patch(original_2, new HarmonyMethod(patch_2));
        Harmony.Patch(original_3, postfix: new HarmonyMethod(patch_3));
    }

    public static string ConvertItemsSoldToJson()
    {
        return JsonConvert.SerializeObject(Plugin.itemsSold, Formatting.Indented);
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

                // Increment the global itemsSoldCount by the quantity of the current product
                if (Plugin.itemsSold.ContainsKey(kvp.Key))
                {
                    Plugin.itemsSold[kvp.Key] = new int[] {
                        Plugin.itemsSold[kvp.Key][0] + kvp.Value,
                        Plugin.itemsSold[kvp.Key][1],
                        Plugin.itemsSold[kvp.Key][2],
                        Plugin.itemsSold[kvp.Key][3],
                        Plugin.itemsSold[kvp.Key][4],
                        Plugin.itemsSold[kvp.Key][5],
                        Plugin.itemsSold[kvp.Key][6]
                    };
                }
                else
                {
                    Plugin.itemsSold.Add(kvp.Key, new int[] { kvp.Value, 0, 0, 0, 0, 0, 0 });
                }

                Plugin.itemsSoldCount += kvp.Value;
            }

            Logger.LogInfo("-------------------");
            var sortedDict = from entry in Plugin.itemsSold orderby Singleton<LocalizationManager>.Instance.LocalizedProductName(entry.Key) ascending select entry;

            // Print ASCII table
            Logger.LogInfo("Product Name                  | Today | Yesterday | Day Before | 4 Days Ago | 5 Days Ago | 6 Days Ago | 7 Days Ago | Max | Avg | Shelf Capacity");
            Logger.LogInfo("------------------------------+-------+-----------+-----------+------------+------------+------------+------------+-----+-----+---------------");
            foreach (KeyValuePair<int, int[]> kvp in sortedDict)
            {
                string productName = Singleton<LocalizationManager>.Instance.LocalizedProductName(kvp.Key);
                ProductSO Product = Singleton<IDManager>.Instance.ProductSO(kvp.Key);
                string productBrand = Product.ProductBrand;
                int inStock = Singleton<InventoryManager>.Instance.GetInventoryAmount(kvp.Key);
                if (inStock == 0)
                {
                    productName = "**" + productName + " " + productBrand; // Add brand name to product name
                }
                else
                {
                    productName = productName + " " + productBrand; // Add brand name to product name
                }


                productName = productName.Length > 30 ? productName.Substring(0, 30) : productName; // Truncate if longer than 30
                var values = kvp.Value;
                int maxValue = values.Max(); // Calculate the maximum value in the array
                List<DisplaySlot> ds = new List<DisplaySlot>();
                int x1 = Singleton<DisplayManager>.Instance.GetDisplaySlots(kvp.Key, false, ds);
                int productPerShelf = Product.GridLayoutInStorage.productCount;
                int shelfCapacity = x1 * productPerShelf;

                string row = $"{productName.PadRight(30)} | {values[0].ToString().PadLeft(5)} | {values[1].ToString().PadLeft(9)} | {values[2].ToString().PadLeft(9)} | {values[3].ToString().PadLeft(10)} | {values[4].ToString().PadLeft(10)} | {values[5].ToString().PadLeft(10)} | {values[6].ToString().PadLeft(10)} | {maxValue.ToString().PadLeft(3)} | {((int)values.Average()).ToString().PadLeft(3)} | {shelfCapacity.ToString().PadLeft(3)}";

                if (inStock == 0)
                {
                    Logger.LogError($"{row}");
                }
                else if (shelfCapacity > maxValue * 2 && x1 > 1)
                {
                    Logger.LogWarning($"{row}");
                }
                else if (values.Average() > shelfCapacity)
                {
                    Logger.LogError($"{row}");
                }
                else if (maxValue > shelfCapacity * 2)
                {
                    Logger.LogError($"{row}");
                }

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
                Plugin.itemsSold[key] = new int[] { 0, values[0], values[1], values[2], values[3], values[4], values[5] };
            }
            // Reset the items sold count for the next day
            Plugin.itemsSoldCount = 0;

            Logger.LogInfo("Items sold data rotated for the next day.");

            // Save itemsSold data as JSON
            string jsonPath = Path.Combine(Paths.PluginPath, "SoldItemsData.json");
            try
            {
                // Ensure the directory exists
                Directory.CreateDirectory(Paths.PluginPath);

                // Write the JSON file
                File.WriteAllText(jsonPath, Plugin.ConvertItemsSoldToJson());
                Logger.LogInfo($"Items sold data saved to {jsonPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save items sold data: {ex.Message}");
            }


        }
    }

    [HarmonyPatch(typeof(DayCycleManager), "StartNextDay")]
    public class StartNextDay_Patch
    {

        // Function to iterate through the shopping cart and log the items sold
        public static void StartNextDay_Patch1(DayCycleManager __instance)
        {
            var Logger = BepInEx.Logging.Logger.CreateLogSource("SoldItems");
            Logger.LogInfo($"StartNextDay_Patch executed!");
            int items = 0;
            foreach (int prodid in Singleton<ProductLicenseManager>.Instance.UnlockedProducts)
            {
                if (Plugin.itemsSold.TryGetValue(prodid, out int[] soldItems))
                {
                    int maxSold = soldItems.Max();
                    int inStock = Singleton<InventoryManager>.Instance.GetInventoryAmount(prodid);
                    if (inStock < maxSold + 5)
                    {
                        ProductSO Product1 = Singleton<IDManager>.Instance.ProductSO(prodid);
                        int product_in_box = Product1.GridLayoutInBox.productCount;
                        Logger.LogInfo($"Product {Product1.ProductName} in box: {product_in_box} in stock {inStock} max sold {maxSold}");
                        float price = Singleton<PriceManager>.Instance.SellingPrice(prodid);
                        ItemQuantity iq = new ItemQuantity(prodid, price);

                        List<DisplaySlot> ds = new List<DisplaySlot>();
                        int x1 = Singleton<DisplayManager>.Instance.GetDisplaySlots(prodid, false, ds);
                        int productPerShelf = Product1.GridLayoutInStorage.productCount;
                        int shelfCapacity = x1 * productPerShelf;
                        int max = Math.Max(maxSold, shelfCapacity);

                        int toOrder = (int)Math.Ceiling((float)((max - inStock) / product_in_box));
                        int extra = 2;
                        if (product_in_box == 4)
                        {
                            extra = 3;
                        }
                        iq.FirstItemCount = toOrder + extra;
                        items += iq.FirstItemCount;
                        Logger.LogInfo($"Adding {iq.FirstItemCount} to cart for product {Product1.ProductName}");
                        Singleton<CartManager>.Instance.AddCart(iq, SalesType.PRODUCT);
                        Logger.LogInfo($"Total items in cart: {items}");
                        if (items > 40)
                        {
                            Logger.LogInfo($"Purchasing {items} items in cart");
                            Singleton<CartManager>.Instance.MarketShoppingCart.Purchase(false);
                            items = 0;
                        }
                    }

                }
            }
            Singleton<CartManager>.Instance.MarketShoppingCart.Purchase(false);
        }
    }


}