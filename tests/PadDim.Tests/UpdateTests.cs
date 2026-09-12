using PadDim;
using System.Security.Cryptography;
using System.Text.Json;

static class UpdateTests
{
    static void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
    public static void Run()
    {
        string hash = Convert.ToHexString(SHA256.HashData([1, 2, 3]));
        string Make(string tag = "v0.1.10", string? url = null, string? digest = null, bool prerelease = false) => JsonSerializer.Serialize(new
        {
            tag_name = tag, draft = false, prerelease, body = "入力誤判定を修正\n再取得ボタンを改善\n自動更新を追加",
            assets = new[] { new { name = "PadDim-Setup-0.1.10-win-x64.exe", browser_download_url = url ?? "https://github.com/marinyan/PadDim/releases/download/v0.1.10/PadDim-Setup-0.1.10-win-x64.exe", size = 3, digest = digest ?? "sha256:" + hash } }
        });
        var release = AppUpdate.ParseRelease(Make(), new Version(0, 1, 9, 0));
        Check(release?.Version == new Version(0, 1, 10), "Update versions compare numerically");
        Check(release!.Changes.Contains("入力誤判定"), "Update notice includes release changes");
        Check(AppUpdate.ParseRelease(Make(), new Version(0, 1, 10, 0)) is null, "Current version is not reinstalled");
        Check(AppUpdate.ParseRelease(Make(), new Version(0, 2, 0)) is null, "Older release is not installed");
        Check(AppUpdate.ParseRelease(Make(prerelease: true), new Version(0, 1, 9)) is null, "Prereleases are excluded");
        Check(AppUpdate.ParseRelease(Make(url: "https://example.com/installer.exe"), new Version(0, 1, 9)) is null, "Foreign installer URLs are excluded");
        bool invalid = false;
        try { AppUpdate.ParseRelease(Make(digest: "sha256:invalid"), new Version(0, 1, 9)); }
        catch (InvalidDataException) { invalid = true; }
        Check(invalid, "Missing or invalid integrity data prevents update");
        var settings = Settings.FromJson(JsonSerializer.Serialize(new Settings { SkippedUpdateVersions = ["0.1.10"], CheckForUpdates = false }));
        Check(settings.SkippedUpdateVersions.SequenceEqual(["0.1.10"]) && !settings.CheckForUpdates, "Declined version and update preference survive restart");
        Check(!AppUpdate.ShouldOffer(release, settings.SkippedUpdateVersions), "Declined version is not offered or downloaded");
        Check(AppUpdate.ShouldOffer(release with { Version = new(0, 1, 11) }, settings.SkippedUpdateVersions), "Next release is offered after declining previous one");
        Check(!AppUpdate.ShouldOffer(release, ["0.1.10", "0.1.11"]), "Declining a later release does not forget an earlier refusal");
        string file = Path.Combine(Path.GetTempPath(), "PadDim-update-test-" + Guid.NewGuid());
        try
        {
            File.WriteAllBytes(file, [1,2,3]);
            AppUpdate.VerifyAsync(file, release, default).GetAwaiter().GetResult();
            Check(true, "Verified installer accepts matching bytes");
            File.WriteAllBytes(file, [1,2,4]);
            bool rejected = false;
            try { AppUpdate.VerifyAsync(file, release, default).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Modified installer is rejected before execution");
        }
        finally { File.Delete(file); }
    }
}
