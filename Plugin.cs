using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using CodeStage.AntiCheat.Detectors;
using TaskbarHero;
using TaskbarHero.Data;
using TaskbarHero.StatusSystem;
using TaskbarHero.UI;
using TaskbarHero.Manager;
using TaskbarHero.EasySaveData;

namespace J3L1XD;

// ─── Toggle States ────────────────────────────────────
static class ModToggles
{
    public static bool GodMode;
    public static bool OneHitKill;
    public static bool Speed5x;
    public static bool AutoChest;
    public static bool AutoActBoss;
    public static bool AntiCheatBypass;
    public static bool ShowMenu = true;
    public static bool XpBoost;
    public static bool UnlockAll;
    public static float SpeedMultiplier = 5f;
    public static float DamageMultiplier = 1000f;
    public static float GoldMultiplier = 9999f;
    public static float XpMultiplier = 9999f;
}

// ─── Anti-Cheat Bypass ────────────────────────────────
[HarmonyPatch(typeof(InjectionDetector), "ykl")]
sealed class NI { static bool Prefix() => !ModToggles.AntiCheatBypass; }

[HarmonyPatch(typeof(SpeedHackDetector), "ykl")]
sealed class NS { static bool Prefix() => !ModToggles.AntiCheatBypass; }

[HarmonyPatch(typeof(WallHackDetector), "ykl")]
sealed class NW { static bool Prefix() => !ModToggles.AntiCheatBypass; }

[HarmonyPatch(typeof(TimeCheatingDetector), "ykl")]
sealed class NT { static bool Prefix() => !ModToggles.AntiCheatBypass; }

// ─── Hero Mods ────────────────────────────────────────
[HarmonyPatch(typeof(Hero), "gpp")]
sealed class HF { static void Postfix(ref float __result) { if (ModToggles.Speed5x) __result *= ModToggles.SpeedMultiplier; } }

[HarmonyPatch(typeof(Hero), "gqm")]
sealed class HI { static bool Prefix() => !ModToggles.GodMode; }

[HarmonyPatch(typeof(Hero), "edk")]
sealed class HD { static bool Prefix() => !ModToggles.GodMode; }

// ─── Monster Kill ─────────────────────────────────────
[HarmonyPatch(typeof(Monster), "gqm")]
sealed class MK
{
    static void Prefix(Monster __instance, ref DamageInfo a)
    {
        if (ModToggles.OneHitKill && __instance != null && __instance.bsbf != EStageType.ACTBOSS)
            a.OriginDamage = Math.Max(a.OriginDamage * ModToggles.DamageMultiplier, ModToggles.DamageMultiplier);
    }
}

// ─── Unlock All (DLC + Pets) ──────────────────────────
[HarmonyPatch(typeof(DLCManager), "hbo")]
sealed class UA { static bool Prefix(ref bool __result) { if (!ModToggles.UnlockAll) return true; __result = true; return false; } }

[HarmonyPatch(typeof(PetManager), "kqt")]
sealed class UP { static bool Prefix(ref bool __result) { if (!ModToggles.UnlockAll) return true; __result = true; return false; } }

[HarmonyPatch(typeof(PetManager), "kqx")]
sealed class UQ { static bool Prefix(ref bool __result) { if (!ModToggles.UnlockAll) return true; __result = true; return false; } }

// Prevent crash when clicking unlocked pet with no save data
[HarmonyPatch(typeof(PetManager), "krd")]
sealed class UR { static void Postfix(int a, ref PetSaveData __result) { if (!ModToggles.UnlockAll || __result != null) return; __result = new PetSaveData(a, true, true); } }

// ─── XP / Drop Rates ──────────────────────────────────
[HarmonyPatch(typeof(AccountStatus), "klm")]
sealed class XP
{
    static void Postfix(EAccountStatus a, ref int __result)
    {
        if (!ModToggles.XpBoost) return;
        if (a == EAccountStatus.DropChanceNormalChest) __result = 10;
        else if (a == EAccountStatus.DropChanceStageBossChest) __result = 10;
        else if (a == EAccountStatus.DropChanceNormalChestPercent) __result = 100;
        else if (a == EAccountStatus.DropChanceStageBossChestPercent) __result = 100;
        else if (a == EAccountStatus.IncreaseGoldAmount) __result = (int)ModToggles.GoldMultiplier;
        else if (a == EAccountStatus.IncreaseExpAmount) __result = (int)ModToggles.XpMultiplier;
        else if (a == EAccountStatus.OpenOneTypeChestAllAtOnce || a == EAccountStatus.OpenAllTypeChestAllAtOnce ||
                 a == EAccountStatus.UnlockAutoOpenNormalChest || a == EAccountStatus.UnlockAutoOpenStageBossChest ||
                 a == EAccountStatus.UnlockAutoOpenActBossChest) __result = 9999;
    }
}

