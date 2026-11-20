using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Lunapack.Cli.Packs.Lifecycle;

internal static class LifecycleHookMacProcess
{
    private const int SpawnAttributeBufferSize = 1024;
    private const int ContinueSignal = 19;
    private const int Interrupted = 4;

    public static int StartSuspended(string executable, IReadOnlyList<string> arguments)
    {
        var attributes = Marshal.AllocHGlobal(SpawnAttributeBufferSize);
        var attributesInitialized = false;
        try
        {
            var initializationError = LifecycleHookMacNativeMethods.InitializeSpawnAttributes(
                attributes
            );
            ThrowOnFailure(initializationError, "initialize suspended lifecycle hook launch");
            attributesInitialized = true;
            ThrowOnFailure(
                LifecycleHookMacNativeMethods.SetSpawnFlags(
                    attributes,
                    LifecycleHookMacNativeMethods.StartSuspended
                ),
                "configure suspended lifecycle hook launch"
            );

            var argumentList = new string?[arguments.Count + 2];
            argumentList[0] = executable;
            for (var index = 0; index < arguments.Count; index++)
            {
                argumentList[index + 1] = arguments[index];
            }

            var environmentValues = Environment
                .GetEnvironmentVariables()
                .Cast<DictionaryEntry>()
                .Select(entry => $"{entry.Key}={entry.Value}")
                .ToArray();
            var environment = new string?[environmentValues.Length + 1];
            environmentValues.CopyTo(environment, 0);
            var nativeExecutable = Marshal.StringToCoTaskMemUTF8(executable);
            var nativeArguments = AllocateStringArray(argumentList, out var argumentStrings);
            var nativeEnvironment = AllocateStringArray(environment, out var environmentStrings);
            try
            {
                var spawnError = LifecycleHookMacNativeMethods.Spawn(
                    out var processId,
                    nativeExecutable,
                    nint.Zero,
                    attributes,
                    nativeArguments,
                    nativeEnvironment
                );
                ThrowOnFailure(spawnError, $"start lifecycle hook executable '{executable}'");
                return processId;
            }
            finally
            {
                FreeStringArray(nativeEnvironment, environmentStrings);
                FreeStringArray(nativeArguments, argumentStrings);
                Marshal.FreeCoTaskMem(nativeExecutable);
            }
        }
        finally
        {
            if (attributesInitialized)
            {
                _ = LifecycleHookMacNativeMethods.DestroySpawnAttributes(attributes);
            }

            Marshal.FreeHGlobal(attributes);
        }
    }

    public static void Resume(int processId)
    {
        if (LifecycleHookUnixNativeMethods.Kill(processId, ContinueSignal) != 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to resume suspended lifecycle hook."
            );
        }
    }

    public static Task<int> WaitForExitAsync(int processId) =>
        Task.Run(() =>
        {
            while (true)
            {
                var result = LifecycleHookUnixNativeMethods.WaitProcess(
                    processId,
                    out var status,
                    0
                );
                if (result == processId)
                {
                    return DecodeExitCode(status);
                }

                var error = Marshal.GetLastWin32Error();
                if (error != Interrupted)
                {
                    throw new Win32Exception(error, "Unable to wait for lifecycle hook process.");
                }
            }
        });

    private static int DecodeExitCode(int status)
    {
        var signal = status & 0x7f;
        return signal == 0 ? (status >> 8) & 0xff : 128 + signal;
    }

    private static void ThrowOnFailure(int error, string operation)
    {
        if (error != 0)
        {
            throw new Win32Exception(error, $"Unable to {operation}.");
        }
    }

    private static nint AllocateStringArray(string?[] values, out nint[] allocatedStrings)
    {
        allocatedStrings = new nint[values.Length];
        var array = Marshal.AllocHGlobal(values.Length * IntPtr.Size);
        try
        {
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                var nativeValue = value is null ? nint.Zero : Marshal.StringToCoTaskMemUTF8(value);
                allocatedStrings[index] = nativeValue;
                Marshal.WriteIntPtr(array, index * IntPtr.Size, nativeValue);
            }

            return array;
        }
        catch
        {
            FreeStringArray(array, allocatedStrings);
            throw;
        }
    }

    private static void FreeStringArray(nint array, IReadOnlyList<nint> allocatedStrings)
    {
        foreach (var value in allocatedStrings)
        {
            if (value != nint.Zero)
            {
                Marshal.FreeCoTaskMem(value);
            }
        }

        if (array != nint.Zero)
        {
            Marshal.FreeHGlobal(array);
        }
    }
}
