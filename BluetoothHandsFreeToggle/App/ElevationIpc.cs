using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace BluetoothHandsFreeToggle.App;

public static class ElevationIpc
{
    public sealed record ElevatedResult(bool Success, string Title, string[] Lines);

    public static ElevatedResult RunElevatedAndWait(string exePath, string[] elevatedArgs, TimeSpan timeout)
    {
        // Parent process: create a pipe, start elevated child, read result.
        var pipeName = "bthftoggle_" + Guid.NewGuid().ToString("N");

        using var pipeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var args = BuildArgsWithPipe(elevatedArgs, pipeName);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC
            return new ElevatedResult(false, "Elevation cancelled", new[]
            {
                "Administrator permission was not granted.",
                "No changes were made."
            });
        }

        var connected = pipeServer.WaitForConnectionAsync().Wait(timeout);
        if (!connected)
        {
            return new ElevatedResult(false, "Timeout", new[]
            {
                "The elevated process did not respond in time.",
                "No confirmation was received."
            });
        }

        using var ms = new MemoryStream();
        pipeServer.CopyTo(ms);
        var json = Encoding.UTF8.GetString(ms.ToArray()).Trim();

        try
        {
            var result = JsonSerializer.Deserialize<ElevatedResult>(json);
            return result ?? new ElevatedResult(false, "Unknown result", new[] { "The elevated process returned empty response." });
        }
        catch
        {
            return new ElevatedResult(false, "Invalid response", new[]
            {
                "The elevated process returned an unreadable response.",
                "Raw:",
                json.Length > 500 ? json[..500] + "..." : json
            });
        }
    }

    public static int RunAsElevatedChildAndReply(string pipeName, Func<ElevatedResult> action)
    {
        ElevatedResult result;
        try
        {
            result = action();
        }
        catch (Exception ex)
        {
            result = new ElevatedResult(false, "Error", new[]
            {
                "Unhandled exception in elevated process:",
                ex.ToString()
            });
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(10_000);

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
            var bytes = Encoding.UTF8.GetBytes(json);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
        }
        catch
        {
            // If IPC fails, we still return a meaningful exit code.
        }

        return result.Success ? 0 : 1;
    }

    private static string BuildArgsWithPipe(string[] elevatedArgs, string pipeName)
    {
        // add: --elevated --pipe "<pipename>"
        var all = new List<string>();
        all.AddRange(elevatedArgs);
        all.Add("--pipe");
        all.Add(pipeName);

        return string.Join(" ", all.Select(QuoteIfNeeded));
    }

    private static string QuoteIfNeeded(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "\"\"";

        if (s.Any(char.IsWhiteSpace) || s.Contains('"'))
            return "\"" + s.Replace("\"", "\\\"") + "\"";

        return s;
    }
}
