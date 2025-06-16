using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Text;
using RAGDotNet.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace RAGDotNet.Services;

public static class DataIngestor
{
    public static async Task Ingest(
        string sourceDirectory,
        VectorStoreCollection<string, IngestedDocument> documentStore,
        VectorStoreCollection<string, IngestedChunk> chunkStore
    )
    {
        var sourceId = $"PDF:{sourceDirectory}";

        var currentFiles = Directory.GetFiles(sourceDirectory, "*.pdf");

        var existingDocuments = await documentStore
            .GetAsync(doc => doc.SourceId == sourceId, top: int.MaxValue)
            .ToListAsync();

        var deletedDocuments = existingDocuments
            .Where(doc => !currentFiles.Any(f => Path.GetFileName(f) == doc.DocumentId))
            .ToList();

        foreach (var deleted in deletedDocuments)
        {
            Console.WriteLine($"🗑️ Deleting: {deleted.DocumentId}");
            var chunks = await chunkStore
                .GetAsync(c => c.DocumentId == deleted.DocumentId, top: int.MaxValue)
                .ToListAsync();

            if (chunks.Count != 0)
            {
                await chunkStore.DeleteAsync(chunks.Select(c => c.Key));
            }

            await documentStore.DeleteAsync(deleted.Key);
        }

        // ------------------ Ingest New/Updated Files ------------------

        foreach (var file in currentFiles)
        {
            var fileId = Path.GetFileName(file);
            var fileVersion = File.GetLastWriteTimeUtc(file).ToString("o");
            var existingDoc = existingDocuments.FirstOrDefault(d => d.DocumentId == fileId);

            var isModified = existingDoc == null || existingDoc.DocumentVersion != fileVersion;
            if (!isModified)
            {
                continue;
            }

            if (existingDoc != null)
            {
                var oldChunks = await chunkStore
                    .GetAsync(c => c.DocumentId == fileId, top: int.MaxValue)
                    .ToListAsync();

                if (oldChunks.Count != 0)
                {
                    await chunkStore.DeleteAsync(oldChunks.Select(c => c.Key));
                }
            }

            Console.WriteLine($"Ingesting: {fileId}");

            var newDoc = new IngestedDocument
            {
                Key = Guid.NewGuid().ToString(),
                DocumentId = fileId,
                DocumentVersion = fileVersion,
                SourceId = sourceId,
            };

            await documentStore.UpsertAsync(newDoc);

            using var pdf = PdfDocument.Open(file);
            var newChunks = new List<IngestedChunk>();

            foreach (var page in pdf.GetPages())
            {
                var letters = page.Letters;
                var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
                var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
                var fullText = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    blocks.Select(b => b.Text.ReplaceLineEndings(" "))
                );

#pragma warning disable SKEXP0050
                var chunks = TextChunker.SplitPlainTextParagraphs([fullText], 200);
#pragma warning restore SKEXP0050

                foreach (var chunkText in chunks)
                {
                    newChunks.Add(
                        new IngestedChunk
                        {
                            Key = Guid.NewGuid().ToString(),
                            DocumentId = fileId,
                            PageNumber = page.Number,
                            Text = chunkText,
                        }
                    );
                }
            }

            await chunkStore.UpsertAsync(newChunks);
        }

        Console.WriteLine("Ingestion complete.");
    }
}