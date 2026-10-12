using System;
using System.Collections.Generic;

namespace ObraDinnInGameHints
{
    internal sealed class HintEngine
    {
        private readonly IHintRepository repository;
        private readonly IProgressSource progressSource;
        private readonly IHintStateStore stateStore;
        private readonly HashSet<string> sessionExcluded = new HashSet<string>(StringComparer.Ordinal);
        private HintCandidate selectedCandidate;
        private string selectedProfile;

        public HintEngine(IHintRepository repository, IProgressSource progressSource, IHintStateStore stateStore)
        {
            this.repository = repository;
            this.progressSource = progressSource;
            this.stateStore = stateStore;
        }

        public HintCandidate SelectCandidate()
        {
            HintCandidate best = null;
            IList<HintRecord> records = repository.Records;
            for (int i = 0; i < records.Count; i++)
            {
                HintRecord record = records[i];
                SubjectProgress progress = progressSource.GetProgress(record);
                if (progress == null || !progress.Visible || progress.MarkedCorrect)
                    continue;

                HintCandidate candidate = BuildCandidate(record, progress);
                if (candidate == null || sessionExcluded.Contains(candidate.Key))
                    continue;

                if (best == null || Compare(candidate, best) < 0)
                    best = candidate;
            }
            selectedCandidate = best;
            selectedProfile = progressSource.ActiveProfileId;
            return best;
        }

        private HintCandidate BuildCandidate(HintRecord record, SubjectProgress progress)
        {
            HintCandidate identity = BuildBranchCandidate(record, progress, HintBranch.Identity, progress.IdentityResolved);
            HintCandidate fate = BuildBranchCandidate(record, progress, HintBranch.Fate, progress.FateResolved);

            if (identity == null)
                return fate;
            if (fate == null)
                return identity;

            if (identity.HintProgress.Revealed != fate.HintProgress.Revealed)
                return identity.HintProgress.Revealed < fate.HintProgress.Revealed ? identity : fate;
            return identity;
        }

        private HintCandidate BuildBranchCandidate(HintRecord record, SubjectProgress progress, HintBranch branch, bool resolved)
        {
            if (resolved || record.GetHints(branch).Count == 0)
                return null;

            HintProgress hintProgress = stateStore.Get(progressSource.ActiveProfileId, progress.GameFaceId, branch);
            if (hintProgress.FinalRevealed)
                return null;

            int ratioScore = progress.TotalClues == 0
                ? 300
                : (int)Math.Round(300.0 * progress.VisitedClues / Math.Max(1, progress.TotalClues));
            int score = ratioScore;
            if (progress.IsCurrent)
                score += 1000;
            if (progress.TotalClues == 0 || progress.VisitedClues >= progress.TotalClues)
                score += 400;
            score += Math.Max(0, 100 - hintProgress.Revealed * 10);

            return new HintCandidate
            {
                Record = record,
                Branch = branch,
                Progress = progress,
                HintProgress = hintProgress,
                Score = score
            };
        }

        private static int Compare(HintCandidate left, HintCandidate right)
        {
            int score = right.Score.CompareTo(left.Score);
            if (score != 0)
                return score;
            int face = left.Record.CrewIndex.CompareTo(right.Record.CrewIndex);
            if (face != 0)
                return face;
            return left.Branch.CompareTo(right.Branch);
        }

        public int GetUnlockedNonFinal(HintCandidate candidate)
        {
            int nonFinal = candidate.NonFinalCount;
            if (nonFinal <= 0)
                return 0;
            if (candidate.Progress.TotalClues <= 0)
                return nonFinal;
            if (candidate.Progress.VisitedClues <= 0)
                return 1;

            int unlocked = (int)Math.Ceiling(
                nonFinal * candidate.Progress.VisitedClues / (double)candidate.Progress.TotalClues);
            return Math.Max(1, Math.Min(nonFinal, unlocked));
        }

