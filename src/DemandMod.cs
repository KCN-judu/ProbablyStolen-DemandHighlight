using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DemandHighlight
{
    // Outlines what the current customer wants: green = wanted, amber = stolen, red = contraband, corner
    // brackets = wanted things are inside. During an inspection only stolen goods and contraband are marked.
    //
    // The mod is three parts that only pass data along:
    //   front (Intent.Pure.cs, GameIntent): what the customer or the item filter asks for, as a Query value;
    //   back (Engine): the answer to a Query over the store, as a Result;
    //   painter (Painter, Sprites): a Result drawn on screen.
    //
    // Work happens only when something changed: the query (read every 0.5 s and before each walk, and compared by
    // value), an item (validated by the game or a tag changed), an item entering or leaving an inventory, or a
    // container window opening or closing. Changes are batched to one walk at most every 0.15 s. With no change,
    // nothing runs. A walk covers what is on screen and reuses every answer still valid (see Engine); a walk cut short
    // by its time budget continues next frame.
    //
    // Leaving the store releases everything kept from it (see Release).
    //
    // This file is the entry point (IMod): it installs the Harmony postfixes that only mark work as needed, reads the
    // options and key bindings (ModHelper.GetConfig, ModHelper.AddGameOptionKeyBind), and runs Tick once per frame
    // through Ticker. Patched game types: GameItemElement (Validate, RotCW, RotCCW), GameItem (the tag methods,
    // SetGameItemType, AddParent, RemoveParent) and PixelWindow (Show, Hide). All patches are postfixes and none
    // changes a result.
    public sealed class DemandMod : IMod
    {
        public const string ModId = "demand_highlight";
        const string HarmonyId = "mod." + ModId;
        internal const string Tag = "[DemandHighlight] ";
        const string HotkeyPref = "DemandHighlight.hotkey";
        const string PanelKeyPref = "DemandHighlight.panelKey";
        // No default keys: both are bound in the game's options menu (keybind section).
        const KeyCode DefaultKey = KeyCode.None, DefaultPanelKey = KeyCode.None;
        // MinGap: shortest time between two walks. QueryPoll: how often the query is read between walks, because the
        // game gives no event for a customer changing what they do or want (a flexi buyer choosing, an inspection
        // starting).
        const float MinGap = 0.15f, QueryPoll = 0.5f;
        const double SlowWalkMs = 8.0;

        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static string modId = ModId;
        static Harmony harmony;
        static GameObject ticker;
        static ConfigEntry<int> thickness, corner;
        static ConfigEntry<bool> clientGoods, fill, filterNumbers;
        static bool optionsAdded;

        // on: the player's toggle. dirty: something changed since the last walk. drawn: outlines are currently shown.
        static bool on = true;
        static bool dirty = true;
        // Set when the query changed; the next complete walk writes one line to the log.
        static bool logNext;
        static bool drawn;
        static float nextAllowed, nextPoll, nextSlowLog;
        static Query query = Query.Nothing;
        static readonly System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
        static StoreClient lastClient;
        static KeyCode toggleKey = DefaultKey, panelKey = DefaultPanelKey;
        static bool filtering;
        static bool warned;

        // The game passes the manifest; its ID is the key for localized text and options, so it is read instead of ModId.
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
                // Pictures made in code, offered to texture packs (Marks.cs). Not part of the all or nothing above:
                // without it the filter still works, with the cross drawn at first use.
                try { Marks.Offer(); } catch (Exception e) { Debug.LogWarning(Tag + "could not offer pictures to texture packs: " + e.Message); }
                // The public query API (QueryHost), for other mods. Also outside the all or nothing: the filter and the
                // highlight do not need it.
                try { QueryHost.Publish(); Debug.Log(Tag + "query API " + QueryApi.Version + " published"); }
                catch (Exception e) { Debug.LogWarning(Tag + "could not publish the query API: " + e.Message); }
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
            try { Marks.Withdraw(); } catch (Exception) { }
            try { QueryHost.Withdraw(); } catch (Exception) { }
        }

        // Safe to run again after OnDisable: each part is created only when missing. The Validate, tag, AddParent and
        // RemoveParent patches are what keep the answers kept in Engine correct; without any of them an item could
        // keep a stale verdict.
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
                // PixelWindow.Show and Hide have several overloads; every one is patched so no way of opening a
                // container window is missed.
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
                // The Ticker lives across scenes, but holds nothing from the store: it only calls Tick.
                ticker = new GameObject("DemandHighlight.Ticker");
                UnityEngine.Object.DontDestroyOnLoad(ticker);
                ticker.hideFlags = HideFlags.HideAndDontSave;
                ticker.AddComponent<Ticker>();
            }
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // The postfix named name in this class, found by reflection so a rename fails at startup, not silently.
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
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (harmony != null)
            {
                try { harmony.UnpatchAll(HarmonyId); } catch (Exception e) { Debug.LogWarning(Tag + "unpatching failed: " + e.Message); }
                harmony = null;
            }
            if (ticker != null) { UnityEngine.Object.Destroy(ticker); ticker = null; }
            try { Painter.DestroyAll(); } catch (Exception) { }
            try { Sprites.Clear(); } catch (Exception) { }
            try { Panel.Destroy(); } catch (Exception) { }
            try { Marks.Release(); } catch (Exception) { }
            drawn = false;
            lastClient = null;
        }

        // Only a single-mode load of a scene other than the store releases (rule in the code standard: nothing kept
        // after leaving the store; the decision itself is StoreExit.Left, with a headless test).
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!StoreExit.Left(scene.name, mode == LoadSceneMode.Single)) return;
            try { Release(); }
            catch (Exception e) { Debug.LogWarning(Tag + "releasing the store failed: " + e.Message); }
        }

        // A store's items, their UI, windows and customers all point at each other, so one reference kept here would
        // keep the whole store in memory after leaving it. Every record, outline and window of the mod that points
        // into the store is dropped; the next store fills them again. The item filter's choices stay.
        static void Release()
        {
            Engine.Release();
            GameIntent.Release();
            Painter.DestroyAll();
            Panel.Destroy();
            Marks.Release();
            lastClient = null;
            query = Query.Nothing;
            drawn = false;
            dirty = true;
        }

        static void MarkDirty() { dirty = true; }

        // An item changed: what is kept about it, and the insides of the containers above it, are stale.
        static void ItemChanged(GameItem __instance)
        {
            dirty = true;
            Engine.Forget(__instance);
        }

        // A tag changed: as above, and the filter counts are stale.
        static void TagChanged(GameItem __instance)
        {
            dirty = true;
            Filter.StockVersion++;
            Engine.Forget(__instance);
        }

        // An item entered or left a container: the item's record (it may now be movable or not) and the insides of
        // the containers it left or entered are stale.
        static void ItemMoved(GameItem __instance, GraphNodeStorage node)
        {
            dirty = true;
            Filter.StockVersion++;
            Engine.Forget(__instance);
            Engine.ForgetAbove(node);
        }

        // Only a container window opening or closing changes what is on screen; tooltips and other windows don't.
        static void WindowChanged(PixelWindow __instance)
        {
            var owners = __instance.parentItems;
            if (owners != null && owners.Count > 0) dirty = true;
        }

        // Localized text of this mod (Localization/*.csv), looked up each time so a language change applies at once.
        static string L(string key) { return ModHelper.GetLocalized(modId, key); }

        // Reads the saved keys and defines the options once; the options live in the game's config, the keys in PlayerPrefs.
        // A failure here leaves defaults in place and does not stop the mod.
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
                filterNumbers = config.Define("filterNumbers", true, "General", () => L("opt_numbers"), () => L("opt_numbers_tip"), false);
                filterNumbers.OnChanged += on => Panel.SetNumbers(on);
                Panel.Numbers = () => filterNumbers == null || filterNumbers.Value;
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

        // Thickness, rounding or fill changed: the cached textures no longer match, so they are rebuilt on the next walk.
        static void Restyle()
        {
            ApplyLook();
            Sprites.Clear();
            dirty = true;
        }

        // One frame of the mod: hotkeys, then the query, then at most one time-limited walk. A walk happens only when
        // dirty is set and MinGap has passed.
        internal static void Tick()
        {
            try
            {
                // Not while a key is being bound, nor while the filter's box is being typed in.
                if (!KeyBindHelper.SuppressHotkeys && !Panel.Typing)
                {
                    if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
                    {
                        on = !on;
                        dirty = true;
                        Debug.Log(Tag + (on ? "on" : "off"));
                    }
                    if (panelKey != KeyCode.None && Input.GetKeyDown(panelKey)) Panel.Toggle();
                }
                Panel.Refresh();

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
                var client = GameIntent.CurrentClient();
                if (client != lastClient) { lastClient = client; dirty = true; nextAllowed = 0f; nextPoll = 0f; }

                // The query is read on the poll and right before a walk; a different value means new work.
                bool due = dirty && now >= nextAllowed;
                if (due || now >= nextPoll)
                {
                    nextPoll = now + QueryPoll;
                    var q = GameIntent.Read(client, clientGoods != null && clientGoods.Value);
                    if (!q.Equals(query))
                    {
                        query = q;
                        dirty = true;
                        logNext = true;
                        due = now >= nextAllowed;
                    }
                }
                if (query.Kind == Query.Op.Nothing)
                {
                    if (drawn) { Painter.HideAll(); drawn = false; }
                    if (logNext) { logNext = false; Debug.Log(Tag + "query: nothing"); }
                    dirty = false;
                    return;
                }

                if (due)
                {
                    dirty = false;
                    nextAllowed = now + MinGap;
                    timer.Reset();
                    timer.Start();
                    bool complete;
                    bool any = Engine.Run(query, GameIntent.Ask, out complete);
                    double walkMs = timer.Elapsed.TotalMilliseconds;
                    if (logNext && complete) { logNext = false; Debug.Log(Tag + "query: " + query + "; " + Stats()); }
                    if (any) { Painter.Paint(Engine.Result); drawn = true; }
                    else if (drawn) { Painter.HideAll(); drawn = false; }
                    // Cut short by the time budget: carry on next frame.
                    if (!complete) { dirty = true; nextAllowed = 0f; }
                    timer.Stop();
                    double ms = timer.Elapsed.TotalMilliseconds;
                    if (ms > SlowWalkMs && now >= nextSlowLog)
                    {
                        nextSlowLog = now + 2f;
                        Debug.Log(Tag + "slow refresh: " + ms.ToString("0.0") + " ms (walk " + walkMs.ToString("0.0") +
                            " ms, draw " + (ms - walkMs).ToString("0.0") + " ms; " + Stats() +
                            (complete ? ")" : ", continuing)"));
                    }
                }
            }
            catch (Exception e)
            {
                // Never retry a failing refresh every frame: an exception per frame is itself a stutter.
                dirty = true;
                nextAllowed = Time.unscaledTime + 1f;
                if (!warned) { warned = true; Debug.LogWarning(Tag + "refresh failed: " + e); }
            }
        }

        static string Stats()
        {
            return "visited " + Engine.Visited + ", judged " + Engine.Computed + ", asked the game " + Engine.Asked +
                ", marked " + Engine.Result.Items.Count;
        }
    }

    // Unity calls Update on a MonoBehaviour every frame; this is the mod's only per-frame hook.
    public sealed class Ticker : MonoBehaviour
    {
        void Update() { DemandMod.Tick(); }
    }
}
