using System;
using System.Collections.Generic;
using System.IO;
using ObraDinnInGameHints;

internal static class LogicTests
{
    private sealed class Repository : IHintRepository
    {
        public readonly List<HintRecord> Items = new List<HintRecord>();
        public IList<HintRecord> Records { get { return Items; } }
    }

    private sealed class Progress : IProgressSource
    {
        public string ActiveProfileId { get; set; }
        public readonly Dictionary<string, SubjectProgress> Values = new Dictionary<string, SubjectProgress>();
        public SubjectProgress GetProgress(HintRecord record) { return Values[record.SourceFaceKey]; }
    }

    private sealed class MemoryStore : IHintStateStore
    {
        private readonly Dictionary<string, HintProgress> values = new Dictionary<string, HintProgress>();
        private static string Key(string p, string f, HintBranch b) { return p + "|" + f + "|" + b; }
        public HintProgress Get(string p, string f, HintBranch b)
        {
            HintProgress value;
            return values.TryGetValue(Key(p, f, b), out value) ? value.Clone() : new HintProgress();
        }
        public void Put(string p, string f, HintBranch b, HintProgress v) { values[Key(p, f, b)] = v.Clone(); }
        public void ResetProfile(string p)
        {
            List<string> remove = new List<string>();
            foreach (string key in values.Keys) if (key.StartsWith(p + "|")) remove.Add(key);
            for (int i = 0; i < remove.Count; i++) values.Remove(remove[i]);
        }
    }

    private static int failures;

