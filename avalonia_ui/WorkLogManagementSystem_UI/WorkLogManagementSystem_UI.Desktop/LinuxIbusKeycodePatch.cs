using System;
using System.Reflection;
using HarmonyLib;

namespace WorkLogManagementSystem_UI.Desktop;

internal static class LinuxIbusKeycodePatch
{
    private const string IBusTypeName = "Avalonia.FreeDesktop.DBusIme.IBus.IBusX11TextInputMethod, Avalonia.FreeDesktop";
    private const string HarmonyId = "WorkLogManagementSystem_UI.LinuxIbusKeycodePatch";

    public static void Apply()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Type? ibusType = Type.GetType(IBusTypeName, throwOnError: false);
        MethodInfo? target = ibusType?.GetMethod("HandleKeyCore", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? prefix = typeof(LinuxIbusKeycodePatch).GetMethod(nameof(FixIbusKeyCode), BindingFlags.Static | BindingFlags.NonPublic);

        if (target is null || prefix is null)
        {
            Console.Error.WriteLine("Korean IBus keycode patch skipped: Avalonia IBus method not found.");
            return;
        }

        new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
        Console.Error.WriteLine("Korean IBus keycode patch applied.");
    }

    private static void FixIbusKeyCode([HarmonyArgument("keyCode")] ref int keyCode)
    {
        if (keyCode >= 8)
        {
            keyCode -= 8;
        }
    }
}