// ─── Chest Callback ───────────────────────────────────
[HarmonyPatch(typeof(vy), "eva")]
sealed class CW { static void Postfix(uz.StageCache a) { J3L1XDKeeper.OnChestReceived(a); } }

// ─── Plugin Entry ─────────────────────────────────────
[BepInPlugin("com.j3l1xd.taskbarhero", "J3L1XD", "1.0.0")]
[BepInProcess("TaskBarHero.exe")]
public sealed class J3L1XDPlugin : BasePlugin
{
    public override void Load()
    {
        var log = BepInEx.Logging.Logger.CreateLogSource("J3L1XD");
        try { new Harmony("com.j3l1xd.taskbarhero").PatchAll(); log.LogInfo("Harmony OK"); }
        catch (Exception e) { log.LogError($"Harmony fail: {e}"); }

        ClassInjector.RegisterTypeInIl2Cpp<J3L1XDKeeper>();
        GameObject go = new("J3L1XDKeeper");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<J3L1XDKeeper>();
        log.LogInfo("J3L1XD v1.0.0 loaded — all cheats OFF. Press F5 for menu.");
    }
}

// ─── Main Mod Controller ──────────────────────────────
public sealed class J3L1XDKeeper : MonoBehaviour
{
    // Reflection cache
    private static readonly MethodInfo OpenAllBoxMethod = AccessTools.Method(typeof(StageBox), "lkk");
    private static readonly FieldInfo BoxTypeField = AccessTools.Field(typeof(StageBox), "m_boxType");
    private static readonly MethodInfo EnterActBossMethod = AccessTools.Method(typeof(StagePortal), "lcq");
    private static readonly FieldInfo SoulStoneTextField = AccessTools.Field(typeof(StagePortal), "text_Soulstone");

    private readonly Dictionary<IntPtr, float> actBossHp = new();
    private readonly Dictionary<IntPtr, float> actBossLastHitAt = new();
    private float nextChestClaimAt;
    private float nextActBossAt;
    private const float ActBossKillSeconds = 60f;

    // GUI state
    private Rect menuRect = new(Screen.width - 380, 50, 350, 500);
    private string statusText = "";
    private float statusTimer;
    private bool stylesInit;
    private bool dragging;
    private Vector2 dragOffset;
    private bool cursorCaptured;
    private bool windowInputCaptured;
    private bool previousCursorVisible;
    private long previousWindowExStyle;
    private IntPtr windowHandle;
    private CursorLockMode previousCursorLockState;
    private const float ItemH = 38;
    private const float SectionH = 24;
    private const float SliderH = 42;
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    // ── Glass UI colors ──
    private static readonly Color ColBg = new(0.055f, 0.055f, 0.055f, 0.98f);
    private static readonly Color ColAccent = new(0.95f, 0.48f, 0.08f, 1f);
    private static readonly Color ColText = new(0.84f, 0.72f, 0.53f, 1f);
    private static readonly Color ColTextDim = new(0.48f, 0.43f, 0.36f, 1f);
    private static readonly Color ColTitle = new(1f, 0.76f, 0.17f, 1f);
    private static readonly Color ColFrameOuter = new(0.01f, 0.01f, 0.012f, 1f);
    private static readonly Color ColFrameMid = new(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color ColFrameInner = new(0.38f, 0.36f, 0.31f, 1f);
    private static readonly Color ColHeader = new(0.36f, 0.05f, 0.045f, 1f);
    private static readonly Color ColHeaderDark = new(0.12f, 0.018f, 0.018f, 1f);
    private static readonly Color ColSlot = new(0.095f, 0.095f, 0.09f, 1f);
    private static readonly Color ColSlotDark = new(0.025f, 0.025f, 0.023f, 1f);
    private static readonly Color ColSlotBorder = new(0.24f, 0.23f, 0.2f, 1f);
    private static readonly Color ColButton = new(0.34f, 0.13f, 0.055f, 1f);

    [DllImport("user32.dll", EntryPoint = "GetActiveWindow")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", EntryPoint = "SetActiveWindow")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", EntryPoint = "ScreenToClient", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    public J3L1XDKeeper(IntPtr p) : base(p) { }
    internal static void OnChestReceived(uz.StageCache _) { /* webhook removed */ }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5)) SetMenuVisible(!ModToggles.ShowMenu);

