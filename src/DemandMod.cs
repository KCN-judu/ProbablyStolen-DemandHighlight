using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DemandHighlight
{
    // Outlines what the current customer wants: green = wanted, amber = stolen, red = contraband, corner
    // brackets = wanted things are inside. During an inspection only stolen goods and contraband are marked.
    //
    // Work happens only when something changed: the customer or their intent, an item (validated by the game or a
    // tag changed), an item entering or leaving an inventory, or a container window opening or closing. Changes
    // are batched to one walk at most every 0.15 s. With no change, nothing runs. A walk covers what is on screen
    // and reuses every cached verdict (see Rules); a walk cut short by its time budget continues next frame.
    public sealed class DemandMod : IMod
    {
        public const string ModId = "demand_highlight";
        const string HarmonyId = "mod." + ModId;
        const string Tag = "[DemandHighlight] ";
        const string HotkeyPref = "DemandHighlight.hotkey";
        const string PanelKeyPref = "DemandHighlight.panelKey";
        // No default keys: both are bound in the game's options menu (keybind section).
        const KeyCode DefaultKey = KeyCode.None, DefaultPanelKey = KeyCode.None;
        const float MinGap = 0.15f, IntentPoll = 0.5f;
        const double SlowWalkMs = 8.0;

        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static string modId = ModId;
        static Harmony harmony;
        static GameObject ticker;
        static ConfigEntry<int> thickness, corner;
        static ConfigEntry<bool> clientGoods, fill;
        static bool optionsAdded;

        static bool on = true;
        static bool dirty = true;
        static bool drawn;
        static float nextAllowed, nextIntentPoll, nextSlowLog;
        static int lastIntent;
        static readonly System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
        static StoreClient lastClient;
        static KeyCode toggleKey = DefaultKey, panelKey = DefaultPanelKey;
        static bool filtering;
        static bool warned;

        public void Init(ModManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.ID)) modId = manifest.ID;
        }

        // Enabling is all or nothing: if a patch target is missing (after a game update, say) or any step fails,
        // everything already installed is taken down again, one line goes to Player.log and the mod stays off.
        // A half-enabled mod would react to some changes and not others.
        public void OnEnable()
        {
            try
            {
                ReadConfig();
                Install();
                dirty = true;
            }
            catch (Exception e)
            {
                TearDown();
                Debug.LogWarning(Tag + "could not start, left off: " + e.Message);
            }
        }

        public void OnDisable()
        {
            TearDown();
        }

        static void Install()
        {
            if (harmony == null)
            {
                // Types are named at each call: the game's mod scanner asks for a permission when reflection
                // goes through a Type it cannot see.
                var h = new Harmony(HarmonyId);
                harmony = h;
                var mark = Hook("MarkDirty");
                var changed = Hook("ItemChanged");
                var moved = Hook("ItemMoved");
                var tagged = Hook("TagChanged");
                var window = Hook("WindowChanged");
                Patch(h, typeof(GameItemElement).GetMethod("Validate", AnyInstance, null, Type.EmptyTypes, null), "GameItemElement.Validate", changed);
                Patch(h, typeof(GameItem).GetMethod("EnableTag", AnyInstance, null, new[] { typeof(string), typeof(bool) }, null), "GameItem.EnableTag", tagged);
                Patch(h, typeof(GameItem).GetMethod("DisableTag", AnyInstance, null, new[] { typeof(string), typeof(bool) }, null), "GameItem.DisableTag", tagged);
                Patch(h, typeof(GameItem).GetMethod("SetEnableDisableTag", AnyInstance, null, new[] { typeof(string), typeof(bool) }, null), "GameItem.SetEnableDisableTag", tagged);
                Patch(h, typeof(GameItem).GetMethod("ModifyTag", AnyInstance, null, new[] { typeof(string), typeof(Action<TagState>), typeof(bool) }, null), "GameItem.ModifyTag", tagged);
                Patch(h, typeof(GameItem).GetMethod("SetGameItemType", AnyInstance, null, new[] { typeof(string) }, null), "GameItem.SetGameItemType", tagged);
                Patch(h, typeof(GameItemElement).GetMethod("RotCW", AnyInstance, null, Type.EmptyTypes, null), "GameItemElement.RotCW", mark);
                Patch(h, typeof(GameItemElement).GetMethod("RotCCW", AnyInstance, null, Type.EmptyTypes, null), "GameItemElement.RotCCW", mark);
                Patch(h, typeof(GameItem).GetMethod("AddParent", AnyInstance, null, new[] { typeof(GraphNodeStorage) }, null), "GameItem.AddParent", moved);
                Patch(h, typeof(GameItem).GetMethod("RemoveParent", AnyInstance, null, new[] { typeof(GraphNodeStorage) }, null), "GameItem.RemoveParent", moved);
                int shows = 0, hides = 0;
                foreach (var m in typeof(PixelWindow).GetMethods(AnyInstance))
                {
                    if (m.Name == "Show") { h.Patch(m, null, window); shows++; }
                    else if (m.Name == "Hide") { h.Patch(m, null, window); hides++; }
                }
                if (shows == 0 || hides == 0) throw new MissingMethodException("PixelWindow.Show/Hide not found");
            }
            if (ticker == null)
            {
                ticker = new GameObject("DemandHighlight.Ticker");
                UnityEngine.Object.DontDestroyOnLoad(ticker);
                ticker.hideFlags = HideFlags.HideAndDontSave;
                ticker.AddComponent<Ticker>();
            }
        }

        static HarmonyMethod Hook(string name)
        {
            var m = typeof(DemandMod).GetMethod(name, AnyStatic);
            if (m == null) throw new MissingMethodException("DemandMod." + name + " not found");
            return new HarmonyMethod(m);
        }

        static void Patch(Harmony h, MethodInfo target, string name, HarmonyMethod postfix)
        {
            if (target == null) throw new MissingMethodException(name + " not found");
            h.Patch(target, null, postfix);
        }

        // Takes down everything the mod installed or created; safe to call at any point, also mid-install.
        static void TearDown()
        {
            if (harmony != null)
            {
                try { harmony.UnpatchAll(HarmonyId); } catch (Exception e) { Debug.LogWarning(Tag + "unpatching failed: " + e.Message); }
                harmony = null;
            }
            if (ticker != null) { UnityEngine.Object.Destroy(ticker); ticker = null; }
            try { Painter.DestroyAll(); } catch (Exception) { }
            try { Sprites.Clear(); } catch (Exception) { }
            try { Panel.Destroy(); } catch (Exception) { }
            drawn = false;
            lastClient = null;
        }

        static void MarkDirty() { dirty = true; }

        // An item changed: its own entry and those of the containers above it are stale.
        static void ItemChanged(GameItem __instance)
        {
            dirty = true;
            Rules.Forget(__instance);
        }

        // A tag changed: as above, and the filter counts are stale.
        static void TagChanged(GameItem __instance)
        {
            dirty = true;
            Filter.StockVersion++;
            Rules.Forget(__instance);
        }

        // An item entered or left a container: the item's record (it may now be movable or not) and the insides of
        // the containers it left or entered are stale.
        static void ItemMoved(GameItem __instance, GraphNodeStorage node)
        {
            dirty = true;
            Filter.StockVersion++;
            Rules.Forget(__instance);
            Rules.ForgetAbove(node);
        }

        // Only a container window opening or closing changes what is on screen; tooltips and other windows don't.
        static void WindowChanged(PixelWindow __instance)
        {
            var owners = __instance.parentItems;
            if (owners != null && owners.Count > 0) dirty = true;
        }

        static string L(string key) { return ModHelper.GetLocalized(modId, key); }

        static void ReadConfig()
        {
            try
            {
                var key = PlayerPrefs.GetInt(HotkeyPref, (int)DefaultKey);
                toggleKey = Enum.IsDefined(typeof(KeyCode), key) ? (KeyCode)key : DefaultKey;
            }
            catch (Exception) { toggleKey = DefaultKey; }
            try
            {
                var key = PlayerPrefs.GetInt(PanelKeyPref, (int)DefaultPanelKey);
                panelKey = Enum.IsDefined(typeof(KeyCode), key) ? (KeyCode)key : DefaultPanelKey;
            }
            catch (Exception) { panelKey = DefaultPanelKey; }
            Panel.L = L;
            Panel.Changed = () => { dirty = true; nextAllowed = 0f; };

            if (optionsAdded) return;
            optionsAdded = true;
            try
            {
                var config = ModHelper.GetConfig(modId);
                thickness = config.Define("lineThickness", 2, "General", () => L("opt_thickness"), 1, 6, () => L("opt_thickness_tip"), false);
                corner = config.Define("cornerRadius", 2, "General", () => L("opt_corner"), 0, 8, () => L("opt_corner_tip"), false);
                fill = config.Define("fillTint", true, "General", () => L("opt_fill"), () => L("opt_fill_tip"), false);
                clientGoods = config.Define("highlightClientGoods", false, "General", () => L("opt_goods"), () => L("opt_goods_tip"), false);
                thickness.OnChanged += _ => Restyle();
                corner.OnChanged += _ => Restyle();
                fill.OnChanged += _ => Restyle();
                clientGoods.OnChanged += _ => dirty = true;
            }
            catch (Exception e) { Debug.LogWarning(Tag + "settings unavailable, using defaults: " + e.Message); }
            try
            {
                ModHelper.AddGameOptionKeyBind(modId, "keybind", "toggleKey", () => L("opt_key"),
                    () => toggleKey,
                    k => { toggleKey = k; try { PlayerPrefs.SetInt(HotkeyPref, (int)k); PlayerPrefs.Save(); } catch (Exception) { } },
                    () => L("opt_key_tip"));
                ModHelper.AddGameOptionKeyBind(modId, "keybind", "panelKey", () => L("opt_panel_key"),
                    () => panelKey,
                    k => { panelKey = k; try { PlayerPrefs.SetInt(PanelKeyPref, (int)k); PlayerPrefs.Save(); } catch (Exception) { } },
                    () => L("opt_panel_key_tip"));
            }
            catch (Exception e) { Debug.LogWarning(Tag + "key bindings unavailable: " + e.Message); }
            ApplyLook();
        }

        static void ApplyLook()
        {
            Painter.Thickness = thickness != null ? Mathf.Clamp(thickness.Value, 1, 6) : 2;
            Painter.Corner = corner != null ? Mathf.Clamp(corner.Value, 0, 8) : 2;
            Painter.Fill = fill == null || fill.Value;
        }

        static void Restyle()
        {
            ApplyLook();
            Sprites.Clear();
            dirty = true;
        }

        internal static void Tick()
        {
            try
            {
                if (!KeyBindHelper.SuppressHotkeys)
                {
                    if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
                    {
                        on = !on;
                        dirty = true;
                        Debug.Log(Tag + (on ? "on" : "off"));
                    }
                    if (panelKey != KeyCode.None && Input.GetKeyDown(panelKey)) Panel.Toggle();
                }

                if (!on)
                {
                    if (drawn) { Painter.HideAll(); drawn = false; }
                    lastClient = null;
                    return;
                }

                // Switching between the customer highlight and the item filter recolours plain matches.
                if (Filter.Active != filtering)
                {
                    filtering = Filter.Active;
                    Painter.Filtering = filtering;
                    Sprites.Clear();
                    dirty = true;
                }

                float now = Time.unscaledTime;
                var client = Rules.CurrentClient();
                if (client != lastClient) { lastClient = client; dirty = true; nextAllowed = 0f; }
                if (client != null && now >= nextIntentPoll)
                {
                    nextIntentPoll = now + IntentPoll;
                    int intent = Rules.Intent(client);
                    if (intent != lastIntent) { lastIntent = intent; dirty = true; }
                }
                if (client == null && !filtering)
                {
                    if (drawn) { Painter.HideAll(); drawn = false; }
                    dirty = false;
                    return;
                }

                if (dirty && now >= nextAllowed)
                {
                    dirty = false;
                    nextAllowed = now + MinGap;
                    timer.Reset();
                    timer.Start();
                    bool complete;
                    bool any = Rules.Walk(client, clientGoods != null && clientGoods.Value, out complete);
                    double walkMs = timer.Elapsed.TotalMilliseconds;
                    if (any) { Painter.Paint(complete); drawn = true; }
                    else if (drawn) { Painter.HideAll(); drawn = false; }
                    // Cut short by the time budget: carry on next frame.
                    if (!complete) { dirty = true; nextAllowed = 0f; }
                    timer.Stop();
                    double ms = timer.Elapsed.TotalMilliseconds;
                    if (ms > SlowWalkMs && now >= nextSlowLog)
                    {
                        nextSlowLog = now + 2f;
                        Debug.Log(Tag + "slow refresh: " + ms.ToString("0.0") + " ms (walk " + walkMs.ToString("0.0") +
                            " ms, draw " + (ms - walkMs).ToString("0.0") + " ms; visited " + Rules.Visited +
                            ", computed " + Rules.Computed + ", marked " + Rules.Hits.Count +
                            (complete ? ")" : ", continuing)"));
                    }
                }
            }
            catch (Exception e)
            {
                // Never retry a failing refresh every frame: an exception per frame is itself a stutter.
                nextAllowed = Time.unscaledTime + 1f;
                if (!warned) { warned = true; Debug.LogWarning(Tag + "refresh failed: " + e); }
            }
        }
    }

    public sealed class Ticker : MonoBehaviour
    {
        void Update() { DemandMod.Tick(); }
    }
}
