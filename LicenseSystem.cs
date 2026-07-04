using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace J3L1XD.Licensing;

internal static class HWID
{
    private static string _cached;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Get()
    {
        if (_cached != null) return _cached;

        var sb = new StringBuilder();

        try
        {
            sb.Append(GetMachineGuid());
            sb.Append("|");
            sb.Append(Environment.ProcessorCount);
            sb.Append("|");
            sb.Append(Environment.MachineName);
        }
        catch
        {
            sb.Append(Environment.MachineName);
            sb.Append("|");
            sb.Append(Environment.UserName);
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        _cached = BitConverter.ToString(hash).Replace("-", "").Substring(0, 32);
        return _cached;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string GetMachineGuid()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString() ?? "UNKNOWN";
        }
        catch
        {
            return "UNKNOWN";
        }
    }
}

internal static class AntiTamper
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsDebuggerAttached()
    {
        try
        {
            return Debugger.IsAttached;
        }
        catch
        {
            return false;
        }
    }
}

internal class LicenseValidator
{
    private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    private static string _serverUrl = "http://localhost:8000";
    private static readonly string _licenseFile = Path.Combine(
        Paths.BepInExRootPath, "config", "J3L1XD.license");
    private static ManualLogSource _log;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Init(ManualLogSource log)
    {
        _log = log;
        try
        {
            var configPath = Path.Combine(Paths.BepInExRootPath, "config", "J3L1XD.cfg");
            if (File.Exists(configPath))
            {
                var lines = File.ReadAllLines(configPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("ServerUrl="))
                    {
                        _serverUrl = line.Substring("ServerUrl=".Length).Trim();
                        break;
                    }
                }
            }
        }
        catch { }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<bool> Validate()
    {
        if (AntiTamper.IsDebuggerAttached())
        {
            _log?.LogError("Debugger detected - license validation blocked");
            return false;
        }

        var licenseKey = LoadLicenseKey();
        if (string.IsNullOrEmpty(licenseKey))
        {
            _log?.LogError("No license key found. Create J3L1XD.license in BepInEx/config/ with your key.");
            return false;
        }

        var hwid = HWID.Get();

        try
        {
            var json = $"{{\"license_key\":\"{licenseKey}\",\"hwid\":\"{hwid}\"}}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_serverUrl}/api/validate", content);

            if (!response.IsSuccessStatusCode)
            {
                _log?.LogError($"License server error: {response.StatusCode}");
                return false;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var valid = responseJson.Contains("\"valid\":true") || responseJson.Contains("\"valid\": true");

            if (valid)
            {
                _log?.LogInfo("License validated successfully");
                return true;
            }
            else
            {
                _log?.LogError("License invalid");
                return false;
            }
        }
        catch (HttpRequestException)
        {
            _log?.LogError("Cannot connect to license server. Check your internet connection.");
            return false;
        }
        catch (Exception ex)
        {
            _log?.LogError($"License validation error: {ex.Message}");
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string LoadLicenseKey()
    {
        try
        {
            if (File.Exists(_licenseFile))
                return File.ReadAllText(_licenseFile).Trim();
        }
        catch { }
        return null;
    }
}
