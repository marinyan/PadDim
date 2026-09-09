using System.Text.Json;

namespace PadDim;

// Written before the first hardware write; entries survive process termination.
public sealed class BrightnessRecovery(string path)
{
    public sealed record Entry(string Id, uint Minimum, uint Maximum, uint Original);
    private List<Entry>? entries;
    private List<Entry> Entries => entries ??= Read();
    public int Count => Entries.Count;
    private List<Entry> Read()
    {
        if (!File.Exists(path)) return [];
        var saved = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("輝度の復元記録が不正です");
        if (saved.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id) || e.Maximum < e.Minimum || e.Original < e.Minimum || e.Original > e.Maximum)
            || saved.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Count)
            throw new InvalidDataException("輝度の復元記録が不正です");
        return saved;
    }
    private void Save(List<Entry> updated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, updated);
            file.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
        entries = updated;
    }
    public void Capture(IBrightnessTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.RecoveryId)) throw new InvalidOperationException("画面を一意に識別できないため本体輝度を変更しません");
        if (Entries.Any(e => string.Equals(e.Id, target.RecoveryId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("以前の輝度の復元が残っています");
        Save([.. Entries, new(target.RecoveryId, target.Minimum, target.Maximum, target.Original)]);
    }
    public void Complete(string id) => Save(Entries.Where(e => !string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)).ToList());
    public List<string> Recover(IEnumerable<IBrightnessTarget> discovered)
    {
        var targets = discovered.ToList();
        var errors = new List<string>();
        try
        {
            foreach (var entry in Entries.ToArray())
            {
                var matches = targets.Where(t => string.Equals(t.RecoveryId, entry.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) { errors.Add($"{entry.Id}: 復元対象を一意に確認できません。再接続後に再試行します。"); continue; }
                var target = matches[0];
                if (target.Minimum != entry.Minimum || target.Maximum != entry.Maximum)
                { errors.Add($"{target.Name}: 輝度範囲が変わったため復元を保留します。"); continue; }
                try { target.Set(entry.Original); Complete(entry.Id); }
                catch (Exception ex) { errors.Add($"{target.Name}: 復元できません — {ex.Message}"); }
            }
        }
        finally { foreach (var target in targets) target.Dispose(); }
        return errors;
    }
}
