using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.App;

public static class ElevationIpc
{
    private const int MaxResponseBytes = 1024 * 1024;
    private const int ErrorCancelled = 1223;

    public sealed record ElevatedResult(bool Success, string Title, string[] Lines);
    private sealed record PipeEnvelope(string Token, ElevatedResult Result);

    public static ElevatedResult RunElevatedAndWait(
        string executablePath,
        string[] elevatedArguments,
        TimeSpan timeout)
        => RunElevatedAndWaitAsync(executablePath, elevatedArguments, timeout)
            .GetAwaiter()
            .GetResult();

    public static int RunAsElevatedChildAndReply(
        string pipeName,
        string token,
        Func<ElevatedResult> action)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(10_000);

            ElevatedResult result;
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                result = new ElevatedResult(
                    false,
                    Text.Get("elevation.operationFailed"),
                    [Text.Get("elevation.unhandledException"), ex.ToString()]);
            }

            WriteFrame(client, new PipeEnvelope(token, result));
            return result.Success ? 0 : 1;
        }
        catch
        {
            // The parent can report a timeout or a broken IPC channel.
            return 1;
        }
    }

    private static async Task<ElevatedResult> RunElevatedAndWaitAsync(
        string executablePath,
        IEnumerable<string> elevatedArguments,
        TimeSpan timeout)
    {
        var pipeName = $"bthftoggle_{Guid.NewGuid():N}";
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        using var pipeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            Verb = "runas"
        };

        foreach (var argument in elevatedArguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add(token);

        Process? child;
        try
        {
            child = Process.Start(startInfo);
            if (child is null)
            {
                return Failure(
                    Text.Get("elevation.failed"),
                    Text.Get("elevation.windowsDidNotStart"));
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return Failure(
                Text.Get("elevation.cancelled"),
                Text.Get("elevation.permissionDenied"),
                Text.Get("cli.noChanges"));
        }
        catch (Exception ex)
        {
            return Failure(
                Text.Get("elevation.failed"),
                ex.Message,
                Text.Get("cli.noChanges"));
        }

        using (child)
        using (var cancellation = new CancellationTokenSource(timeout))
        {
            try
            {
                var connectionTask = pipeServer.WaitForConnectionAsync(cancellation.Token);
                var exitTask = child.WaitForExitAsync(cancellation.Token);
                var firstCompleted = await Task.WhenAny(connectionTask, exitTask).ConfigureAwait(false);

                if (firstCompleted == exitTask && !pipeServer.IsConnected)
                {
                    await exitTask.ConfigureAwait(false);
                    return Failure(
                        Text.Get("elevation.processExited"),
                        Text.Format("elevation.processExitedDetail", child.ExitCode));
                }

                await connectionTask.ConfigureAwait(false);
                var envelope = await ReadFrameAsync(pipeServer, cancellation.Token).ConfigureAwait(false);

                if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(envelope.Token),
                        Convert.FromHexString(token)))
                {
                    return Failure(
                        Text.Get("elevation.invalidResponse"),
                        Text.Get("elevation.tokenMismatch"));
                }

                return envelope.Result;
            }
            catch (OperationCanceledException)
            {
                return Failure(
                    Text.Get("elevation.timeout"),
                    Text.Format("elevation.timeoutDetail", timeout.TotalSeconds),
                    Text.Get("elevation.timeoutHint"));
            }
            catch (Exception ex)
            {
                return Failure(Text.Get("elevation.ipcFailed"), ex.Message);
            }
        }
    }

    private static void WriteFrame(Stream stream, PipeEnvelope envelope)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        if (payload.Length > MaxResponseBytes)
            throw new InvalidDataException(Text.Get("elevation.responseTooLarge"));

        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        stream.Write(length);
        stream.Write(payload);
        stream.Flush();
    }

    private static async Task<PipeEnvelope> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        if (length is <= 0 or > MaxResponseBytes)
        {
            throw new InvalidDataException(
                Text.Format("elevation.invalidResponseLength", length));
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<PipeEnvelope>(payload)
               ?? throw new InvalidDataException(Text.Get("elevation.emptyResponse"));
    }

    private static ElevatedResult Failure(string title, params string[] lines)
        => new(false, title, lines);
}
