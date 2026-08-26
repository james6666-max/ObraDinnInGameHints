using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ObraDinnInGameHints
{
    internal sealed class GameProgressSource : IProgressSource
    {
        private Book activeBook;
        private BookContent bookContent;
        private FaceLib faceLib;
        private string activeProfileId = "default";
        private string currentFaceId;
        private readonly Action<string> diagnostic;
        private readonly HashSet<string> reportedFailures = new HashSet<string>(StringComparer.Ordinal);

        public GameProgressSource(Action<string> diagnostic)
        {
            this.diagnostic = diagnostic;
        }

        public string ActiveProfileId
        {
            get { return activeProfileId; }
        }

        public string CurrentFaceId
        {
            get { return currentFaceId; }
        }

        public Book ActiveBook
        {
            get { return activeBook; }
        }

        public void SetActiveProfile(string profileId)
        {
            activeProfileId = string.IsNullOrEmpty(profileId) ? "default" : profileId;
        }

        public void SetBook(Book book)
        {
            activeBook = book;
            bookContent = null;
            faceLib = null;
            currentFaceId = null;
            if (book == null)
                return;

            try
            {
                bookContent = AccessTools.Field(typeof(Book), "bookContent").GetValue(book) as BookContent;
                if (bookContent != null)
                    faceLib = AccessTools.Field(typeof(BookContent), "faceLib").GetValue(bookContent) as FaceLib;
            }
            catch
            {
                bookContent = null;
                faceLib = null;
            }
        }

        public void SetCurrentFace(string faceId)
        {
            currentFaceId = faceId;
        }

        public SubjectProgress GetProgress(HintRecord record)
        {
            try
            {
                if (SaveData.it == null || Manifest.it == null)
                    return null;

                string gameFaceId = ResolveGameFaceId(record);
                if (string.IsNullOrEmpty(gameFaceId))
                    return null;

                // The private mapping stores a manifest index, never a face_XX resource key.
                SaveData.FaceDataRo face = SaveData.it.faceRo[gameFaceId];
                if (face == null)
                {
                    ReportOnce("face:" + record.CrewIndex, "No save face exists for a resolved crew index.");
                    return null;
                }

                string[] clueIds = Manifest.it.GetCrewClueMomentIds(gameFaceId);
                HashSet<string> uniqueClues = new HashSet<string>(StringComparer.Ordinal);
                int visited = 0;
                if (clueIds != null)
                {
                    for (int i = 0; i < clueIds.Length; i++)
                    {
                        string clueId = clueIds[i];
                        if (string.IsNullOrEmpty(clueId) || !uniqueClues.Add(clueId))
                            continue;
                        if (SaveData.it.HaveVisitedMoment(clueId))
                            visited++;
                    }
                }

                bool visible = visited > 0;
                if (bookContent != null)
                    visible = BookContent.GetClueStatus(gameFaceId) == BookContent.ClueStatus.Seen;

                bool identityResolved = string.Equals(face.nameId, gameFaceId, StringComparison.Ordinal);
                bool fateResolved = !string.IsNullOrEmpty(face.fateId) &&
                    Manifest.it.IsCorrectFate(gameFaceId, face.fateId);

                return new SubjectProgress
                {
                    GameFaceId = gameFaceId,
                    Visible = visible,
                    MarkedCorrect = face.markedCorrect,
                    IdentityResolved = identityResolved,
                    FateResolved = fateResolved,
                    IsCurrent = string.Equals(currentFaceId, gameFaceId, StringComparison.Ordinal),
                    VisitedClues = visited,
                    TotalClues = uniqueClues.Count
                };
            }
            catch (Exception ex)
            {
                ReportOnce("progress:" + record.CrewIndex, "Progress lookup failed: " + ex.GetType().Name);
                return null;
            }
        }

        public string ResolveGameFaceId(HintRecord record)
        {
            if (record == null || record.CrewIndex < 0 || Manifest.it == null)
                return null;
            try
            {
                string gameFaceId = Manifest.it.GetCrewId(record.CrewIndex);
                if (string.IsNullOrEmpty(gameFaceId))
                    ReportOnce("manifest:" + record.CrewIndex, "Manifest returned no game ID for a crew index.");
                return gameFaceId;
            }
            catch (Exception ex)
            {
                ReportOnce("manifest:" + record.CrewIndex, "Manifest ID resolution failed: " + ex.GetType().Name);
                return null;
            }
        }

        private void ReportOnce(string key, string message)
        {
            if (diagnostic != null && reportedFailures.Add(key))
                diagnostic(message);
        }

        public Sprite GetFaceSprite(string faceId)
        {
            try
            {
                if (faceLib == null)
                    return null;
                FaceLib.Face face = faceLib.Find(faceId);
                if (face == null)
                    return null;
                return face.spriteLo != null ? face.spriteLo : face.spriteHi;
            }
            catch
            {
                return null;
            }
        }
    }
}
