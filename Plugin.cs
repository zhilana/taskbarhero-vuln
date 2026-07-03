using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TaskbarHero;
using TaskbarHero.Data;
using TaskbarHero.StatusSystem;
using TaskbarHero.UI;
using UnityEngine;

namespace J3L1XD;

static class CheatValues
{
    public const float AttackSpeed = 10f;
    public const float MoveSpeed = 100f;
    public const float MoveStepMultiplier = 8f;
    public const float ExpMultiplier = 10_000_000_000_000f;
    public const long GoldInjectionAmount = long.MaxValue;

    public static float MulExp(float value)
    {
        if (value <= 0f)
            return value;
        var result = value * ExpMultiplier;
        return float.IsInfinity(result) ? float.MaxValue : result;
    }
}

[HarmonyPatch(typeof(Hero), "gra")]
sealed class GodModePatch
{
    static bool Prefix() => false;
}

[HarmonyPatch(typeof(DamageInfo), "gsv")]
sealed class DamageBoostPatch
{
    static void Postfix(ref float __result)
    {
        __result = 1_000_000_000f;
    }
}

[HarmonyPatch(typeof(Monster), "Update")]
sealed class MonsterAutoHitPatch
{
    static readonly ConstructorInfo DamageInfoCtor = AccessTools.TypeByName("TaskbarHero.DamageInfo")
        .GetConstructor(new[] { typeof(float), typeof(bool), typeof(Unit), typeof(EDamageType), typeof(bool), typeof(bool) });
    static readonly MethodInfo ApplyDamage = AccessTools.Method(typeof(Monster), "gra");

    static void Postfix(Monster __instance)
    {
        if (__instance == null)
            return;

        var damage = DamageInfoCtor.Invoke(new object[] { 1_000_000_000f, false, null, EDamageType.Melee, true, true });
        ApplyDamage.Invoke(__instance, new[] { damage, false });
    }
}


[HarmonyPatch(typeof(Hero), "Update")]
sealed class AttackSpeedPatch
{
    static void Postfix(Hero __instance)
    {
        var animator = __instance.GetComponent<Animator>();
        if (animator != null)
            animator.speed = CheatValues.AttackSpeed;
    }
}


[HarmonyPatch(typeof(Unit), "gtq")]
sealed class AttackSpeedStatPatch
{
    static void Postfix(Unit __instance, ref float __result)
    {
        if (__instance is Hero)
            __result = CheatValues.AttackSpeed;
    }
}

[HarmonyPatch(typeof(Unit), "gtv")]
sealed class MoveSpeedPatch
{
    static void Postfix(Unit __instance, ref float __result)
    {
        if (__instance is Hero)
            __result = CheatValues.MoveSpeed;
    }
}

[HarmonyPatch(typeof(Unit), "gsq")]
sealed class MoveStepPatch
{
    static void Prefix(Unit __instance, ref float a)
    {
        if (__instance is Hero)
            a *= CheatValues.MoveStepMultiplier;
    }
}

static class OneShotCoins
{
    static bool Done;

    public static void Try(ref long amount, EGoldCurrencySource source)
    {
        if (Done || amount <= 0 || source != EGoldCurrencySource.MonsterKill)
            return;

        amount = Math.Max(amount, CheatValues.GoldInjectionAmount);
        Done = true;
    }
}

[HarmonyPatch(typeof(vb.tq), "isd")]
sealed class OneShotCoinsBasePatch
{
    static void Prefix(ref long a, EGoldCurrencySource b) => OneShotCoins.Try(ref a, b);
}

[HarmonyPatch(typeof(zc), "isd")]
sealed class OneShotCoinsGoldPatch
{
    static void Prefix(ref long a, EGoldCurrencySource b) => OneShotCoins.Try(ref a, b);
}

[HarmonyPatch(typeof(AccountStatus), "knx")]
sealed class AccountRewardPatch
{
    static void Postfix(EAccountStatus a, ref int __result)
    {
        switch (a)
        {
            case EAccountStatus.IncreaseExpAmount:
            case EAccountStatus.AdditionalExp:
            case EAccountStatus.AdditionalExpStageBoss:
            case EAccountStatus.AdditionalExpActBoss:
            case EAccountStatus.AdditionalExpNormalMonster:
            case EAccountStatus.OfflineRewardExpPercent:
                __result = int.MaxValue;
                break;
        }
    }
}

[HarmonyPatch(typeof(vd))]
sealed class HeroExpPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(vd)))
        {
            var p = method.GetParameters();
            if (method.ReturnType == typeof(void) && p.Length == 1 && p[0].ParameterType == typeof(float))
                yield return method;
        }
    }

    static void Prefix(ref float a)
    {
        a = CheatValues.MulExp(a);
    }
}

[HarmonyPatch(typeof(vb.ul), "jcg")]
sealed class MonsterExpGetterPatch
{
    static void Postfix(ref int __result)
    {
        __result = int.MaxValue;
    }
}

[HarmonyPatch(typeof(vb.ul), MethodType.Constructor, typeof(MonsterInfoData))]
sealed class MonsterRewardDataPatch
{
    static void Prefix(MonsterInfoData a)
    {
        if (a == null)
            return;
        a.RewardExp = int.MaxValue;
    }
}

[HarmonyPatch(typeof(StageManager))]
sealed class StageEnterChestSpacePatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(StageManager)))
        {
            if (method.ReturnType == typeof(EStageEnterResultType))
                yield return method;
        }
    }

    static void Postfix(ref EStageEnterResultType __result)
    {
        if (__result == EStageEnterResultType.FailReasonNeedChestSpace)
            __result = EStageEnterResultType.Success;
    }
}

[BepInPlugin("com.j3l1xd.taskbarhero", "J3L1XD", "1.0.23")]
[BepInProcess("TaskBarHero.exe")]
public sealed class J3L1XDPlugin : BasePlugin
{
    public override void Load()
    {
        var log = BepInEx.Logging.Logger.CreateLogSource("J3L1XD");
        try
        {
            new Harmony("com.j3l1xd.taskbarhero").PatchAll();
            log.LogInfo("J3L1XD 1.0.23 loaded — God Mode + One Hit Kill + Attack Speed + Move Speed + Stable Coins loaded.");
        }
        catch (Exception e)
        {
            log.LogError($"Harmony fail: {e}");
        }
    }
}