        public AdvanceResult RequestNextHint(HintCandidate candidate)
        {
            if (!RefreshCandidate(candidate))
                return new AdvanceResult { Kind = AdvanceResultKind.Locked };
            HintProgress state = stateStore.Get(progressSource.ActiveProfileId, candidate.Progress.GameFaceId, candidate.Branch);
            if (state.FinalRevealed)
                return new AdvanceResult { Kind = AdvanceResultKind.NothingAvailable };
            state.Reflected = true;
            int unlocked = GetUnlockedNonFinal(candidate);

            if (state.Revealed < unlocked)
            {
                state.Revealed++;
                stateStore.Put(progressSource.ActiveProfileId, candidate.Progress.GameFaceId, candidate.Branch, state);
                candidate.HintProgress = state;
                return new AdvanceResult
                {
                    Kind = AdvanceResultKind.Hint,
                    Text = candidate.Hints[state.Revealed - 1],
                    Revealed = state.Revealed,
                    Unlocked = unlocked
                };
            }

            if (state.Revealed < candidate.NonFinalCount)
            {
                return new AdvanceResult
                {
                    Kind = AdvanceResultKind.Locked,
                    Revealed = state.Revealed,
                    Unlocked = unlocked
                };
            }

            if (!candidate.AllRelevantCluesVisited)
            {
                return new AdvanceResult
                {
                    Kind = AdvanceResultKind.Locked,
                    Revealed = state.Revealed,
                    Unlocked = unlocked
                };
            }

            return new AdvanceResult
            {
                Kind = AdvanceResultKind.FinalConfirmationRequired,
                Revealed = state.Revealed,
                Unlocked = unlocked
            };
        }

        public AdvanceResult RevealFinalHint(HintCandidate candidate)
        {
            if (!RefreshCandidate(candidate) || !candidate.AllRelevantCluesVisited || candidate.Hints.Count == 0)
                return new AdvanceResult { Kind = AdvanceResultKind.Locked };

            HintProgress state = stateStore.Get(progressSource.ActiveProfileId, candidate.Progress.GameFaceId, candidate.Branch);
            if (state.Revealed < candidate.NonFinalCount)
                return new AdvanceResult { Kind = AdvanceResultKind.Locked };
            state.Reflected = true;
            state.Revealed = Math.Max(state.Revealed, candidate.NonFinalCount);
            state.FinalRevealed = true;
            stateStore.Put(progressSource.ActiveProfileId, candidate.Progress.GameFaceId, candidate.Branch, state);
            candidate.HintProgress = state;
            return new AdvanceResult
            {
                Kind = AdvanceResultKind.FinalHint,
                Text = candidate.Hints[candidate.Hints.Count - 1],
                Revealed = state.Revealed,
                Unlocked = candidate.NonFinalCount
            };
        }

        public string GetLatestHint(HintCandidate candidate)
        {
            // A restored or restarted save can have less evidence than its saved hint history.
            int revealed = Math.Min(candidate.HintProgress.Revealed, GetUnlockedNonFinal(candidate));
            if (candidate.HintProgress.FinalRevealed && candidate.Hints.Count > 0)
                return candidate.Hints[candidate.Hints.Count - 1];
            if (revealed <= 0)
                return null;
            return candidate.Hints[revealed - 1];
        }

        public void ExcludeForSession(HintCandidate candidate)
        {
            if (candidate != null)
                sessionExcluded.Add(candidate.Key);
        }

        public void ClearSessionExclusions()
        {
            sessionExcluded.Clear();
            selectedCandidate = null;
        }

        private bool RefreshCandidate(HintCandidate candidate)
        {
            // A panel can outlive its profile or the game progress used to select it.
            if (candidate == null || !object.ReferenceEquals(candidate, selectedCandidate) ||
                !string.Equals(selectedProfile, progressSource.ActiveProfileId, StringComparison.Ordinal))
                return false;
            SubjectProgress current = progressSource.GetProgress(candidate.Record);
            if (current == null || !current.Visible || current.MarkedCorrect ||
                !string.Equals(current.GameFaceId, candidate.Progress.GameFaceId, StringComparison.Ordinal) ||
                (candidate.Branch == HintBranch.Identity ? current.IdentityResolved : current.FateResolved))
                return false;
            candidate.Progress = current;
            return true;
        }

        public void ResetCurrentProfile()
        {
            stateStore.ResetProfile(progressSource.ActiveProfileId);
            ClearSessionExclusions();
        }
    }
}
