using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PadDim;

public sealed record UpdateRelease(Version Version, string Url, string Digest, long Size, string Changes);
public sealed class AppUpdate
{
    private static readonly HttpClient http = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PadDim-Updater/1.0");
        return client;
    }
    public static UpdateRelease? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) || version.Build < 0 || version.Revision >= 0) return null;
        if (version <= new Version(current.Major, current.Minor, Math.Max(current.Build, 0))) return null;
        string name = $"PadDim-Setup-{version}-win-x64.exe";
        string url = $"https://github.com/marinyan/PadDim/releases/download/{tag}/{name}";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name || asset.GetProperty("browser_download_url").GetString() != url) continue;
            string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
            long size = asset.GetProperty("size").GetInt64();
            if (digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal) || !digest[7..].All(Uri.IsHexDigit) || size <= 0 || size > 300_000_000)
                throw new InvalidDataException("更新ファイルの検証情報が不正です");
            string body = root.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "";
            string summary = string.Join(Environment.NewLine, body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).Take(6));
            if (summary.Length > 700) summary = summary[..700] + "…";
            return new(version, url, digest[7..], size, summary.Length == 0 ? "変更点の記載はありません。" : summary);
        }
        return null;
    }
    public async Task<UpdateRelease?> CheckAsync(CancellationToken token) => ParseRelease(
        await http.GetStringAsync("https://api.github.com/repos/marinyan/PadDim/releases/latest", token),
        typeof(AppUpdate).Assembly.GetName().Version!);
    public static bool ShouldOffer(UpdateRelease? release, IEnumerable<string> skipped) => release is not null && !skipped.Contains(release.Version.ToString());
    public static async Task VerifyAsync(string path, UpdateRelease release, CancellationToken token)
    {
        using var file = File.OpenRead(path);
        if (file.Length != release.Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(release.Digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新ファイルの検証に失敗しました。再度ダウンロードしてください。");
    }
    public async Task<string> DownloadAsync(UpdateRelease release, CancellationToken token)
    {
        string folder = Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"PadDim-Setup-{release.Version}-win-x64.exe");
        try
        {
            using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            {
                byte[] buffer = new byte[81920]; long total = 0; int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("更新ファイルのサイズが一致しません");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
            }
            await VerifyAsync(path, release, token);
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8E28E5C-44C1-4B90-8EF2-705F65EAF225}_is1");
        string? location = key?.GetValue("InstallLocation") as string;
        return location is not null && Path.GetFullPath(location).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
    private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";
    public static string BuildHelperScript(int processId, string installer, string directory, string digest, string? resultPath = null)
    {
        string executable = Path.Combine(directory, "PadDim.exe");
        string log = Path.Combine(Path.GetDirectoryName(installer)!, "update.log");
        string result = resultPath ?? Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "update-result.txt");
        return $$"""
        $ErrorActionPreference = 'Stop'
        $installerPath = {{Quote(installer)}}
        $appPath = {{Quote(executable)}}
        $logPath = {{Quote(log)}}
        $resultPath = {{Quote(result)}}
        $mayRestart = $false
        try {
            $oldProcess = Get-Process -Id {{processId}} -ErrorAction SilentlyContinue
            [IO.File]::WriteAllText({{Quote(Path.Combine(Path.GetDirectoryName(installer)!, "helper.ready"))}}, 'ready')
            if ($oldProcess -and !$oldProcess.WaitForExit(120000)) { throw 'PadDim did not exit; update cancelled.' }
            $mayRestart = $true
            $hashStream = [IO.File]::OpenRead($installerPath)
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $actualHash = [BitConverter]::ToString($hasher.ComputeHash($hashStream)).Replace('-', '') }
            finally { $hashStream.Dispose(); $hasher.Dispose() }
            if ($actualHash -ne {{Quote(digest)}}) { throw 'Installer digest mismatch.' }
            $installArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', {{Quote("/DIR=\"" + directory.TrimEnd('\\') + "\"")}}, ('/LOG="' + $logPath + '"'))
            $setup = Start-Process -FilePath $installerPath -ArgumentList $installArgs -WindowStyle Hidden -Wait -PassThru
            if ($setup.ExitCode -ne 0) { throw ('Installer failed: ' + $setup.ExitCode) }
            [IO.File]::WriteAllText($resultPath, 'OK')
        } catch {
            $_ | Out-String | Add-Content -LiteralPath $logPath
            [IO.File]::WriteAllText($resultPath, ('更新に失敗しました。ログ: ' + $logPath))
        } finally {
            if ($mayRestart -and (Test-Path -LiteralPath $appPath)) {
                Start-Process -FilePath $appPath -ArgumentList '--tray' -WindowStyle Hidden
            }
        }
        """;
    }
    public static void LaunchInstaller(string path, UpdateRelease release)
    {
        string script = BuildHelperScript(Environment.ProcessId, path, AppContext.BaseDirectory, release.Digest);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("更新処理を開始できませんでした");
        string ready = Path.Combine(Path.GetDirectoryName(path)!, "helper.ready");
        var watch = Stopwatch.StartNew();
        while (!File.Exists(ready) && !helper.HasExited && watch.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(50);
        if (!File.Exists(ready)) throw new InvalidOperationException("更新補助プログラムを起動できませんでした。PadDimは終了しません。");
    }
}
