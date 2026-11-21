using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.InteropServices;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class LifecycleHookContainment : IDisposable
{
    private const int JobObjectBasicAccountingInformation = 1;
    private const int JobExitCode = 1;
    private const int MacDescendantSettleMilliseconds = 25;
    private const int SupervisorProcessCount = 1;

    private readonly IFileSystem _fileSystem;
    private readonly LifecycleHookJobHandle? _jobHandle;
    private readonly ConcurrentDictionary<int, byte> _macProcessIds = new();
    private readonly ConcurrentDictionary<int, byte> _macProcessGroupIds = new();

    private LifecycleHookContainment(IFileSystem fileSystem, LifecycleHookJobHandle? jobHandle)
    {
        _fileSystem = fileSystem;
        _jobHandle = jobHandle;
    }

    public static LifecycleHookContainment Create(IFileSystem fileSystem)
    {
        if (OperatingSystem.IsWindows())
        {
            return new LifecycleHookContainment(fileSystem, CreateWindowsJob());
        }

        if (LifecycleHookUnixNativeMethods.SetSessionId() < 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to isolate lifecycle hook process group."
            );
        }

        if (
            OperatingSystem.IsLinux()
            && LifecycleHookUnixNativeMethods.ProcessControl(
                LifecycleHookUnixNativeMethods.ChildSubreaperOption,
                1,
                0,
                0,
                0
            ) < 0
        )
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to retain orphaned lifecycle hook processes."
            );
        }

        return new LifecycleHookContainment(fileSystem, null);
    }

    public async Task<bool> HasActiveDescendantsAsync(CancellationToken cancellationToken)
    {
        if (_jobHandle is not null)
        {
            return HasWindowsJobDescendants(_jobHandle);
        }

        if (OperatingSystem.IsLinux())
        {
            ReapLinuxChildren();
            return HasLinuxDescendants();
        }

        return await HasMacDescendantsAsync(cancellationToken);
    }

    public Task TrackMacDescendantsAsync(int rootProcessId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return TrackMacProcessesAsync(rootProcessId, cancellationToken);
    }

    public static void Kill(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (KillUnixProcess(-process.Id))
            {
                return;
            }
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    public async Task AbortAsync()
    {
        if (_jobHandle is not null)
        {
            if (
                !LifecycleHookWindowsNativeMethods.TerminateJobObject(
                    _jobHandle.DangerousGetHandle(),
                    JobExitCode
                )
            )
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Unable to terminate lifecycle hook Windows job."
                );
            }

            return;
        }

        if (OperatingSystem.IsLinux())
        {
            KillLinuxDescendantGroups(_fileSystem);
        }

        if (OperatingSystem.IsMacOS())
        {
            await KillMacDescendantsAsync();
        }

        var result = LifecycleHookUnixNativeMethods.Kill(
            -Environment.ProcessId,
            LifecycleHookUnixNativeMethods.SignalKill
        );
        if (result < 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to terminate lifecycle hook process group."
            );
        }
    }

    public void Dispose()
    {
        _jobHandle?.Dispose();
    }

    private static LifecycleHookJobHandle CreateWindowsJob()
    {
        var handle = LifecycleHookWindowsNativeMethods.CreateJobObject(nint.Zero, null);
        var jobHandle = new LifecycleHookJobHandle(handle);
        if (jobHandle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to create lifecycle hook job object."
            );
        }

        if (
            !LifecycleHookWindowsNativeMethods.AssignProcessToJobObject(
                jobHandle.DangerousGetHandle(),
                LifecycleHookWindowsNativeMethods.GetCurrentProcess()
            )
        )
        {
            var error = Marshal.GetLastWin32Error();
            jobHandle.Dispose();
            throw new Win32Exception(
                error,
                $"Unable to constrain lifecycle hook processes in a Windows job (error {error})."
            );
        }

        return jobHandle;
    }

    private static bool HasWindowsJobDescendants(LifecycleHookJobHandle jobHandle)
    {
        var accounting = default(LifecycleHookJobAccounting);
        var succeeded = LifecycleHookWindowsNativeMethods.QueryInformationJobObject(
            jobHandle.DangerousGetHandle(),
            JobObjectBasicAccountingInformation,
            ref accounting,
            (uint)Marshal.SizeOf<LifecycleHookJobAccounting>(),
            nint.Zero
        );
        if (!succeeded)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to inspect lifecycle hook Windows job."
            );
        }

        return accounting.ActiveProcesses > SupervisorProcessCount;
    }

    private bool HasLinuxDescendants()
    {
        var (parents, _) = ReadLinuxProcessRelationships(_fileSystem);
        foreach (var processId in parents.Keys)
        {
            if (IsDescendantOfSupervisor(processId, parents))
            {
                return true;
            }
        }

        return false;
    }

    private static void KillLinuxDescendantGroups(IFileSystem fileSystem)
    {
        var (parents, groups) = ReadLinuxProcessRelationships(fileSystem);
        var descendantGroups = groups
            .Where(pair => IsDescendantOfSupervisor(pair.Key, parents))
            .Select(pair => pair.Value)
            .Where(groupId => groupId != Environment.ProcessId)
            .Distinct()
            .ToArray();
        foreach (var groupId in descendantGroups)
        {
            _ = KillUnixProcess(-groupId);
        }
    }

    private static (
        Dictionary<int, int> Parents,
        Dictionary<int, int> Groups
    ) ReadLinuxProcessRelationships(IFileSystem fileSystem)
    {
        var parents = new Dictionary<int, int>();
        var groups = new Dictionary<int, int>();
        foreach (var processDirectory in fileSystem.Directory.EnumerateDirectories("/proc"))
        {
            var processIdText = fileSystem.Path.GetFileName(processDirectory);
            if (
                !int.TryParse(
                    processIdText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var processId
                )
                || processId == Environment.ProcessId
            )
            {
                continue;
            }

            try
            {
                var stat = fileSystem.File.ReadAllText(
                    fileSystem.Path.Combine(processDirectory, "stat")
                );
                var commandEnd = stat.LastIndexOf(')');
                if (commandEnd < 0)
                {
                    continue;
                }

                var fields = stat[(commandEnd + 2)..].Split(' ');
                if (
                    fields.Length > 2
                    && int.TryParse(
                        fields[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parentProcessId
                    )
                    && int.TryParse(
                        fields[2],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var processGroupId
                    )
                )
                {
                    parents[processId] = parentProcessId;
                    groups[processId] = processGroupId;
                }
            }
            catch (IOException)
            {
                // A process can exit while /proc is being inspected.
            }
            catch (UnauthorizedAccessException)
            {
                // A process can disappear or become inaccessible during inspection.
            }
        }

        return (parents, groups);
    }

    private static void ReapLinuxChildren()
    {
        while (LifecycleHookUnixNativeMethods.WaitProcess(-1, out _, 1) > 0) { }
    }

    private static bool IsDescendantOfSupervisor(int processId, Dictionary<int, int> parents)
    {
        var visited = new HashSet<int>();
        var currentProcessId = processId;
        while (
            visited.Add(currentProcessId)
            && parents.TryGetValue(currentProcessId, out var parentProcessId)
        )
        {
            if (parentProcessId == Environment.ProcessId)
            {
                return true;
            }

            currentProcessId = parentProcessId;
        }

        return false;
    }

    private async Task<bool> HasMacDescendantsAsync(CancellationToken cancellationToken)
    {
        await RefreshMacDescendantsAsync(rootProcessId: null, cancellationToken);
        if (_macProcessIds.IsEmpty)
        {
            await Task.Delay(MacDescendantSettleMilliseconds, cancellationToken);
            await RefreshMacDescendantsAsync(rootProcessId: null, cancellationToken);
        }

        return !_macProcessIds.IsEmpty;
    }

    private async Task TrackMacProcessesAsync(
        int rootProcessId,
        CancellationToken cancellationToken
    )
    {
        await RefreshMacDescendantsAsync(rootProcessId, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(MacDescendantSettleMilliseconds, cancellationToken);
            await RefreshMacDescendantsAsync(rootProcessId: null, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task RefreshMacDescendantsAsync(
        int? rootProcessId,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("/bin/ps")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("-axo");
        process.StartInfo.ArgumentList.Add("pid=,ppid=,pgid=");
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var processRows =
            new Dictionary<int, (int ProcessId, int ParentProcessId, int ProcessGroupId)>();
        foreach (
            var row in (await outputTask).Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (ParseMacProcessRow(row) is { } processRow)
            {
                processRows[processRow.ProcessId] = processRow;
            }
        }
        var activeProcessIds = new HashSet<int>(processRows.Keys);
        foreach (var processId in _macProcessIds.Keys)
        {
            if (!activeProcessIds.Contains(processId))
            {
                _macProcessIds.TryRemove(processId, out _);
            }
        }

        if (rootProcessId is not null)
        {
            _macProcessIds.TryAdd(rootProcessId.Value, 0);
        }

        foreach (var processRow in processRows.Values)
        {
            if (
                processRow.ProcessId == process.Id
                || processRow.ProcessId == Environment.ProcessId
                || !IsMacDescendant(processRow, processRows)
            )
            {
                continue;
            }

            _macProcessIds.TryAdd(processRow.ProcessId, 0);
            if (processRow.ProcessGroupId != Environment.ProcessId)
            {
                _macProcessGroupIds.TryAdd(processRow.ProcessGroupId, 0);
            }
        }

        var activeGroups = new HashSet<int>(
            _macProcessIds.Keys.Select(processId =>
                processRows.TryGetValue(processId, out var row) ? row.ProcessGroupId : 0
            )
        );
        foreach (var groupId in _macProcessGroupIds.Keys)
        {
            if (!activeGroups.Contains(groupId))
            {
                _macProcessGroupIds.TryRemove(groupId, out _);
            }
        }
    }

    internal static (int ProcessId, int ParentProcessId, int ProcessGroupId)? ParseMacProcessRow(
        string row
    )
    {
        var fields = row.Split(
            [' ', '\t', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (
            fields.Length < 3
            || !int.TryParse(
                fields[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var processId
            )
            || !int.TryParse(
                fields[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parentProcessId
            )
            || !int.TryParse(
                fields[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var processGroupId
            )
        )
        {
            return null;
        }

        return (processId, parentProcessId, processGroupId);
    }

    private bool IsMacDescendant(
        (int ProcessId, int ParentProcessId, int ProcessGroupId) row,
        Dictionary<int, (int ProcessId, int ParentProcessId, int ProcessGroupId)> rows
    )
    {
        if (row.ProcessGroupId == Environment.ProcessId)
        {
            return true;
        }

        var parentProcessId = row.ParentProcessId;
        var visited = new HashSet<int>();
        while (visited.Add(parentProcessId) && rows.TryGetValue(parentProcessId, out var parent))
        {
            if (
                parentProcessId == Environment.ProcessId
                || _macProcessIds.ContainsKey(parentProcessId)
            )
            {
                return true;
            }

            parentProcessId = parent.ParentProcessId;
        }

        return false;
    }

    private async Task KillMacDescendantsAsync()
    {
        await RefreshMacDescendantsAsync(rootProcessId: null, CancellationToken.None);
        foreach (var groupId in _macProcessGroupIds.Keys)
        {
            _ = KillUnixProcess(-groupId);
        }

        foreach (var processId in _macProcessIds.Keys)
        {
            if (processId != Environment.ProcessId)
            {
                _ = KillUnixProcess(processId);
            }
        }
    }

    private static bool KillUnixProcess(int processIdOrGroup)
    {
        var result = LifecycleHookUnixNativeMethods.Kill(
            processIdOrGroup,
            LifecycleHookUnixNativeMethods.SignalKill
        );
        if (result == 0)
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == LifecycleHookUnixNativeMethods.NoSuchProcess)
        {
            return false;
        }

        throw new Win32Exception(
            error,
            $"Unable to terminate lifecycle hook process {processIdOrGroup}."
        );
    }
}