    public static int Main()
    {
        Run("current target wins", CurrentTargetWins);
        Run("tier gating", TierGating);
        Run("resolved branch selection", ResolvedBranchSelection);
        Run("session exclusion", SessionExclusion);
        Run("double-confirm boundary", FinalBoundary);
        Run("game ID state key", GameIdStateKey);
        Run("centered UI actions", CenteredUiActions);
        Run("native cursor retry state", NativeCursorRetryState);
        Run("mouse input override state", MouseInputOverrideState);
        Run("json state isolation", JsonStateIsolation);
        Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TESTS FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static HintRecord Record(string face, int crewIndex)
    {
        HintRecord record = new HintRecord { SourceFaceKey = face, CrewIndex = crewIndex };
        record.IdentityHints.AddRange(new[] { "i1", "i2", "i3", "ifinal" });
        record.FateHints.AddRange(new[] { "f1", "f2", "ffinal" });
        return record;
    }

    private static void CurrentTargetWins()
    {
        Repository repo = new Repository();
        repo.Items.Add(Record("face_01", 1));
        repo.Items.Add(Record("face_02", 2));
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values["face_01"] = P("game_01", true, 3, 3, false, false, false);
        progress.Values["face_02"] = P("game_02", true, 1, 3, true, false, false);
        HintEngine engine = new HintEngine(repo, progress, new MemoryStore());
        Equal("game_02", engine.SelectCandidate().Progress.GameFaceId);
    }

    private static void TierGating()
    {
        Repository repo = new Repository();
        HintRecord record = Record("face_01", 1);
        repo.Items.Add(record);
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values[record.SourceFaceKey] = P("game_01", true, 0, 3, false, false, false);
        HintEngine engine = new HintEngine(repo, progress, new MemoryStore());
        Equal(1, engine.GetUnlockedNonFinal(engine.SelectCandidate()));
        progress.Values[record.SourceFaceKey] = P("game_01", true, 1, 3, false, false, false);
        Equal(1, engine.GetUnlockedNonFinal(engine.SelectCandidate()));
        progress.Values[record.SourceFaceKey] = P("game_01", true, 2, 3, false, false, false);
        Equal(2, engine.GetUnlockedNonFinal(engine.SelectCandidate()));
        progress.Values[record.SourceFaceKey] = P("game_01", true, 3, 3, false, false, false);
        Equal(3, engine.GetUnlockedNonFinal(engine.SelectCandidate()));
        progress.Values[record.SourceFaceKey] = P("game_01", true, 0, 0, false, false, false);
        Equal(3, engine.GetUnlockedNonFinal(engine.SelectCandidate()));
    }

    private static void ResolvedBranchSelection()
    {
        Repository repo = new Repository();
        HintRecord record = Record("face_01", 1);
        repo.Items.Add(record);
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values[record.SourceFaceKey] = P("game_01", true, 1, 2, false, true, false);
        HintCandidate candidate = new HintEngine(repo, progress, new MemoryStore()).SelectCandidate();
        Equal(HintBranch.Fate, candidate.Branch);
    }

    private static void SessionExclusion()
    {
        Repository repo = new Repository();
        repo.Items.Add(Record("face_01", 1));
        repo.Items.Add(Record("face_02", 2));
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values["face_01"] = P("game_01", true, 2, 3, false, false, false);
        progress.Values["face_02"] = P("game_02", true, 1, 3, false, false, false);
        HintEngine engine = new HintEngine(repo, progress, new MemoryStore());
        HintCandidate first = engine.SelectCandidate();
        engine.ExcludeForSession(first);
        NotEqual(first.Progress.GameFaceId, engine.SelectCandidate().Progress.GameFaceId);
    }

    private static void FinalBoundary()
    {
        Repository repo = new Repository();
        HintRecord record = Record("face_01", 1);
        repo.Items.Add(record);
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values[record.SourceFaceKey] = P("game_01", true, 3, 3, false, false, false);
        MemoryStore store = new MemoryStore();
        HintEngine engine = new HintEngine(repo, progress, store);
        HintCandidate candidate = engine.SelectCandidate();
        Equal(AdvanceResultKind.Hint, engine.RequestNextHint(candidate).Kind);
        Equal(AdvanceResultKind.Hint, engine.RequestNextHint(candidate).Kind);
        Equal(AdvanceResultKind.Hint, engine.RequestNextHint(candidate).Kind);
        Equal(AdvanceResultKind.FinalConfirmationRequired, engine.RequestNextHint(candidate).Kind);
        Equal(AdvanceResultKind.FinalHint, engine.RevealFinalHint(candidate).Kind);
        Equal(true, store.Get("P1", "game_01", HintBranch.Identity).FinalRevealed);
    }

    private static void GameIdStateKey()
    {
        Repository repo = new Repository();
        HintRecord record = Record("face_01", 6);
        repo.Items.Add(record);
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values[record.SourceFaceKey] = P("crew_real_06", true, 1, 3, false, false, false);
        MemoryStore store = new MemoryStore();
        HintEngine engine = new HintEngine(repo, progress, store);
        HintCandidate candidate = engine.SelectCandidate();
        Equal("crew_real_06|Identity", candidate.Key);
        Equal(AdvanceResultKind.Hint, engine.RequestNextHint(candidate).Kind);
        Equal(1, store.Get("P1", "crew_real_06", HintBranch.Identity).Revealed);
        Equal(0, store.Get("P1", "face_01", HintBranch.Identity).Revealed);
    }

    private static void JsonStateIsolation()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ObraDinnHintTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        try
        {
            JsonHintStateStore store = new JsonHintStateStore(path, null);
            store.Put("A", "face_01", HintBranch.Identity, new HintProgress { Reflected = true, Revealed = 2 });
            store.Put("B", "face_01", HintBranch.Identity, new HintProgress { Reflected = true, Revealed = 1 });
            JsonHintStateStore loaded = new JsonHintStateStore(path, null);
            Equal(2, loaded.Get("A", "face_01", HintBranch.Identity).Revealed);
            Equal(1, loaded.Get("B", "face_01", HintBranch.Identity).Revealed);
            loaded.ResetProfile("A");
            Equal(0, loaded.Get("A", "face_01", HintBranch.Identity).Revealed);
            Equal(1, loaded.Get("B", "face_01", HintBranch.Identity).Revealed);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void CenteredUiActions()
    {
        float[] positions = UiPresentation.CenterButtonPositions(new[] { 230f, 160f }, 18f);
        Equal(-89f, positions[0]);
        Equal(124f, positions[1]);
        float leftEdge = positions[0] - 115f;
        float rightEdge = positions[1] + 80f;
        Equal(-leftEdge, rightEdge);
    }

    private static void NativeCursorRetryState()
    {
        Equal(true, UiPresentation.ShouldRetryNativeCursor(true, false, false));
        Equal(false, UiPresentation.ShouldRetryNativeCursor(true, true, true));
        Equal(true, UiPresentation.ShouldRetryNativeCursor(true, true, false));
        Equal(false, UiPresentation.ShouldRetryNativeCursor(false, false, false));
    }

    private static void MouseInputOverrideState()
    {
        Equal(true, UiPresentation.ShouldOverrideMouseInput(true));
        Equal(false, UiPresentation.ShouldOverrideMouseInput(false));
        Equal(true, UiPresentation.ShouldRestoreMouseState(false, true));
        Equal(false, UiPresentation.ShouldRestoreMouseState(true, true));
        Equal(false, UiPresentation.ShouldRestoreMouseState(false, false));
    }

    private static SubjectProgress P(string gameFaceId, bool visible, int visited, int total, bool current, bool identity, bool fate)
    {
        return new SubjectProgress
        {
            GameFaceId = gameFaceId,
            Visible = visible,
            VisitedClues = visited,
            TotalClues = total,
            IsCurrent = current,
            IdentityResolved = identity,
            FateResolved = fate
        };
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Equal(object expected, object actual)
    {
        if (!object.Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual);
    }
    private static void NotEqual(object left, object right)
    {
        if (object.Equals(left, right)) throw new Exception("values unexpectedly equal");
    }
}
