using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WorldAI
{
    internal static class SemanticLore
    {
        private const int ChunkSize = 1600;
        private const int MaxChunks = 640;
        private static readonly Dictionary<string, double[]> Index = new Dictionary<string, double[]>(StringComparer.Ordinal);

        private sealed class Chunk
        {
            internal Entry Entry;
            internal string Text;
            internal string Key;
            internal double Score;
        }

        internal static async Task<List<Entry>> Find(World world, Session session, IEnumerable<Message> history,
            List<Entry> keywordHits, CancellationToken token)
        {
            var entries = Context.EnabledEntries(world, session).Where(entry => entry.UsesSemantics && !entry.Always &&
                !keywordHits.Any(hit => hit.Id == entry.Id)).ToList();
            if (entries.Count == 0) return new List<Entry>();
            var latest = history.LastOrDefault(message => message.Role == "user" && !String.IsNullOrWhiteSpace(message.Text));
            if (latest == null) return new List<Entry>();
            var api = Context.Find(world.Models, world.Settings.SemanticApiId);
            string model = world.Settings.EmbeddingModel;
            if (api == null || api.Demo || String.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("请在世界书的「语义检索设置」里配置向量模型。");

            string prefix = api.Id + "\n" + api.Endpoint + "\n" + model + "\n";
            var chunks = new List<Chunk>();
            foreach (var entry in entries)
                foreach (string text in Split(entry.Content))
                {
                    chunks.Add(new Chunk { Entry = entry, Text = text, Key = prefix + entry.Name + "\n" + text });
                    if (chunks.Count > MaxChunks)
                        throw new InvalidDataException("参与语义检索的设定太多，请把当前世界书拆小一些。");
                }
            if (Index.Count > 2048) Index.Clear();
            string question = latest.Text.Length <= ChunkSize ? latest.Text : latest.Text.Substring(latest.Text.Length - ChunkSize);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(40));
                var missing = chunks.GroupBy(chunk => chunk.Key).Select(group => group.First())
                    .Where(chunk => !Index.ContainsKey(chunk.Key)).ToList();
                for (int offset = 0; offset < missing.Count; offset += 16)
                {
                    var batch = missing.Skip(offset).Take(16).ToList();
                    var vectors = await Models.Embed(api, model, batch.Select(chunk => chunk.Entry.Name + "\n" + chunk.Text).ToArray(), deadline.Token);
                    for (int i = 0; i < batch.Count; i++) Index[batch[i].Key] = vectors[i];
                }
                var query = (await Models.Embed(api, model, new[] { question }, deadline.Token))[0];
                foreach (var chunk in chunks)
                    chunk.Score = Similarity(query, Index[chunk.Key]);
            }
            return chunks.Where(chunk => chunk.Score >= 0.42).OrderByDescending(chunk => chunk.Score)
                .GroupBy(chunk => chunk.Entry.Id).Take(4).Select(group => group.First())
                .Select(chunk => new Entry { Id = chunk.Entry.Id, Name = chunk.Entry.Name, Content = chunk.Text }).ToList();
        }

        private static IEnumerable<string> Split(string text)
        {
            for (int start = 0; start < text.Length;)
            {
                int length = Math.Min(ChunkSize, text.Length - start);
                if (start + length < text.Length)
                {
                    int newline = text.LastIndexOf('\n', start + length - 1, length);
                    if (newline > start + length / 2) length = newline - start + 1;
                    else if (Char.IsHighSurrogate(text[start + length - 1])) length--;
                }
                string chunk = text.Substring(start, length).Trim();
                if (chunk.Length > 0) yield return chunk;
                start += length;
            }
        }

        private static double Similarity(double[] query, double[] candidate)
        {
            if (query.Length != candidate.Length)
                throw new InvalidDataException("向量维度发生变化，请重启客户端后再试。");
            double dot = 0, queryLength = 0, candidateLength = 0;
            for (int i = 0; i < query.Length; i++)
            {
                dot += query[i] * candidate[i];
                queryLength += query[i] * query[i];
                candidateLength += candidate[i] * candidate[i];
            }
            return queryLength == 0 || candidateLength == 0 ? 0 : dot / Math.Sqrt(queryLength * candidateLength);
        }
    }
}
