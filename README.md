## 🧠 .NET Console App: Azure AI Integration (Procedural Example)

![dotnet](https://github.com/aherrick/RAGDotNet/actions/workflows/dotnet.yml/badge.svg)

This project contains **procedural C# code** for working with:

- **Azure OpenAI** (via `AzureOpenAIClient`)
- **Azure AI Search** (via `SearchIndexClient`)
- **Embeddings** (via `text-embedding-3-small` deployment)

> 🛠️ **This code is adapted from the official Microsoft [.NET AI Templates Preview 1](https://devblogs.microsoft.com/dotnet/announcing-dotnet-ai-template-preview1/)**, designed to help developers get started with generative AI in a familiar, straightforward way.

### 🔐 Configuration (User Secrets)

To securely configure the endpoints and API keys, this project uses [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).

Example secrets.json structure:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-openai-url-here/",
    "Key": "your-openai-key-here",
    "EmbeddingDeployment": "text-embedding-3-small",
    "ChatDeployment": "gpt-4o-mini"
  },
  "AzureAISearch": {
    "Endpoint": "https://your-search-url-here/",
    "Key": "your-search-key-here"
  }
}
```
