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
        Run("final cannot skip tiers", FinalCannotSkipTiers);
        Run("stale candidate profile isolation", StaleCandidateProfileIsolation);
        Run("candidate refreshes clue progress", CandidateRefreshesClueProgress);
        Run("latest hint respects restored save progress", LatestHintRespectsRestoredSaveProgress);
        Run("resolved candidate cannot reveal", ResolvedCandidateCannotReveal);
        Run("game ID state key", GameIdStateKey);
        Run("centered UI actions", CenteredUiActions);
        Run("native cursor retry state", NativeCursorRetryState);
        Run("mouse input override state", MouseInputOverrideState);
        Run("json state isolation", JsonStateIsolation);
        Run("json rejects partial documents", JsonRejectsPartialDocuments);
        Run("json whitespace and escaped identifiers", JsonWhitespaceAndEscapedIdentifiers);
        Run("json failed save preserves state", JsonFailedSavePreservesState);
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

    private static void FinalCannotSkipTiers()
    {
        Repository repo = new Repository();
        repo.Items.Add(Record("face_01", 1));
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values["face_01"] = P("game_01", true, 3, 3, false, false, false);
        MemoryStore store = new MemoryStore();
        HintEngine engine = new HintEngine(repo, progress, store);
        Equal(AdvanceResultKind.Locked, engine.RevealFinalHint(engine.SelectCandidate()).Kind);
        Equal(false, store.Get("P1", "game_01", HintBranch.Identity).FinalRevealed);
    }

    private static void StaleCandidateProfileIsolation()
    {
        Repository repo = new Repository();
        repo.Items.Add(Record("face_01", 1));
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values["face_01"] = P("game_01", true, 3, 3, false, false, false);
        MemoryStore store = new MemoryStore();
        HintEngine engine = new HintEngine(repo, progress, store);
        HintCandidate candidate = engine.SelectCandidate();
        progress.ActiveProfileId = "P2";
        Equal(AdvanceResultKind.Locked, engine.RequestNextHint(candidate).Kind);
        Equal(AdvanceResultKind.Locked, engine.RevealFinalHint(candidate).Kind);
        Equal(0, store.Get("P2", "game_01", HintBranch.Identity).Revealed);
        progress.ActiveProfileId = "P1";
        engine.ResetCurrentProfile();
        Equal(AdvanceResultKind.Locked, engine.RequestNextHint(candidate).Kind);
    }

    private static void CandidateRefreshesClueProgress()
    {
        Repository repo = new Repository();
        repo.Items.Add(Record("face_01", 1));
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values["face_01"] = P("game_01", true, 3, 3, false, false, false);
        MemoryStore store = new MemoryStore();
        HintEngine engine = new HintEngine(repo, progress, store);
        HintCandidate candidate = engine.SelectCandidate();
        Equal(AdvanceResultKind.Hint, engine.RequestNextHint(candidate).Kind);
        progress.Values["face_01"] = P("game_01", true, 0, 3, false, false, false);
        Equal(AdvanceResultKind.Locked, engine.RequestNextHint(candidate).Kind);
        store.Put("P1", "game_01", HintBranch.Identity, new HintProgress { Revealed = 3 });
        Equal(AdvanceResultKind.Locked, engine.RevealFinalHint(candidate).Kind);
        progress.Values["face_01"] = P("game_01", true, 3, 3, false, false, false);
        Equal(AdvanceResultKind.FinalConfirmationRequired, engine.RequestNextHint(candidate).Kind);
        Equal(AdvanceResultKind.FinalHint, engine.RevealFinalHint(candidate).Kind);
        Equal(AdvanceResultKind.NothingAvailable, engine.RequestNextHint(candidate).Kind);
    }

    private static void LatestHintRespectsRestoredSaveProgress()
    {
        Repository repo = new Repository();
        repo.Items.Add(Record("face_01", 1));
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values["face_01"] = P("game_01", true, 3, 3, false, false, true);
        MemoryStore store = new MemoryStore();
        HintEngine engine = new HintEngine(repo, progress, store);
        HintCandidate candidate = engine.SelectCandidate();
        for (int i = 0; i < 3; i++)
            Equal(AdvanceResultKind.Hint, engine.RequestNextHint(candidate).Kind);
        Equal("i3", engine.GetLatestHint(candidate));

        // Loading earlier progress in the same slot must not display a later historical tier.
        engine.ClearSessionExclusions();
        progress.Values["face_01"] = P("game_01", true, 1, 3, false, false, true);
        candidate = engine.SelectCandidate();
        Equal("i1", engine.GetLatestHint(candidate));
        Equal(AdvanceResultKind.Locked, engine.RequestNextHint(candidate).Kind);
        Equal(3, store.Get("P1", "game_01", HintBranch.Identity).Revealed);

        // Preserve history so previously read tiers return as their evidence is recovered.
        progress.Values["face_01"] = P("game_01", true, 2, 3, false, false, true);
        Equal("i2", engine.GetLatestHint(engine.SelectCandidate()));
        progress.Values["face_01"] = P("game_01", true, 3, 3, false, false, true);
        Equal("i3", engine.GetLatestHint(engine.SelectCandidate()));
    }

    private static void ResolvedCandidateCannotReveal()
    {
        Repository repo = new Repository();
        repo.Items.Add(Record("face_01", 1));
        Progress progress = new Progress { ActiveProfileId = "P1" };
        progress.Values["face_01"] = P("game_01", true, 3, 3, false, false, false);
        HintEngine engine = new HintEngine(repo, progress, new MemoryStore());
        HintCandidate candidate = engine.SelectCandidate();
        progress.Values["face_01"].IdentityResolved = true;
        Equal(AdvanceResultKind.Locked, engine.RequestNextHint(candidate).Kind);
        Equal(AdvanceResultKind.Locked, engine.RevealFinalHint(candidate).Kind);
        progress.Values["face_01"].IdentityResolved = false;
        progress.Values["face_01"].MarkedCorrect = true;
        Equal(AdvanceResultKind.Locked, engine.RequestNextHint(candidate).Kind);
    }

    private static void JsonRejectsPartialDocuments()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ObraDinnHintTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        try
        {
            JsonHintStateStore store = new JsonHintStateStore(path, null);
            store.Put("A", "face_01", HintBranch.Identity, new HintProgress { Revealed = 2 });
            string valid = File.ReadAllText(path);
            string[] invalid = new[]
            {
                valid.Substring(0, valid.Length - 2),
                valid.Replace("\"version\":1", "\"version\":10"),
                valid.Replace("]}", ",{\"broken\":true}]}"),
                valid.Replace("]}", ",]}"),
                valid + " trailing garbage"
            };
            for (int i = 0; i < invalid.Length; i++)
            {
                File.WriteAllText(path, invalid[i]);
                bool logged = false;
                JsonHintStateStore loaded = new JsonHintStateStore(path, delegate(string message) { logged = true; });
                Equal(0, loaded.Get("A", "face_01", HintBranch.Identity).Revealed);
                Equal(true, logged);
            }
            Equal(true, Directory.GetFiles(dir, "*.corrupt-*.json").Length > 0);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void JsonWhitespaceAndEscapedIdentifiers()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ObraDinnHintTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        try
        {
            string profile = "A\t\n\r\"\\";
            JsonHintStateStore store = new JsonHintStateStore(path, null);
            store.Put(profile, "face\b\f", HintBranch.Fate, new HintProgress { Revealed = 2 });
            string json = File.ReadAllText(path).Replace(":", " : ").Replace(",", ",\n ");
            File.WriteAllText(path, "\n " + json + "\n");
            JsonHintStateStore loaded = new JsonHintStateStore(path, null);
            Equal(2, loaded.Get(profile, "face\b\f", HintBranch.Fate).Revealed);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void JsonFailedSavePreservesState()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ObraDinnHintTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        try
        {
            JsonHintStateStore store = new JsonHintStateStore(path, null);
            store.Put("A", "face_01", HintBranch.Identity, new HintProgress { Revealed = 1 });
            string original = File.ReadAllText(path);
            using (FileStream held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try { store.Put("A", "face_01", HintBranch.Identity, new HintProgress { Revealed = 2 }); }
                catch (IOException) { failed = true; }
                Equal(true, failed);
                Equal(1, store.Get("A", "face_01", HintBranch.Identity).Revealed);
                Equal(original, File.ReadAllText(path));
                failed = false;
                try { store.ResetProfile("A"); }
                catch (IOException) { failed = true; }
                Equal(true, failed);
                Equal(1, store.Get("A", "face_01", HintBranch.Identity).Revealed);
                Equal(original, File.ReadAllText(path));
            }
            Equal(1, new JsonHintStateStore(path, null).Get("A", "face_01", HintBranch.Identity).Revealed);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
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
