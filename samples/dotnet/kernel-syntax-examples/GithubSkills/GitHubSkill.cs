// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI.Tokenizers;
using Microsoft.SemanticKernel.KernelExtensions;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SemanticFunctions.Partitioning;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.Web;
namespace GitHubSkills;

/// <summary>
/// Skill for interacting with a GitHub repository.
/// </summary>
public class GitHubSkill
{
    /// <summary>
    /// Parameter names.
    /// <see cref="ContextVariables"/>
    /// </summary>
    public static class Parameters
    {
        /// <summary>
        /// Name of the repository repositoryBranch which will be downloaded and summarized.
        /// </summary>
        public const string RepositoryBranch = "repositoryBranch";

        /// <summary>
        /// The search string to match against the names of files in the repository.
        /// </summary>
        public const string SearchPattern = "searchPattern";

        /// <summary>
        /// Document file path.
        /// </summary>
        public const string FilePath = "filePath";

        /// <summary>
        /// Directory to which to extract compressed file's data.
        /// </summary>
        public const string DestinationDirectoryPath = "destinationDirectoryPath";

        /// <summary>
        /// Name of the memory collection used to store the code summaries.
        /// </summary>
        public const string MemoryCollectionName = "memoryCollectionName";
    }

    /// <summary>
    /// The max tokens to process in a single semantic function call.
    /// </summary>
    private const int MaxTokens = 1024;

    /// <summary>
    /// The max file size to send directly to memory.
    /// </summary>
    private const int MaxFileSize = 2048;

    private readonly ISKFunction _summarizeCodeFunction;
    private readonly IKernel _kernel;
    private readonly WebFileDownloadSkill _downloadSkill;
    private readonly ILogger<GitHubSkill> _logger;
    private readonly string _memoryCollectionName;

    internal const string SummarizeCodeSnippetDefinition =
        @"BEGIN CONTENT TO SUMMARIZE:
{{$INPUT}}
END CONTENT TO SUMMARIZE.

Summarize the content in 'CONTENT TO SUMMARIZE', identifying main points.
Do not incorporate other general knowledge.
Summary is in plain text, in complete sentences, with no markup or tags.

BEGIN SUMMARY:
";

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubSkill"/> class.
    /// </summary>
    /// <param name="kernel">Kernel instance</param>
    /// <param name="downloadSkill">Instance of WebFileDownloadSkill used to download web files</param>
    /// <param name="logger">Optional logger</param>
    /// <param name="GithubCollectionName">Optional Name of the collection</param>
    public GitHubSkill(IKernel kernel, WebFileDownloadSkill downloadSkill, ILogger<GitHubSkill>? logger = null, string GithubCollectionName = Parameters.MemoryCollectionName)
    {
        this._kernel = kernel;
        this._downloadSkill = downloadSkill;
        this._logger = logger ?? NullLogger<GitHubSkill>.Instance;
        this._memoryCollectionName = GithubCollectionName;

        this._summarizeCodeFunction = kernel.CreateSemanticFunction(
            SummarizeCodeSnippetDefinition,
            skillName: nameof(GitHubSkill),
            description: "Given a snippet of code, summarize the part of the file.",
            maxTokens: MaxTokens,
            temperature: 0.1,
            topP: 0.5);
    }

