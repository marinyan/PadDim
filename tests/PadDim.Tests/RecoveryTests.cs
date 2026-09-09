using PadDim;

static class RecoveryTests
{
    private static void Check(bool value, string description)
    { if (!value) throw new Exception(description); Console.WriteLine("PASS " + description); }
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PadDim-recovery-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "recovery.json");
        try
        {
            var journal = new BrightnessRecovery(path);
            var target = new RecoveryTarget("panel-a", 72, value =>
            {
                Check(new BrightnessRecovery(path).Count == 1, "Original persisted before hardware write");
            });
            var session = new BrightnessSession(journal);
            Check(session.Dim([target], 0, () => true).Count == 0 && target.Last == 0, "Dim to zero with recovery journal");
            // Simulate termination without Restore, then a new process with new handles.
            var nextProcess = new BrightnessRecovery(path);
            var reconnected = new RecoveryTarget("panel-a", 0);
            Check(nextProcess.Recover([reconnected]).Count == 0 && reconnected.Last == 72, "New process restores pre-restart brightness, not current zero");
            Check(new BrightnessRecovery(path).Count == 0, "Successful recovery clears durable entry");

            journal = new BrightnessRecovery(path);
            session = new BrightnessSession(journal);
            var failed = new RecoveryTarget("panel-a", 80) { Fail = true };
            Check(session.Dim([failed], 0, () => true).Count == 1 && new BrightnessRecovery(path).Count == 1, "Failed dim write retains recovery record");
            Check(session.Restore().Count == 1 && journal.Count == 1, "Failed restore retains recovery record");
            failed.Fail = false;
            Check(session.Restore().Count == 0 && new BrightnessRecovery(path).Count == 0, "Restore retry clears record after success");

            journal.Capture(new RecoveryTarget("panel-a", 65));
            var wrong = new RecoveryTarget("panel-b", 0);
            Check(journal.Recover([wrong]).Count == 1 && wrong.Writes == 0 && journal.Count == 1, "Never restore another monitor; keep disconnected record");
            var first = new RecoveryTarget("panel-a", 0);
            var duplicate = new RecoveryTarget("panel-a", 0);
            Check(journal.Recover([first, duplicate]).Count == 1 && first.Writes == 0 && duplicate.Writes == 0, "Ambiguous monitor identity is not restored");
            var changed = new RecoveryTarget("panel-a", 0) { Maximum = 200 };
            Check(journal.Recover([changed]).Count == 1 && changed.Writes == 0, "Changed brightness range is not restored");
            var back = new RecoveryTarget("panel-a", 0);
            Check(journal.Recover([back]).Count == 0 && back.Last == 65, "Reconnect restores saved original");

            journal.Capture(new RecoveryTarget("panel-a", 35));
            journal.Capture(new RecoveryTarget("panel-b", 85));
            var panelA = new RecoveryTarget("panel-a", 0);
            Check(journal.Recover([panelA]).Count == 1 && panelA.Last == 35 && new BrightnessRecovery(path).Count == 1,
                "Partial recovery clears only the connected monitor's entry");
            var panelB = new RecoveryTarget("panel-b", 0);
            Check(new BrightnessRecovery(path).Recover([panelB]).Count == 0 && panelB.Last == 85,
                "Another process recovers the remaining monitor independently");
            journal = new BrightnessRecovery(path);

            var blocked = new BrightnessSession(new BrightnessRecovery(directory));
            var noWrite = new RecoveryTarget("panel-a", 50);
            Check(blocked.Dim([noWrite], 0, () => true).Count == 1 && noWrite.Writes == 0, "Cannot persist original: no hardware dimming");
            var unknown = new RecoveryTarget("", 50);
            Check(new BrightnessSession(journal).Dim([unknown], 0, () => true).Count == 1 && unknown.Writes == 0, "Missing stable identity prevents hardware dimming");
            File.WriteAllText(path, "broken JSON");
            var corrupt = new RecoveryTarget("panel-a", 50);
            Check(new BrightnessSession(new BrightnessRecovery(path)).Dim([corrupt], 0, () => true).Count == 1 && corrupt.Writes == 0, "Corrupt journal cannot be overwritten by dimming");
        }
        finally
        {
            foreach (string file in new[] { path, path + ".tmp", directory + ".tmp" })
                if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
    private sealed class RecoveryTarget(string id, uint original, Action<uint>? beforeWrite = null) : IBrightnessTarget
    {
        public string Name => id;
        public string RecoveryId => id;
        public uint Minimum => 0;
        public uint Maximum { get; init; } = 100;
        public uint Original { get; } = original;
        public uint Last { get; private set; } = original;
        public int Writes { get; private set; }
        public bool Fail { get; set; }
        public void Set(uint value) { beforeWrite?.Invoke(value); Writes++; if (Fail) throw new IOException("Driver failure"); Last = value; }
        public void Dispose() { }
    }
}
