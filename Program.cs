using System.ComponentModel;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.AzureAISearch;
using RAGDotNet.Models;
using RAGDotNet.Services;

var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

var azureOpenAI = new AzureOpenAIClient(
    new Uri(
        config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("Missing AzureOpenAI endpoint")
    ),
    new AzureKeyCredential(
        config["AzureOpenAI:Key"] ?? throw new InvalidOperationException("Missing AzureOpenAI key")
    )
);

var embeddingClient = azureOpenAI
    .GetEmbeddingClient(config["AzureOpenAI:EmbeddingDeployment"])
    .AsIEmbeddingGenerator();

var searchIndexClient = new SearchIndexClient(
    new Uri(
        config["AzureAISearch:Endpoint"]
            ?? throw new InvalidOperationException("Missing AzureAISearch endpoint")
    ),
    new AzureKeyCredential(
        config["AzureAISearch:Key"]
            ?? throw new InvalidOperationException("Missing AzureAISearch key")
    )
);

var vectorStore = new AzureAISearchVectorStore(
    searchIndexClient,
    new AzureAISearchVectorStoreOptions() { EmbeddingGenerator = embeddingClient }
);

var collectionChunk = vectorStore.GetCollection<string, IngestedChunk>(
    nameof(IngestedChunk).ToLowerInvariant()
);
await collectionChunk.EnsureCollectionExistsAsync();

var collectionDocument = vectorStore.GetCollection<string, IngestedDocument>(
    nameof(IngestedDocument).ToLowerInvariant()
);
await collectionDocument.EnsureCollectionExistsAsync();

await DataIngestor.Ingest(
    Path.Combine(Directory.GetCurrentDirectory(), "docs"),
    collectionDocument,
    collectionChunk
);

Console.WriteLine("Mock data loaded. You can now chat with the data.");
Console.WriteLine("Type 'exit' to quit.\n");

string SystemPrompt =
    @"
        You are an assistant who answers questions about information you retrieve.
        Do not answer questions about anything else.
        Use only simple markdown to format your responses.

        Use the search tool to find relevant information. When you do this, end your
        reply with citations in the special XML format:

        <citation filename='string' page_number='number'>exact quote here</citation>

        Always include the citation in your response if there are results.

        The quote must be max 5 words, taken word-for-word from the search result, and is the basis for why the citation is relevant.
        Don't refer to the presence of citations; just emit these tags right at the end, with no surrounding text.
        ";

ChatOptions chatOptions = new() { Tools = [AIFunctionFactory.Create(SearchAsync)] };

List<ChatMessage> messages = [];
messages.Add(new(ChatRole.System, SystemPrompt));

var chatClient = azureOpenAI
    .GetChatClient("gpt-4o-mini")
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

while (true)
{
    Console.WriteLine();
    Console.WriteLine("──────────────────────────────────────────────");
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;

    messages.Add(new ChatMessage(ChatRole.User, input));

    var responseText = new TextContent(string.Empty);
    var currentResponseMessage = new ChatMessage(ChatRole.Assistant, [responseText]);

    Console.WriteLine();

    Console.Write("AI: ");

    await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
    {
        //messages.AddMessages(update, filter: c => c is not TextContent);
        responseText.Text += update.Text;

        Console.Write(update.Text);
    }

    Console.WriteLine(); // newline after AI response
    Console.WriteLine("──────────────────────────────────────────────");

    messages.Add(currentResponseMessage);
}

[Description("Searches for information using a phrase or keyword")]
async Task<IEnumerable<string>> SearchAsync(
    [Description("The phrase to search for.")] string searchPhrase,
    [Description(
        "If possible, specify the filename to search that file only. If not provided or empty, the search includes all files."
    )]
        string filenameFilter = null
)
{
    var results = await collectionChunk
        .SearchAsync(
            searchPhrase,
            top: 5,
            new VectorSearchOptions<IngestedChunk>
            {
                // Filter = documentIdFilter is { Length: > 0 } ? record => record.DocumentId == documentIdFilter : null,
            }
        )
        .ToListAsync();

    return results.Select(result =>
        $"<result filename=\"{result.Record.DocumentId}\" page_number=\"{result.Record.PageNumber}\">{result.Record.Text}</result>"
    );
}