using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HaMaxsun.Service;

internal sealed class BridgeWindowsService
{
    private const string ServiceName = "ha_maxsun";
    private const int ServiceWin32OwnProcess = 0x00000010;
    private const int ServiceStopped = 0x00000001;
    private const int ServiceStartPending = 0x00000002;
    private const int ServiceStopPending = 0x00000003;
    private const int ServiceRunning = 0x00000004;
    private const int ServiceAcceptStop = 0x00000001;
    private const int ServiceAcceptShutdown = 0x00000004;
    private const int ServiceControlStop = 0x00000001;
    private const int ServiceControlInterrogate = 0x00000004;
    private const int ServiceControlShutdown = 0x00000005;

    private readonly BridgeOptions _options;
    private readonly BridgeLogger _logger;
    private readonly ManualResetEventSlim _stopped = new(false);
    private readonly ServiceMainDelegate _serviceMain;
    private readonly HandlerExDelegate _handler;
    private CancellationTokenSource? _cts;
    private Task? _runner;
    private IntPtr _statusHandle;

    public BridgeWindowsService(BridgeOptions options, BridgeLogger logger)
    {
        _options = options;
        _logger = logger;
        _serviceMain = ServiceMain;
        _handler = HandlerEx;
    }

    public int Run()
    {
        var serviceTable = new[]
        {
            new ServiceTableEntry
            {
                ServiceName = ServiceName,
                ServiceMain = _serviceMain
            },
            new ServiceTableEntry()
        };

        if (StartServiceCtrlDispatcher(serviceTable))
        {
            return 0;
        }

        var error = Marshal.GetLastWin32Error();
        _logger.Error(new Win32Exception(error), "Failed to connect to Windows Service Control Manager");
        return error == 0 ? 1 : error;
    }

    private void ServiceMain(int argc, IntPtr argv)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, _handler, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero)
        {
            _logger.Error(new Win32Exception(Marshal.GetLastWin32Error()), "Failed to register Windows service handler");
            return;
        }

        SetStatus(ServiceStartPending, 0, waitHintMilliseconds: 30000);
        _logger.Info("Windows service starting.");

        _cts = new CancellationTokenSource();
        _runner = Task.Run(() => RunBridgeAsync(_cts.Token));

        SetStatus(ServiceRunning, ServiceAcceptStop | ServiceAcceptShutdown);
        _stopped.Wait();
    }

    private async Task RunBridgeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await new BridgeRunner(_options, _logger).RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Windows service bridge runner stopped unexpectedly");
        }
        finally
        {
            SetStatus(ServiceStopped, 0);
            _stopped.Set();
        }
    }

    private int HandlerEx(int control, int eventType, IntPtr eventData, IntPtr context)
    {
        switch (control)
        {
            case ServiceControlStop:
            case ServiceControlShutdown:
                _logger.Info("Windows service stopping.");
                SetStatus(ServiceStopPending, 0, waitHintMilliseconds: 15000);
                _cts?.Cancel();
                return 0;
            case ServiceControlInterrogate:
                return 0;
            default:
                return 0;
        }
    }

    private void SetStatus(int currentState, int controlsAccepted, int waitHintMilliseconds = 0)
    {
        if (_statusHandle == IntPtr.Zero)
        {
            return;
        }

        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = currentState,
            ControlsAccepted = controlsAccepted,
            Win32ExitCode = 0,
            ServiceSpecificExitCode = 0,
            CheckPoint = currentState is ServiceStartPending or ServiceStopPending ? 1 : 0,
            WaitHint = waitHintMilliseconds
        };

        if (!SetServiceStatus(_statusHandle, ref status))
        {
            _logger.Error(new Win32Exception(Marshal.GetLastWin32Error()), "Failed to update Windows service status");
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceStartTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(
        string serviceName,
        HandlerExDelegate handler,
        IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr serviceStatusHandle, ref ServiceStatus serviceStatus);

    private delegate void ServiceMainDelegate(int argc, IntPtr argv);

    private delegate int HandlerExDelegate(int control, int eventType, IntPtr eventData, IntPtr context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ServiceName;
        public ServiceMainDelegate? ServiceMain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
    }
}

