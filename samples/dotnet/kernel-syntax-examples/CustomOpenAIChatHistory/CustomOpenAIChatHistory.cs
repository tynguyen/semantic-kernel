// Copyright (c) Microsoft. All rights reserved.
// Copy from OpenAIChatHistory
using System;
using Microsoft.SemanticKernel.AI.ChatCompletion;
namespace CustomOpenAIChatCompletion;

/// <summary>
/// OpenAI Chat content
/// See https://platform.openai.com/docs/guides/chat for details
/// </summary>
public class CustomOpenAIChatHistory : ChatHistory
{
    private const string SystemRole = "system";
    private const string AssistantRole = "assistant";
    private const string UserRole = "user";

    /// <summary>
    /// Create a new and empty chat history
    /// </summary>
    /// <param name="assistantInstructions">Optional instructions for the assistant</param>
    public CustomOpenAIChatHistory(string? assistantInstructions = null)
    {
        if (!string.IsNullOrWhiteSpace(assistantInstructions))
        {
            this.AddSystemMessage(assistantInstructions);
        }
    }

    public CustomOpenAIChatHistory(ChatHistory SourceChatHistory)
    {
        foreach (var message in SourceChatHistory.Messages)
        {
            string AuthorRole = message.AuthorRole;
            this.AddMessage(AuthorRole, message.Content);
        }
    }

    public void displayChatHistory()
    {
        foreach (var message in this.Messages)
        {
            Console.WriteLine("--{0}: {1}", message.AuthorRole, message.Content);
        }

    }
    /// <summary>
    /// Add a system message to the chat history
    /// </summary>
    /// <param name="content">Message content</param>
    public void AddSystemMessage(string content)
    {
        this.AddMessage(SystemRole, content);
    }

    /// <summary>
    /// Add an assistant message to the chat history
    /// </summary>
    /// <param name="content">Message content</param>
    public void AddAssistantMessage(string content)
    {
        this.AddMessage(AssistantRole, content);
    }

    /// <summary>
    /// Add a user message to the chat history
    /// </summary>
    /// <param name="content">Message content</param>
    public void AddUserMessage(string content)
    {
        this.AddMessage(UserRole, content);
    }
}
