﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Memory.Qdrant;
using Microsoft.SemanticKernel.Memory;
using RepoUtils;

// ReSharper disable once InconsistentNaming
public static class Example19_Qdrant
{
    private const string MemoryCollectionName = "qdrant-test";

    public static async Task RunAsync()
    {
        int qdrantPort = int.Parse("6333", CultureInfo.InvariantCulture);
        QdrantMemoryStore memoryStore = new QdrantMemoryStore("http://localhost", qdrantPort, vectorSize: 1536, ConsoleLogger.Log);
        IKernel kernel = Kernel.Builder
            .WithLogger(ConsoleLogger.Log)
            .Configure(c =>
            {
                c.AddOpenAITextCompletionService("davinci", "text-davinci-003", "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");
                c.AddOpenAIEmbeddingGenerationService("ada", "text-embedding-ada-002", "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");
            })
            .WithMemoryStorage(memoryStore)
            .Build();

        Console.WriteLine("== Printing Collections in DB ==");
        var collections = memoryStore.GetCollectionsAsync();
        await foreach (var collection in collections)
        {
            Console.WriteLine(collection);
        }

        // Console.WriteLine("== Adding Memories ==");

        // await kernel.Memory.SaveInformationAsync(MemoryCollectionName, id: "cat1", text: "british short hair");
        // await kernel.Memory.SaveInformationAsync(MemoryCollectionName, id: "cat2", text: "orange tabby");
        // await kernel.Memory.SaveInformationAsync(MemoryCollectionName, id: "cat3", text: "norwegian forest cat");

        Console.WriteLine("== Printing Collections in DB ==");
        collections = memoryStore.GetCollectionsAsync();
        await foreach (var collection in collections)
        {
            Console.WriteLine(collection);
        }

        Console.WriteLine("== Retrieving Memories ==");
        MemoryQueryResult? lookup = await kernel.Memory.GetAsync(MemoryCollectionName, "cat1");
        Console.WriteLine(lookup != null ? lookup.Metadata.Text : "ERROR: memory not found");

        Console.WriteLine("== Similarity Searching Memories: My favorite color is orange ==");
        var searchResults = kernel.Memory.SearchAsync(MemoryCollectionName, "My favorite color is orange", limit: 3, minRelevanceScore: 0.8);

        await foreach (var item in searchResults)
        {
            Console.WriteLine(item.Metadata.Text + " : " + item.Relevance);
        }

        // Console.WriteLine("== Removing Collection {0} ==", MemoryCollectionName);
        // await memoryStore.DeleteCollectionAsync(MemoryCollectionName);

        // Console.WriteLine("== Printing Collections in DB ==");
        // await foreach (var collection in collections)
        // {
        //     Console.WriteLine(collection);
        // }
    }
}
