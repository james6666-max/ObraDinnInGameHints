using System;
using System.Collections.Generic;

namespace ObraDinnInGameHints
{
    internal enum HintBranch
    {
        Identity = 0,
        Fate = 1
    }

    internal sealed class HintRecord
    {
        // The source key is only for validating the private hint bundle. It is not a game ID.
        public string SourceFaceKey;
        public int CrewIndex;
        public readonly List<string> IdentityHints = new List<string>();
        public readonly List<string> FateHints = new List<string>();

        public IList<string> GetHints(HintBranch branch)
        {
            return branch == HintBranch.Identity ? (IList<string>)IdentityHints : FateHints;
        }
    }

    internal sealed class SubjectProgress
    {
        public string GameFaceId;
        public bool Visible;
        public bool MarkedCorrect;
        public bool IdentityResolved;
        public bool FateResolved;
        public bool IsCurrent;
        public int VisitedClues;
        public int TotalClues;
    }

    internal sealed class HintProgress
    {
        public bool Reflected;
        public int Revealed;
        public bool FinalRevealed;

        public HintProgress Clone()
        {
            return new HintProgress
            {
                Reflected = Reflected,
                Revealed = Revealed,
                FinalRevealed = FinalRevealed
            };
        }
    }

    internal sealed class HintCandidate
    {
        public HintRecord Record;
        public HintBranch Branch;
        public SubjectProgress Progress;
        public HintProgress HintProgress;
        public int Score;

        public string Key
        {
            get { return Progress.GameFaceId + "|" + Branch.ToString(); }
        }

        public IList<string> Hints
        {
            get { return Record.GetHints(Branch); }
        }

        public int NonFinalCount
        {
            get { return Math.Max(0, Hints.Count - 1); }
        }

        public bool AllRelevantCluesVisited
        {
            get { return Progress.TotalClues == 0 || Progress.VisitedClues >= Progress.TotalClues; }
        }
    }

    internal enum AdvanceResultKind
    {
        Hint,
        Locked,
        FinalConfirmationRequired,
        FinalHint,
        NothingAvailable
    }

    internal sealed class AdvanceResult
    {
        public AdvanceResultKind Kind;
        public string Text;
        public int Revealed;
        public int Unlocked;
    }

    internal interface IHintRepository
    {
        IList<HintRecord> Records { get; }
    }

    internal interface IProgressSource
    {
        string ActiveProfileId { get; }
        SubjectProgress GetProgress(HintRecord record);
    }

    internal interface IHintStateStore
    {
        HintProgress Get(string profileId, string faceId, HintBranch branch);
        void Put(string profileId, string faceId, HintBranch branch, HintProgress progress);
        void ResetProfile(string profileId);
    }
}
