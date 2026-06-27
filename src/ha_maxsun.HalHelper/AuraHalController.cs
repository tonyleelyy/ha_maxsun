using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using HaMaxsun.Core;

namespace HaMaxsun.HalHelper;

internal sealed class AuraHalController : IDisposable
{
    private readonly HalRuntimeOptions _options;
    private bool _disposed;

    public AuraHalController(HalRuntimeOptions options)
    {
        _options = options;
        PrepareNativeSearchPath();
    }

    public HalResponse Probe()
    {
        var device = FindDevice();
        try
        {
            return HalResponse.SuccessProbe(
                device.Name,
                _options.TargetHalGuid.ToString("D"),
                device.LedCount);
        }
        finally
        {
            device.Release();
        }
    }

    public HalResponse Apply(LightState state)
    {
        var effective = state.EffectiveColor;
        var device = FindDevice();
        try
        {
            ApplyStaticColor(device.Device, effective, device.LedCount);
            return HalResponse.SuccessApply(effective);
        }
        finally
        {
            device.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private AuraDeviceHandle FindDevice()
    {
        ThrowIfDisposed();

        var viaAuraSdk = TryFindDeviceViaAuraSdk();
        if (viaAuraSdk is not null)
        {
            return viaAuraSdk;
        }

        var viaDirectHal = TryFindDeviceViaDirectHal();
        if (viaDirectHal is not null)
        {
            return viaDirectHal;
        }

        Trace("Creating ASUS AuraDevelopement COM object.");
        dynamic aura = CreateAuraDevelopmentObject();
        try
        {
            Trace("Calling AURARequireToken.");
            TryCall(() => aura.AURARequireToken(0));

            var viaKnownGuid = TryFindDeviceViaKnownGuid(aura);
            if (viaKnownGuid is not null)
            {
                return viaKnownGuid;
            }

            var viaHalInfo = TryFindDeviceViaHalInfo(aura);
            if (viaHalInfo is not null)
            {
                return viaHalInfo;
            }

            throw new InvalidOperationException(
                $"Target HAL {_options.TargetHalGuid:D} was found in registry but did not enumerate '{_options.ExpectedDeviceName}'.");
        }
        finally
        {
            ReleaseComObject(aura);
        }
    }

    private AuraDeviceHandle? TryFindDeviceViaAuraSdk()
    {
        object? auraSdk = null;
        object? devices = null;
        try
        {
            Trace("Creating aura.sdk COM object.");
            auraSdk = CreateComObject("aura.sdk");
            Trace("Calling aura.sdk.SwitchMode().");
            ((dynamic)auraSdk).SwitchMode();
            Trace("Calling aura.sdk.Enumerate(0).");
            devices = ((dynamic)auraSdk).Enumerate(0);
            Trace($"aura.sdk returned device collection count={GetCount(devices)}.");
            var selected = SelectDevice(devices, auraSdk, releaseControlOnRelease: false);
            ReleaseComObject(devices);
            if (selected is null)
            {
                ReleaseComObject(auraSdk);
            }

            return selected;
        }
        catch (Exception ex)
        {
            Trace($"aura.sdk path failed: {ex.GetType().Name}: {ex.Message}");
            ReleaseComObject(devices);
            ReleaseComObject(auraSdk);
            return null;
        }
    }

    private AuraDeviceHandle? TryFindDeviceViaDirectHal()
    {
        object? hal = null;
        object? devices = null;
        try
        {
            Trace("Creating MaxSunEneLight.Hal COM object.");
            hal = CreateComObject("MaxSunEneLight.Hal");
            Trace("Calling MaxSunEneLight.Hal.EumerateDevices.");
            devices = ((dynamic)hal).EumerateDevices();
            Trace($"Direct Maxsun HAL returned device collection count={GetCount(devices)}.");
            Trace("Selecting device from direct Maxsun HAL.");
            var selected = SelectDevice(devices, hal);
            ReleaseComObject(devices);
            if (selected is null)
            {
                ReleaseComObject(hal);
            }

            return selected;
        }
        catch (Exception ex)
        {
            Trace($"Direct MaxSunEneLight.Hal path failed: {ex.GetType().Name}: {ex.Message}");
            ReleaseComObject(devices);
            ReleaseComObject(hal);
            return null;
        }
    }

    private AuraDeviceHandle? TryFindDeviceViaHalInfo(dynamic aura)
    {
        object? infos = null;
        try
        {
            Trace("Calling asus.aura.EumerateHalInfo.");
            infos = aura.EumerateHalInfo();
            var count = GetCount(infos);
            foreach (var index in CandidateIndexes(count))
            {
                object? info = null;
                object? hal = null;
                object? devices = null;
                try
                {
                    info = GetItem(infos, index);
                    if (info is null)
                    {
                        continue;
                    }

                    var guid = ReadGuidProperty(info, "Guid");
                    if (guid != _options.TargetHalGuid)
                    {
                        continue;
                    }

                    Trace("Calling IAuraHalInfo.CreateHal.");
                    hal = ((dynamic)info).CreateHal();
                    Trace("Calling IAuraHal.EumerateDevices.");
                    devices = ((dynamic)hal).EumerateDevices();
                    var selected = SelectDevice(devices, hal);
                    ReleaseComObject(devices);
                    ReleaseComObject(info);
                    if (selected is null)
                    {
                        ReleaseComObject(hal);
                    }

                    return selected;
                }
                catch
                {
                    ReleaseComObject(devices);
                    ReleaseComObject(hal);
                    ReleaseComObject(info);
                }
            }
        }
        catch
        {
            Trace("asus.aura.EumerateHalInfo path failed.");
            return null;
        }
        finally
        {
            ReleaseComObject(infos);
        }

        return null;
    }

    private AuraDeviceHandle? TryFindDeviceViaKnownGuid(dynamic aura)
    {
        foreach (var guidArgument in GuidArgumentVariants(_options.TargetHalGuid))
        {
            object? devices = null;
            try
            {
                Trace($"Calling EumerateDevicesFromHal with {guidArgument.GetType().Name}.");
                devices = ((dynamic)aura).EumerateDevicesFromHal(guidArgument, 1);
                var selected = SelectDevice(devices, owner: null);
                ReleaseComObject(devices);
                if (selected is not null)
                {
                    return selected;
                }
            }
            catch
            {
                ReleaseComObject(devices);
            }
        }

        return null;
    }

    private AuraDeviceHandle? SelectDevice(object? devices, object? owner, bool releaseControlOnRelease = true)
    {
        if (devices is null)
        {
            return null;
        }

        var count = GetCount(devices);
        Trace($"Selecting from device collection count={count}.");
        AuraDeviceHandle? fallback = null;

        foreach (var index in CandidateIndexes(count))
        {
            object? device = null;
            try
            {
                device = GetItem(devices, index);
                if (device is null)
                {
                    continue;
                }

                var name = ReadStringProperty(device, "Name") ?? string.Empty;
                var ledCount = ReadIntProperty(device, "LightCount");
                if (ledCount <= 0)
                {
                    ledCount = GetCount(((dynamic)device).Lights);
                }

                Trace($"Candidate device index={index}, name='{name}', ledCount={ledCount}.");

                var handle = new AuraDeviceHandle(device, owner, name, ledCount, releaseControlOnRelease);
                if (IsExpectedDevice(name, ledCount))
                {
                    fallback?.ReleaseDeviceOnly();
                    return handle;
                }

                fallback ??= handle;
                if (!ReferenceEquals(fallback.Device, device))
                {
                    ReleaseComObject(device);
                }
            }
            catch
            {
                ReleaseComObject(device);
            }
        }

        return fallback;
    }

    private void ApplyStaticColor(dynamic device, RgbColor color, int ledCount)
    {
        TryCall(() => device.SetMode(0));

        object? lights = null;
        try
        {
            lights = device.Lights;
            var count = GetCount(lights);
            if (count <= 0)
            {
                count = ledCount;
            }

            foreach (var index in CandidateIndexes(count))
            {
                object? light = null;
                try
                {
                    light = GetItem(lights, index);
                    if (light is null)
                    {
                        continue;
                    }

                    dynamic led = light;
                    led.Red = color.Red;
                    led.Green = color.Green;
                    led.Blue = color.Blue;
                }
                finally
                {
                    ReleaseComObject(light);
                }
            }

            device.Apply();
        }
        finally
        {
            ReleaseComObject(lights);
        }
    }

    private bool IsExpectedDevice(string name, int ledCount)
    {
        if (name.Contains(_options.ExpectedDeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ledCount == _options.ExpectedLedCount;
    }

    private void PrepareNativeSearchPath()
    {
        var paths = new[]
        {
            _options.AuraSdkDirectory,
            _options.MaxsunHalDirectory,
            _options.EneHalDirectory
        }.Where(Directory.Exists).ToArray();

        if (paths.Length == 0)
        {
            return;
        }

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable("PATH", string.Join(';', paths) + ";" + currentPath);
        Directory.SetCurrentDirectory(paths[1 < paths.Length ? 1 : 0]);
    }

    private static object CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)
            ?? throw new InvalidOperationException($"COM ProgID '{progId}' is not registered.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"COM ProgID '{progId}' returned null.");
    }

    private static object CreateAuraDevelopmentObject()
    {
        var errors = new List<string>();
        foreach (var progId in new[] { "asus.aura", "asus.aura.1" })
        {
            try
            {
                return CreateComObject(progId);
            }
            catch (Exception ex)
            {
                errors.Add($"{progId}: {ex.Message}");
            }
        }

        var auraDevelopmentClsid = Guid.Parse("34B707DC-1133-4EBC-B380-21387A50A89D");
        try
        {
            var type = Type.GetTypeFromCLSID(auraDevelopmentClsid, throwOnError: true)
                ?? throw new InvalidOperationException("CLSID lookup returned null.");
            return Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("CLSID activation returned null.");
        }
        catch (Exception ex)
        {
            errors.Add($"{auraDevelopmentClsid:B}: {ex.Message}");
        }

        throw new InvalidOperationException(
            "Could not activate ASUS AuraDevelopement COM object. Tried " + string.Join("; ", errors));
    }

    private static IEnumerable<object> GuidArgumentVariants(Guid guid)
    {
        yield return new[] { guid };
        yield return new object[] { guid };
        yield return new[] { guid.ToString("B") };
        yield return new object[] { guid.ToString("B") };
    }

    private static IEnumerable<int> CandidateIndexes(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return i;
        }

        for (var i = 1; i <= count; i++)
        {
            yield return i;
        }
    }

    private static object? GetItem(object collection, int index)
    {
        try
        {
            return ((dynamic)collection).Item(index);
        }
        catch
        {
            try
            {
                return ((dynamic)collection)[index];
            }
            catch
            {
                return null;
            }
        }
    }

    private static int GetCount(object? collection)
    {
        if (collection is null)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(((dynamic)collection).Count);
        }
        catch
        {
            return 0;
        }
    }

    private static Guid? ReadGuidProperty(object value, string propertyName)
    {
        try
        {
            var property = ReadProperty(value, propertyName);
            if (property is Guid guid)
            {
                return guid;
            }

            return Guid.TryParse(Convert.ToString(property), out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadStringProperty(object value, string propertyName)
    {
        try
        {
            return Convert.ToString(ReadProperty(value, propertyName));
        }
        catch
        {
            return null;
        }
    }

    private static int ReadIntProperty(object value, string propertyName)
    {
        try
        {
            return Convert.ToInt32(ReadProperty(value, propertyName));
        }
        catch
        {
            return 0;
        }
    }

    private static object? ReadProperty(object value, string propertyName)
    {
        dynamic item = value;
        return propertyName switch
        {
            "Guid" => item.Guid,
            "Name" => item.Name,
            "LightCount" => item.LightCount,
            _ => null
        };
    }

    private static void TryCall(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or RuntimeBinderException)
        {
            Debug.WriteLine(ex);
        }
    }

    private static void Trace(string message)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MAXSUN_HAL_TRACE"), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"[hal-trace] {message}");
            Console.Error.Flush();
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
            // Nothing useful can be done during COM cleanup in a short-lived helper.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class AuraDeviceHandle
    {
        public AuraDeviceHandle(object device, object? owner, string name, int ledCount, bool releaseControlOnRelease)
        {
            Device = device;
            Owner = owner;
            Name = name;
            LedCount = ledCount;
            ReleaseControlOnRelease = releaseControlOnRelease;
        }

        public object Device { get; }
        public object? Owner { get; }
        public string Name { get; }
        public int LedCount { get; }
        public bool ReleaseControlOnRelease { get; }

        public void Release()
        {
            if (ReleaseControlOnRelease && Owner is not null)
            {
                TryCall(() => ((dynamic)Owner).ReleaseControl(0));
            }

            ReleaseComObject(Device);
            ReleaseComObject(Owner);
        }

        public void ReleaseDeviceOnly()
        {
            ReleaseComObject(Device);
        }
    }
}

