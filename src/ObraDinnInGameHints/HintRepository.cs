using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace ObraDinnInGameHints
{
    internal sealed class EmbeddedHintRepository : IHintRepository
    {
        private const string ResourceName = "ObraDinnInGameHints.Resources.hints.dat";
        private readonly List<HintRecord> records = new List<HintRecord>();

        public EmbeddedHintRepository()
        {
            Load();
        }

        public IList<HintRecord> Records
        {
            get { return records.AsReadOnly(); }
        }

        private void Load()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
                throw new InvalidOperationException("Embedded hint resource is missing.");

            using (stream)
            using (DeflateStream compressed = new DeflateStream(stream, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(compressed, System.Text.Encoding.UTF8))
            {
                string magic = reader.ReadString();
                if (magic != "ODIH2")
                    throw new InvalidOperationException("Unsupported hint resource format.");
                int count = reader.ReadInt32();
                if (count <= 0 || count > 1000)
                    throw new InvalidOperationException("Hint record count is invalid.");

                HashSet<string> faces = new HashSet<string>(StringComparer.Ordinal);
                HashSet<int> crewIndices = new HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    HintRecord record = new HintRecord();
                    record.SourceFaceKey = reader.ReadString();
                    record.CrewIndex = reader.ReadInt32();
                    ReadStrings(reader, record.IdentityHints);
                    ReadStrings(reader, record.FateHints);

                    if (string.IsNullOrEmpty(record.SourceFaceKey) || record.CrewIndex < 0)
                        throw new InvalidOperationException("Hint mapping contains an empty identifier.");
                    if (!faces.Add(record.SourceFaceKey) || !crewIndices.Add(record.CrewIndex))
                        throw new InvalidOperationException("Hint mapping contains a duplicate identifier.");
                    if (record.IdentityHints.Count == 0 && record.FateHints.Count == 0)
                        throw new InvalidOperationException("Hint mapping contains an empty record.");

                    records.Add(record);
                }
            }
        }

        private static void ReadStrings(BinaryReader reader, List<string> destination)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 100)
                throw new InvalidOperationException("Hint tier count is invalid.");
            for (int i = 0; i < count; i++)
                destination.Add(reader.ReadString());
        }
    }
}