        if (ModToggles.ShowMenu) ApplyMenuCursor();
        else if (cursorCaptured || windowInputCaptured) RestoreMenuInput();

        if (ModToggles.Speed5x) Time.timeScale = ModToggles.SpeedMultiplier;

        if (ModToggles.OneHitKill || ModToggles.GodMode)
        {
            Hero hero = FindObjectOfType<Hero>();
            if (hero == null) return;
            foreach (Monster c in FindObjectsOfType<Monster>())
            {
                if (c == null || !c.brua) continue;
                if (c.bsbf == EStageType.ACTBOSS)
                {
                    if (ModToggles.GodMode) DamageActBoss(c, hero);
                }
                else if (ModToggles.OneHitKill)
                    c.gqm(new DamageInfo(ModToggles.DamageMultiplier, false, hero, EDamageType.None, false, false));
            }
        }

        if (ModToggles.AutoChest) TryAutoChest();
        if (ModToggles.AutoActBoss) TryAutoActBoss();

        if (statusTimer > 0) statusTimer -= Time.unscaledDeltaTime;
    }

    // ─── Game logic ──────────────────────────────────────
    private void DamageActBoss(Monster monster, Hero hero)
    {
        IntPtr id = monster.Pointer;
        float now = Time.unscaledTime;
        if (!actBossHp.ContainsKey(id))
            actBossHp[id] = Math.Max(monster.UnitHealthController.bscp, 1f);
        if (actBossLastHitAt.TryGetValue(id, out float last) && now - last < 1f) return;
        actBossLastHitAt[id] = now;
        monster.gqm(new DamageInfo(actBossHp[id] / ActBossKillSeconds, false, hero, EDamageType.None, false, false));
    }

    private void TryAutoChest()
    {
        if (Time.unscaledTime < nextChestClaimAt || OpenAllBoxMethod == null) return;
        nextChestClaimAt = Time.unscaledTime + 1f;
        foreach (StageBox box in FindObjectsOfType<StageBox>())
        {
            if (box == null) continue;
            int typeVal = (int)(BoxTypeField?.GetValue(box) ?? -1);
            if (typeVal <= 0) continue;
            OpenAllBoxMethod.Invoke(box, null);
            ShowStatus(typeVal == 2 ? "Auto-chest: Act Boss" : "Auto-chest: Stage Boss");
        }
    }

    private void TryAutoActBoss()
    {
        if (Time.unscaledTime < nextActBossAt || EnterActBossMethod == null || SoulStoneTextField == null) return;
        nextActBossAt = Time.unscaledTime + 3f;
        foreach (StagePortal portal in FindObjectsOfType<StagePortal>())
        {
            if (portal == null || !HasEnoughSoulStones(portal)) continue;
            EnterActBossMethod.Invoke(portal, null);
            ShowStatus("Auto-enter: Act Boss");
            return;
        }
    }

    private static bool HasEnoughSoulStones(StagePortal portal)
    {
        object label = SoulStoneTextField.GetValue(portal);
        if (label == null) return false;
        string text = label.GetType().GetProperty("text")?.GetValue(label)?.ToString();
        if (string.IsNullOrEmpty(text)) return false;
        MatchCollection nums = Regex.Matches(text, "\\d+");
        if (nums.Count < 2) return false;
        return int.TryParse(nums[0].Value, out int owned) &&
               int.TryParse(nums[1].Value, out int needed) &&
               needed > 0 && owned >= needed;
    }

    private void ShowStatus(string msg) { statusText = msg; statusTimer = 2f; }

    private void SetMenuVisible(bool visible)
    {
        ModToggles.ShowMenu = visible;
        if (visible) ApplyMenuCursor();
        else RestoreMenuInput();
    }

    private void ApplyMenuCursor()
    {
        if (!cursorCaptured)
        {
            previousCursorVisible = Cursor.visible;
            previousCursorLockState = Cursor.lockState;
            cursorCaptured = true;
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        ApplyMenuWindowInput();
    }

    private void RestoreMenuInput()
    {
        if (cursorCaptured)
        {
            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLockState;
            cursorCaptured = false;
        }

        RestoreMenuWindowInput();
    }

    private void ApplyMenuWindowInput()
    {
        IntPtr hWnd = ResolveWindowHandle();
        if (hWnd == IntPtr.Zero) return;

        if (!windowInputCaptured)
        {
            previousWindowExStyle = GetWindowExStyle(hWnd);
            windowInputCaptured = true;
        }

        if (!IsNativeCursorOverMenu(hWnd))
        {
            long styleOutsideMenu = GetWindowExStyle(hWnd);
            if (styleOutsideMenu != previousWindowExStyle)
            {
                SetWindowExStyle(hWnd, previousWindowExStyle);
                RefreshWindowStyle(hWnd);
            }
            return;
        }

        long currentStyle = GetWindowExStyle(hWnd);
        long interactiveStyle = currentStyle & ~WsExTransparent & ~WsExNoActivate;
        if (currentStyle != interactiveStyle)
        {
            SetWindowExStyle(hWnd, interactiveStyle);
            RefreshWindowStyle(hWnd);
        }

        SetForegroundWindow(hWnd);
        SetActiveWindow(hWnd);
    }

    private void RestoreMenuWindowInput()
    {
        if (!windowInputCaptured || windowHandle == IntPtr.Zero) return;
        SetWindowExStyle(windowHandle, previousWindowExStyle);
        RefreshWindowStyle(windowHandle);
        windowInputCaptured = false;
    }

    private IntPtr ResolveWindowHandle()
    {
        if (windowHandle != IntPtr.Zero) return windowHandle;

        IntPtr activeWindow = GetActiveWindow();
        if (activeWindow != IntPtr.Zero)
        {
            windowHandle = activeWindow;
            return windowHandle;
        }

        IntPtr unityWindow = FindWindow("UnityWndClass", null);
        if (unityWindow != IntPtr.Zero)
        {
            windowHandle = unityWindow;
            return windowHandle;
        }

        windowHandle = FindWindow(null, "TaskBarHero");
        return windowHandle;
    }

    private bool IsNativeCursorOverMenu(IntPtr hWnd)
    {
        if (!GetCursorPos(out NativePoint point)) return false;
        if (!ScreenToClient(hWnd, ref point)) return false;
        return menuRect.Contains(new Vector2(point.X, point.Y));
    }

    private static long GetWindowExStyle(IntPtr hWnd) => GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();

    private static void SetWindowExStyle(IntPtr hWnd, long style) => SetWindowLongPtr(hWnd, GwlExStyle, new IntPtr(style));

    private static void RefreshWindowStyle(IntPtr hWnd)
    {
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    // ─── Glass-styled Mod Menu ───────────────────────────
    void OnGUI()
    {
        if (!ModToggles.ShowMenu) return;

        if (!stylesInit) InitStyles();

        int prevDepth = GUI.depth;
        GUI.depth = -10000;
        Color prev = GUI.color;

        menuRect.x = Mathf.Clamp(menuRect.x, 0, Screen.width - menuRect.width);
        menuRect.y = Mathf.Clamp(menuRect.y, 0, Screen.height - menuRect.height);

        DrawFrame(menuRect, ColFrameOuter, ColFrameMid, ColBg);

        Rect titleRect = new(menuRect.x + 5, menuRect.y + 5, menuRect.width - 10, 34);
        DrawFrame(titleRect, ColFrameOuter, ColHeaderDark, ColHeader);

        GUIStyle titleStyle = new() { fontSize = 17, fontStyle = FontStyle.Bold,
            normal = { textColor = ColTitle }, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(titleRect.x, titleRect.y + 1, titleRect.width, titleRect.height), "J3L1XD STASH", titleStyle);

        GUIStyle closeStyle = new() { fontSize = 20, fontStyle = FontStyle.Bold,
            normal = { textColor = ColTitle }, hover = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter };
        Rect closeRect = new(titleRect.x + titleRect.width - 29, titleRect.y + 3, 25, 28);
        if (Event.current.type == EventType.MouseDown && closeRect.Contains(Event.current.mousePosition))
        {
            SetMenuVisible(false);
            Event.current.Use();
        }
        DrawFrame(closeRect, ColFrameOuter, ColAccent, ColButton);
        GUI.Label(closeRect, "×", closeStyle);

        Rect accentRect = new(menuRect.x + 8, titleRect.y + titleRect.height + 5, menuRect.width - 16, 3);
        GUI.DrawTexture(accentRect, MakeTex(1, 1, ColAccent));

        float contentY = accentRect.y + 8;
        float y = 0;
        y = DrawSection(contentY, y, "COMBAT");
        y = DrawToggleItem(contentY, y, "God Mode", ref ModToggles.GodMode, "Invulnerable");
        y = DrawToggleItem(contentY, y, "Damage Boost", ref ModToggles.OneHitKill, "Multiply monster damage");
        y = DrawSliderItem(contentY, y, "Damage Multiplier", ref ModToggles.DamageMultiplier, 1f, 1000f, "0x");

        y = DrawSection(contentY, y, "WORLD");
        y = DrawToggleItem(contentY, y, "Speed Boost", ref ModToggles.Speed5x, "Custom game speed");
        y = DrawSliderItem(contentY, y, "Speed Multiplier", ref ModToggles.SpeedMultiplier, 1f, 20f, "0.0x");

        y = DrawSection(contentY, y, "AUTOMATION");
        y = DrawToggleItem(contentY, y, "Auto Chest", ref ModToggles.AutoChest, "Auto-claim boss chests");
        y = DrawToggleItem(contentY, y, "Auto Act Boss", ref ModToggles.AutoActBoss, "Auto-enter portal");

        y = DrawSection(contentY, y, "SYSTEM");
        y = DrawToggleItem(contentY, y, "Anti-Cheat Bypass", ref ModToggles.AntiCheatBypass, "Disable AC");
        y = DrawToggleItem(contentY, y, "XP / Drop Boost", ref ModToggles.XpBoost, "EXP, gold, drops");
        y = DrawSliderItem(contentY, y, "Gold Multiplier", ref ModToggles.GoldMultiplier, 1f, 9999f, "0x");
        y = DrawSliderItem(contentY, y, "XP Multiplier", ref ModToggles.XpMultiplier, 1f, 9999f, "0x");
        y = DrawToggleItem(contentY, y, "Unlock All", ref ModToggles.UnlockAll, "Unlock DLC heroes + pets");

        if (statusTimer > 0 && !string.IsNullOrEmpty(statusText))
        {
            float sy = menuRect.y + 36 + y;
            GUI.DrawTexture(new Rect(menuRect.x + 8, sy, menuRect.width - 16, 1), MakeTex(1, 1, ColAccent));
            GUIStyle statusStyle = new() { fontSize = 11, normal = { textColor = ColAccent },
                alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(menuRect.x, sy + 4, menuRect.width, 20), statusText, statusStyle);
        }

        GUIStyle footStyle = new() { fontSize = 10, normal = { textColor = ColTextDim },
            alignment = TextAnchor.MiddleCenter };
        float fy = menuRect.y + 36 + y + (statusTimer > 0 ? 26 : 2);
        GUI.Label(new Rect(menuRect.x, fy, menuRect.width, 16), "F5 = hide | drag = move", footStyle);

        if (Event.current.type == EventType.MouseDown && titleRect.Contains(Event.current.mousePosition))
        {
            dragOffset = Event.current.mousePosition - menuRect.position;
            dragging = true;
            Event.current.Use();
        }
        if (dragging && Event.current.type == EventType.MouseUp) { dragging = false; Event.current.Use(); }
        if (dragging && Event.current.type == EventType.MouseDrag)
        {
            menuRect.position = Event.current.mousePosition - dragOffset;
            Event.current.Use();
        }

        ConsumeMenuMouseEvents();

        GUI.color = prev;
        GUI.depth = prevDepth;
    }

    private void InitStyles() { stylesInit = true; }

    private float DrawToggleItem(float baseY, float y, string label, ref bool val, string desc)
    {
        Rect r = new(menuRect.x + 8, baseY + y, menuRect.width - 16, 34);
        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            val = !val;
            Event.current.Use();
        }

        bool hover = r.Contains(Event.current.mousePosition);
        Color fill = val ? new Color(0.28f, 0.13f, 0.045f, 1f) : new Color(0.06f, 0.055f, 0.05f, 1f);
        if (hover) fill = val ? new Color(0.36f, 0.17f, 0.055f, 1f) : new Color(0.09f, 0.08f, 0.065f, 1f);
        DrawFrame(r, ColFrameOuter, val ? ColAccent : ColSlotBorder, fill);

        Rect badge = new(r.x + 6, r.y + 5, 62, 24);
        DrawFrame(badge, ColFrameOuter, val ? ColTitle : ColFrameMid, val ? ColAccent : ColSlot);

        GUIStyle stateStyle = new() { fontSize = 10, fontStyle = FontStyle.Bold,
            normal = { textColor = val ? new Color(0.12f, 0.035f, 0.01f, 1f) : new Color(0.45f, 0.36f, 0.26f, 1f) },
            alignment = TextAnchor.MiddleCenter };
        GUI.Label(badge, val ? "ACTIVE" : "OFF", stateStyle);

        Rect gem = new(r.x + r.width - 24, r.y + 9, 16, 16);
        DrawFrame(gem, ColFrameOuter, val ? ColTitle : ColFrameMid, val ? ColTitle : ColSlotDark);

        GUIStyle lbl = new() { fontSize = 13, fontStyle = FontStyle.Bold,
            normal = { textColor = val ? ColTitle : ColText },
            alignment = TextAnchor.MiddleLeft };

        GUI.Label(new Rect(r.x + 78, r.y + 3, r.width - 110, 16), label, lbl);

        GUIStyle dsc = new() { fontSize = 10, normal = { textColor = ColTextDim },
            alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(r.x + 78, r.y + 18, r.width - 110, 12), desc, dsc);

        return y + ItemH;
    }

    private float DrawSection(float baseY, float y, string label)
    {
        Rect r = new(menuRect.x + 8, baseY + y + 3, menuRect.width - 16, 20);
        DrawFrame(r, ColFrameOuter, ColFrameMid, new Color(0.085f, 0.075f, 0.06f, 1f));

        GUIStyle style = new() { fontSize = 10, fontStyle = FontStyle.Bold,
            normal = { textColor = ColAccent }, alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(r.x + 9, r.y + 1, r.width - 18, r.height), label, style);
        return y + SectionH;
    }

    private float DrawSliderItem(float baseY, float y, string label, ref float val, float min, float max, string format)
    {
        Rect r = new(menuRect.x + 8, baseY + y, menuRect.width - 16, SliderH);
        Rect track = new(r.x + 13, r.y + 28, r.width - 26, 6);

        DrawFrame(r, ColFrameOuter, ColSlotBorder, ColSlotDark);

        if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) && r.Contains(Event.current.mousePosition))
        {
            float t = Mathf.Clamp01((Event.current.mousePosition.x - track.x) / track.width);
            float raw = min + (max - min) * t;
            val = max > 100f ? Mathf.Round(raw) : Mathf.Round(raw * 10f) / 10f;
            Event.current.Use();
        }

        GUIStyle nameStyle = new() { fontSize = 12, normal = { textColor = ColText }, alignment = TextAnchor.MiddleLeft };
        GUIStyle valueStyle = new() { fontSize = 12, fontStyle = FontStyle.Bold,
            normal = { textColor = ColTitle }, alignment = TextAnchor.MiddleRight };

        GUI.Label(new Rect(r.x + 12, r.y + 2, r.width - 100, 18), label, nameStyle);
        GUI.Label(new Rect(r.x + r.width - 102, r.y + 2, 90, 18), val.ToString(format), valueStyle);

        float normalized = Mathf.InverseLerp(min, max, val);
        DrawFrame(track, ColFrameOuter, ColFrameMid, new Color(0.075f, 0.07f, 0.06f, 1f));
        GUI.DrawTexture(new Rect(track.x, track.y, track.width * normalized, track.height), MakeTex(1, 1, ColAccent));
        DrawFrame(new Rect(track.x + track.width * normalized - 5, track.y - 5, 10, 16), ColFrameOuter, ColAccent, ColTitle);
        return y + SliderH;
    }

    private void ConsumeMenuMouseEvents()
    {
        if (!menuRect.Contains(Event.current.mousePosition)) return;
        EventType type = Event.current.type;
        if (type == EventType.MouseDown || type == EventType.MouseUp || type == EventType.MouseDrag || type == EventType.ScrollWheel)
            Event.current.Use();
    }

    private void DrawFrame(Rect r, Color outer, Color mid, Color fill)
    {
        GUI.DrawTexture(r, MakeTex(1, 1, outer));
        if (r.width > 4 && r.height > 4)
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), MakeTex(1, 1, mid));
        if (r.width > 8 && r.height > 8)
            GUI.DrawTexture(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), MakeTex(1, 1, fill));
    }

    private Texture2D MakeTex(int w, int h, Color c)
    {
        Texture2D t = new(w, h);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                t.SetPixel(x, y, c);
        t.Apply();
        return t;
    }
}