    /// <summary>
    /// Summarize the code downloaded from the specified URI.
    /// </summary>
    /// <param name="source">URI to download the repository content to be summarized</param>
    /// <param name="context">Semantic kernel context</param>
    /// <returns>Task</returns>
    [SKFunction("Downloads a repository and summarizes the content")]
    [SKFunctionName("SummarizeRepository")]
    [SKFunctionInput(Description = "URL of the GitHub repository to summarize")]
    [SKFunctionContextParameter(Name = Parameters.RepositoryBranch,
        Description = "Name of the repository repositoryBranch which will be downloaded and summarized")]
    [SKFunctionContextParameter(Name = Parameters.SearchPattern, Description = "The search string to match against the names of files in the repository")]
    public async Task SummarizeRepositoryAsync(string source, SKContext context)
    {
        Console.WriteLine("[GithubSkill class] Downloading from uri: " + source);
        this._logger.LogDebug("This is a test");

        if (!context.Variables.Get(Parameters.RepositoryBranch, out string repositoryBranch) || string.IsNullOrEmpty(repositoryBranch))
        {
            repositoryBranch = "main";
        }

        if (!context.Variables.Get(Parameters.SearchPattern, out string searchPattern) || string.IsNullOrEmpty(searchPattern))
        {
            searchPattern = "*.md";
        }

        string tempPath = Path.GetTempPath();
        string directoryPath = Path.Combine(tempPath, $"SK-{Guid.NewGuid()}");
        string filePath = Path.Combine(tempPath, $"SK-{Guid.NewGuid()}.zip");

        try
        {
            var repositoryUri = source.Trim(new char[] { ' ', '/' });
            var context1 = new SKContext(new ContextVariables(), NullMemory.Instance, null, context.Log);
            context1.Variables.Set(Parameters.FilePath, filePath);
            await this._downloadSkill.DownloadToFileAsync($"{repositoryUri}/archive/refs/heads/{repositoryBranch}.zip", context1);

            ZipFile.ExtractToDirectory(filePath, directoryPath);

            await this.SummarizeCodeDirectoryAsync(directoryPath, searchPattern, repositoryUri, repositoryBranch, context);

            // This change the MemoryCollectionName. We don't want this
            // context.Variables.Set(Parameters.MemoryCollectionName, $"{repositoryUri}-{repositoryBranch}");
        }
        finally
        {
            // Cleanup downloaded file and also unzipped content
            if (File.Exists(filePath))
            {
                Console.WriteLine("--> Deleting file {0}", filePath);
                File.Delete(filePath);
            }

            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    /// <summary>
    /// Summarize a code file into an embedding
    /// </summary>
    private async Task SummarizeCodeFileAsync(string filePath, string repositoryUri, string repositoryBranch, string fileUri)
    {
        string code = "";
        if (File.Exists(filePath))
        {
            Console.WriteLine("--> File {0} exists.", filePath);
            try
            {
                Console.WriteLine("--> Reading File {0} ....", filePath);
                // Check the following encode
                code = File.ReadAllText(filePath);
                // code = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                // Do something with the contents of the file
                Console.WriteLine("--> content: {0} ...", code.Substring(0, 10));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR!] {0}", ex);
            }
        }
        else
        {
            Console.WriteLine("--> File {0} does not exist!", (filePath));
            // Handle the case where the file doesn't exist or can't be accessed
        }

        if (code != null && code.Length > 0)
        {
            this._logger.LogDebug("...Code file {0} is not NULL but having length {1} > MaxFileSize {2}", filePath, code.Length, MaxFileSize);
            Console.WriteLine("...Code file {0} is not NULL but having length {1} > MaxFileSize {2}", filePath, code.Length, MaxFileSize);
            if (code.Length > MaxFileSize)
            {
                var extension = new FileInfo(filePath).Extension;

                List<string> lines;
                List<string> paragraphs;

                switch (extension)
                {
                    case ".md":
                    {
                        this._logger.LogDebug("...Code file {0} type: .md. Broking down into paragraphs ...", filePath);
                        Console.WriteLine("...Code file {0} type: .md. Broking down into paragraphs ...", filePath);
                        //TODO: If doc is actually Markdown use the following instead
                        lines = SemanticTextPartitioner.SplitMarkDownLines(code, MaxTokens);
                        paragraphs = SemanticTextPartitioner.SplitMarkdownParagraphs(lines, MaxTokens);
                        // lines = SemanticTextPartitioner.SplitPlainTextLines(code, MaxTokens);
                        // paragraphs = SemanticTextPartitioner.SplitPlainTextParagraphs(lines, MaxTokens);
                        foreach (var line in lines)
                        {
                            Console.WriteLine("\n----line length: {0} ", line.Length);
                            Console.WriteLine("\n----line tokens: {0} ", GPT3Tokenizer.Encode(line).Count);
                            Console.WriteLine("Line: {0}", line);
                        }
                        foreach (var paragrah in paragraphs)
                        {
                            Console.WriteLine("\n----Paragraph length: {0} ", paragrah.Length);
                            Console.WriteLine("Paragraph: {0}", paragrah);
                        }

                        break;
                    }
                    default:
                    {
                        this._logger.LogDebug("...Code file {0} type: {1}. Broking down into paragraphs ...", filePath, extension);
                        lines = SemanticTextPartitioner.SplitPlainTextLines(code, MaxTokens);
                        paragraphs = SemanticTextPartitioner.SplitPlainTextParagraphs(lines, MaxTokens);

                        break;
                    }
                }

                for (int i = 0; i < paragraphs.Count; i++)
                {
                    Console.WriteLine("\n----->[GithubSkill] Embedding and saving paragraph that starts with: \n{0} ...", paragraphs[i].Substring(0, 50));
                    Console.WriteLine($"[GithubSkill] collection name: {repositoryUri}-{repositoryBranch}, key id: {fileUri}-{i}");
                    await this._kernel.Memory.SaveInformationAsync(
                        // $"{repositoryUri}-{repositoryBranch}",
                        // Parameters.MemoryCollectionName,
                        this._memoryCollectionName,
                        text: $"{paragraphs[i]} File:{repositoryUri}/blob/{repositoryBranch}/{fileUri}",
                        id: $"{fileUri}_{i}");
                }
            }
            else
            {
                this._logger.LogDebug("...Code file {0} is not NULL and has length {1} < MaxFileSize {2}", filePath, code.Length, MaxFileSize);
                Console.WriteLine("...Code file {0} is not NULL and has length {1} < MaxFileSize {2}", filePath, code.Length, MaxFileSize);
                await this._kernel.Memory.SaveInformationAsync(
                    // $"{repositoryUri}-{repositoryBranch}",
                    // Parameters.MemoryCollectionName,
                    this._memoryCollectionName,
                    text: $"{code} File:{repositoryUri}/blob/{repositoryBranch}/{fileUri}",
                    id: fileUri);
            }
        }
        else
        {
            Console.WriteLine("Code file is null! {0}", (code));
        }
    }

    /// <summary>
    /// Summarize the code found under a directory into embeddings (one per file)
    /// </summary>
    private async Task SummarizeCodeDirectoryAsync(string directoryPath, string searchPattern, string repositoryUri, string repositoryBranch, SKContext context)
    {
        string[] filePaths = await Task.FromResult(Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories));

        if (filePaths != null && filePaths.Length > 0)
        {
            this._logger.LogDebug(" Found {0} files to summarize", filePaths.Length);
            Console.WriteLine("[GithubSkill class] Found {0} files to summarize", filePaths.Length);

            foreach (string filePath in filePaths)
            {
                var fileUri = this.BuildFileUri(directoryPath, filePath, repositoryUri, repositoryBranch);
                Console.WriteLine("[GithubSkill class] Summarizing file {0} ... ", filePath);
                await this.SummarizeCodeFileAsync(filePath, repositoryUri, repositoryBranch, fileUri);
            }
        }
    }

    /// <summary>
    /// Build the file uri corresponding to the file path.
    /// </summary>
    private string BuildFileUri(string directoryPath, string filePath, string repositoryUri, string repositoryBranch)
    {
        var repositoryBranchName = $"{repositoryUri.Trim('/').Substring(repositoryUri.LastIndexOf('/'))}-{repositoryBranch}";
        return filePath.Substring(directoryPath.Length + repositoryBranchName.Length + 1).Replace('\\', '/');
    }
}
