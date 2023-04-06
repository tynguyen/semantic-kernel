// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CustomOpenAIChatCompletion;
using CustomQASkills;
using GitHubSkills;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Memory.Qdrant;
using Microsoft.SemanticKernel.Connectors.OpenAI.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI.Tokenizers;
using Microsoft.SemanticKernel.CoreSkills;
using Microsoft.SemanticKernel.KernelExtensions;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SemanticFunctions.Partitioning;
using Microsoft.SemanticKernel.Skills;
using Microsoft.SemanticKernel.Skills.Web;
using RepoUtils;

// Console.WriteLine("Paragraphs lengths: {0} ");


// ReSharper disable once InconsistentNaming
public class Example30_github_QA
{
    private IKernel _Kernel;
    private string _PersonalMemoryCollectionName;
    private string _GithubCollectionName;
    private int _QdrantPort;
    private string _QdrantEndPoint;
    private QdrantMemoryStore _MemoryStore;
    private Microsoft.SemanticKernel.CoreSkills.TextMemorySkill _TextMemorySkill = new TextMemorySkill();
    private IChatCompletion? _ScanningService; // = kernel.GetService<IChatCompletion>();
    private OpenAIChatHistory? _ScanningHistory;
    private const int MaxTokensForRetrievedText = 1024; // Max tokens for each retrieved text
    private const string _ChatPrompt = @"
            ChatBot can have a conversation with you about any topic.
            USE ONLY FACTS below, DO NOT COME UP WITH FACTS.
            It can give explicit instructions or say 'I Don't know'.

            FACTS:
            - {{GithubTextmemoryskill.recall $userInput}}

            User: {{$userInput}}
            ChatBot: ";

    private const string _ExtractQuestionPrompt = @"
            Bạn là một nhà tư vấn bảo hiểm xã hội chuyên nghiệp có thể trả lời các câu hỏi của tôi một cách ngắn gọn và thân thiện.
            Câu hỏi của tôi sẽ có dạng:
            {FACTS} - các thông tin tôi đưa ra \n
            {REPLIES} - các suy luận hiện tại \n
            {QUESTION} câu hỏi.
            Go through {REPLIES} line by line and list out information queries needs to answer {QUESTION}. The information queries should be given in a list with
            the following format
            <query> query  </query> \n
            ";

    private const string _ScanningSystemMessage =
            @"Bạn là một nhà tư vấn bảo hiểm xã hội chuyên nghiệp có thể trả lời các câu hỏi của tôi một cách ngắn gọn và thân thiện.
            Câu hỏi tôi sẽ có dạng:
            {FACTS} - các quy định và thông tin được lấy từ nguồn tài liệu\n
            {QUESTION} câu hỏi. Đầu tiên, phân tích {QUESTION} xem câu trả lời cho {QUESTION} gồm những đề mục nào. Ghi nhớ những đề mục này.
            Tóm tắt lại nội dung của {FACTS} theo format sau:
            """"
            <category> tên của đề mục </category> \n
            nội dung
            """"
            ";

    // Buoc 1: scan noi dung cua cau hoi
    // Buoc 2: scan document xem cac phan nao lien he voi noi dug cau hoi
    // Buoc 3: neu noi dung yeu cau ko co san trong question / informatin -> hoi user
    private ISKFunction _ChatFunction;

    private SKContext _GlobalContext;

    public Example30_github_QA(
        string PersonalMemoryCollectionName = "personalCollectionName",
        string GithubCollectionName = "github-collection",
        string QdrantPort = "6333",
        string QdrantEndPoint = "http://localhost"
        )
    {
        this._PersonalMemoryCollectionName = PersonalMemoryCollectionName;
        this._GithubCollectionName = GithubCollectionName;
        this._QdrantEndPoint = QdrantEndPoint;
        this._QdrantPort = int.Parse(QdrantPort, CultureInfo.InvariantCulture);
        this._MemoryStore = new QdrantMemoryStore(this._QdrantEndPoint, this._QdrantPort, vectorSize: 1536, ConsoleLogger.Log);
        this._Kernel = Kernel.Builder
            .WithLogger(ConsoleLogger.Log)
            .Configure(c =>
            {
                c.AddOpenAITextCompletionService("davinci", "text-davinci-003", "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");
                c.AddOpenAIEmbeddingGenerationService("ada", "text-embedding-ada-002", "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");
                c.AddOpenAIChatCompletionService("chat", "gpt-3.5-turbo", "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");

            })
            .WithMemoryStorage(this._MemoryStore)
            .Build();

        this._GlobalContext = this._Kernel.CreateNewContext();
        this._ChatFunction = this._Kernel.CreateSemanticFunction(_ChatPrompt, maxTokens: 200, temperature: 0.0);

    }

    public async Task testExtractQuestions()
    {
        string aggregatedReplies = @"Bạn cần đăng ký tham gia bảo hiểm xã hội và đóng đầy đủ các khoản bảo hiểm xã hội theo quy định để được hưởng trợ cấp thai sản. Sau đó, khi có thai, bạn cần nộp đơn đăng ký nhận trợ cấp thai sản tại cơ quan bảo hiểm xã hội địa phương và cung cấp đầy đủ các giấy tờ liên quan.
            Bạn cần đóng đủ 06 tháng bảo hiểm xã hội trước khi sinh hoặc nhận nuôi con nuôi. Sau đó, bạn có thể nộp hồ sơ đăng ký nhận trợ cấp thai sản tại cơ quan bảo hiểm xã hội nơi bạn đang đóng bảo hiểm.
            Tôi cần biết thêm thông tin về tình trạng của bạn và quy định của địa phương bạn đang sinh sống để trả lời câu hỏi này.
            Bạn cần đăng ký và nộp hồ sơ xin trợ cấp thai sản tại cơ quan Bảo hiểm xã hội địa phương trước khi nghỉ hưu trí hoặc trước khi hết thời hạn quy định tại khoản 1 Điều 34 của Luật Bảo hiểm xã hội. Sau đó, bạn cần tuân thủ các quy định về thời gian nghỉ dưỡng sức, phục hồi sức khỏe sau sinh để được hưởng trợ cấp thai sản.
            Tôi cần thêm thông tin về tình trạng của bạn và quy định của địa phương bạn đang sinh sống để trả lời câu hỏi của bạn. Bạn nên liên hệ với cơ quan bảo hiểm xã hội địa phương để biết thêm chi tiết.
            Tôi cần biết thêm thông tin về quy định của quốc gia bạn đang sống để trả lời câu hỏi này chính xác hơn. Bạn có thể cung cấp thêm thông tin về quốc gia của mình không?
            Bạn cần đáp ứng một trong các điều kiện sau để được hưởng trợ cấp thai sản:
            a) Là lao động nữ mang thai;
            b) Là lao động nữ sinh con;
            c) Là lao động nữ mang thai hộ và người mẹ nhỏ mang thai hộ;
            d) Nhận nuôi con dưới 06 tháng tuổi;
            đ) Là lao động nữ đặt vòng tránh thai, người lao động thực hiện biện pháp triệt sản;
            e) Là lao động nam đang đóng bảo hiểm xã hội có vợ sinh con. Sau đó, bạn cần nộp đơn xin hưởng trợ cấp thai sản tại cơ quan bảo hiểm xã hội.
            Tôi không chắc lắm nhưng tôi nghĩ bạn cần phải đóng bảo hiểm xã hội tối thiểu 06 tháng trở lên và đáp ứng các điều kiện khác theo quy định của pháp luật để được hưởng trợ cấp thai sản từ Bảo hiểm xã hội. Bạn nên liên hệ với cơ quan Bảo hiểm xã hội để biết thêm chi tiết.
            Tôi cần biết thêm thông tin về tình trạng của bạn và quy định của địa phương bạn đang sinh sống để trả lời câu hỏi của bạn.
            Tôi cần biết thêm thông tin về quy định về trợ cấp thai sản của địa phương hoặc nơi làm việc của bạn để trả lời chính xác hơn.
            Tôi không chắc lắm nhưng tôi nghĩ bạn cần đăng ký tham gia bảo hiểm xã hội và đóng đầy đủ các khoản bảo hiểm xã hội để được hưởng trợ cấp thai sản. Bạn nên liên hệ với cơ quan bảo hiểm xã hội để biết thêm chi tiết và thủ tục cụ thể.
            Tôi nghĩ là bạn cần có giấy chứng nhận nghỉ việc hưởng trợ cấp thai sản và giấy chứng nhận nghỉ việc hưởng trợ cấp thai sản của người lao động do người sử dụng lao động lập. Bạn cũng cần có giấy chứng nhận nghỉ việc hưởng trợ cấp thai sản của cơ sở khám bệnh nếu bạn đi khám ngoại trú hoặc giấy chứng nhận nghỉ việc hưởng trợ cấp thai sản của bệnh viện nếu bạn điều trị nội trú.
            Tôi cần biết thêm thông tin về tình trạng của bạn và quy định của cơ quan bảo hiểm xã hội tại địa phương của bạn để trả lời câu hỏi này. Bạn có thể liên hệ với cơ quan bảo hiểm xã hội để được hướng dẫn cụ thể.
            Tôi nghĩ là bạn cần nộp hồ sơ tại cơ quan bảo hiểm xã hội trong thời hạn 10 ngày kể từ ngày nhận đủ hồ sơ theo quy định tại Điều 100 và Điều 101 của Luật Bảo hiểm xã hội. Sau đó, cơ quan bảo hiểm xã hội sẽ giải quyết và tổ chức chi trả cho bạn trong thời hạn 10 ngày kể từ ngày nhận đủ hồ sơ. Nếu bạn thôi việc trước thời điểm sinh con hoặc nhận nuôi con nuôi, thì cơ quan bảo hiểm xã hội sẽ giải quyết và tổ chức chi trả cho bạn trong thời hạn 5 ngày làm việc kể từ ngày nhận đủ hồ sơ.
            Tôi cần tham gia bảo hiểm xã hội và đáp ứng các điều kiện về thời gian đóng bảo hiểm và thời gian nghỉ thai sản theo quy định của pháp luật. Sau đó, tôi cần lập danh sách và nộp cho cơ quan bảo hiểm xã hội trong thời hạn 10 ngày kể từ ngày nghỉ làm. Cơ quan bảo hiểm xã hội sẽ giải quyết và tổ chức chi trả trợ cấp thai sản cho tôi.
            Tôi cần thêm thông tin về tình trạng của bạn và các điều kiện để được hưởng trợ cấp thai sản. Bạn có sổ bảo hiểm xã hội và đã đóng đủ thời gian đóng bảo hiểm chưa?
            REQUESTS: Bạn cần cung cấp thêm thông tin về tình trạng của bạn, bạn có phải là người lao động bị tai nạn lao động hoặc bệnh nghề nghiệp hay không? Bạn đã đóng bảo hiểm xã hội đầy đủ chưa?
            REQUESTS: Bạn cần cung cấp thêm thông tin về tình trạng của bạn và tình trạng công việc hiện tại của bạn. Bạn có đang làm việc và đó là công việc chính thức hay không? Bạn đã đóng bảo hiểm xã hội bao lâu?
            REQUESTS: Bạn vui lòng cung cấp thêm thông tin về trợ cấp thai sản mà bạn muốn hỏi. Vì thông tin trong FACTS của bạn không liên quan đến trợ cấp thai sản.
            Tôi xin lỗi, nhưng thông tin trong FACTS không liên quan đến trợ cấp thai sản. Bạn có thể cung cấp thêm thông tin để tôi có thể trả lời câu hỏi của bạn chính xác hơn không?
            Tôi không chắc lắm nhưng tôi nghĩ bạn cần liên hệ với cơ quan bảo hiểm xã hội để biết thêm thông tin về điều kiện và thủ tục nhận trợ cấp thai sản.
            REQUESTS: Bạn cần cung cấp thêm thông tin về tình trạng của bạn, bạn có đang làm việc hay không, đã đóng bảo hiểm xã hội chưa và ở độ tuổi nào.
            Tôi không chắc lắm nhưng tôi nghĩ bạn cần đăng ký tham gia BHXH và đóng đầy đủ các khoản bảo hiểm xã hội liên quan đến thai sản. Sau đó, khi nghỉ việc hưởng thai sản, bạn cần nộp đơn xin trợ cấp thai sản và cung cấp đầy đủ giấy tờ liên quan theo quy định của pháp luật.
            Tôi nghĩ là bạn cần tham gia bảo hiểm xã hội và đáp ứng các điều kiện quy định tại khoản 1 Điều 31 của Luật Bảo hiểm xã hội để được hưởng trợ cấp thai sản. Nếu mẹ tham gia bảo hiểm xã hội nhưng không đủ điều kiện tại khoản 2 hoặc khoản 3 Điều 31 thì cha hoặc người trực tiếp nuôi dưỡng sẽ được hưởng trợ cấp thai sản. Sau khi sinh con, bạn cần nộp đơn và các giấy tờ liên quan đến trợ cấp thai sản tại cơ quan bảo hiểm xã hội để được giải quyết.
            Tôi cần biết thêm thông tin về tình huống của bạn để có thể trả lời chính xác hơn. Bạn có thể cung cấp thêm thông tin chi tiết về tình huống của bạn không?
            Bạn cần đến khám thai và được chẩn đoán là mang thai hộ hoặc mẹ nhận mang thai hộ để được hưởng trợ cấp thai sản theo quy định của Luật Bảo hiểm xã hội. Sau đó, bạn cần nộp đơn và các giấy tờ liên quan tại cơ quan bảo hiểm xã hội để được giải quyết hồ sơ.
            Bạn cần đăng ký tham gia bảo hiểm xã hội và đáp ứng các điều kiện hưởng trợ cấp thai sản theo quy định của pháp luật. Bạn có thể liên hệ với cơ quan bảo hiểm xã hội để biết thêm thông tin chi tiết và hướng dẫn thủ tục đăng ký.
            Tôi không chắc lắm nhưng tôi nghĩ bạn cần đến cơ quan bảo hiểm xã hội để đăng ký và nộp đầy đủ giấy tờ liên quan để được hưởng trợ cấp thai sản.";

        string userInput = "Tôi cần làm gì để nhận trợ cấp thai sản?";

        int aggregatedRepliesTokens = GPT3Tokenizer.Encode(aggregatedReplies).Count;
        // Hardcode for now
        if (aggregatedRepliesTokens > 3000)
        {
            Console.WriteLine("Aggregated replies {0} > Max tokens {1}", aggregatedRepliesTokens, 3000);
            List<string> lines;
            List<string> paragraphs;
            // Split retrieved text
            //TODO: the following is for Markdown. For others, use different approach (SplitPlainTextLines ...)
            lines = SemanticTextPartitioner.SplitMarkDownLines(aggregatedReplies, MaxTokensForRetrievedText);
            paragraphs = SemanticTextPartitioner.SplitMarkdownParagraphs(lines, MaxTokensForRetrievedText);

            // Iteratively put it through chatGPT
            string _aggregatedReplies = "";
            for (int i = 0; i < paragraphs.Count; i++)
            {
                // Reset auxiliary ChatGPT history
                OpenAIChatHistory __auxChatGPTHistory = await this.createChatGPTHistory(_ExtractQuestionPrompt);
                string _partialUserPrompt = "{REPLIES}: " + (paragraphs[i] != null ? paragraphs[i].ToString() : "") + "{QUESTION} " + userInput;
                int _partialUserPromptTokens = GPT3Tokenizer.Encode(_partialUserPrompt).Count;
                Console.WriteLine("-->\nPartialUserPrompt Tokens: {0}", _partialUserPromptTokens);
                Console.WriteLine("[i = {0} / {1}], PartialUserPrompt: \n{2}", i, paragraphs.Count, _partialUserPrompt);
                __auxChatGPTHistory.AddUserMessage(_partialUserPrompt);
                string _partialReply = await this._ScanningService.GenerateMessageAsync(__auxChatGPTHistory, new ChatRequestSettings());
                _aggregatedReplies += "\n" + _partialReply;
                Console.WriteLine("[i = {0}], Partial Reply: \n{1}", i, _partialReply);
            }

            Console.WriteLine("----\nAll Aggregated Reply: \n{0}", _aggregatedReplies);

        }
        else
        {
            OpenAIChatHistory _extractQuestionHistory = await this.createChatGPTHistory(_ExtractQuestionPrompt);
            string _userPrompt = "{REPLIES}: " + aggregatedReplies + "\n" + "{QUESTION}" + userInput;
            _extractQuestionHistory.AddUserMessage(_userPrompt);
            ChatRequestSettings _chatRequestSettings = new ChatRequestSettings();
            _chatRequestSettings.MaxTokens = 2048;
            string reply = await this._ScanningService.GenerateMessageAsync(_extractQuestionHistory, _chatRequestSettings);
            Console.WriteLine("List of questions: \n{0}", reply);
        }


    }
    public async Task RunAsync()
    {
        // Register skills
        await this.registerSkills();

        // Summarize the github
        await this.summarizeGithubRepo();

        Console.WriteLine("== Printing Collections in DB ==");
        var collections = this._MemoryStore.GetCollectionsAsync();
        await foreach (var collection in collections)
        {
            Console.WriteLine(collection);
        }

        Console.WriteLine("== Printing Collections in DB ==");
        collections = this._MemoryStore.GetCollectionsAsync();
        await foreach (var collection in collections)
        {
            Console.WriteLine(collection);
        }

        // Test handle a question
        await this.handleQuestion();

        // Test extract questions
        // await this.testExtractQuestions();

    }

    public async Task registerSkills()
    {
        // ChatGPT service
        this._ScanningService = this._Kernel.GetService<IChatCompletion>();

        // string question = "Đối tượng nào phải đóng bảo hiểm xã hội bắt buộc?";
        this._Kernel.ImportSkill(this._TextMemorySkill, "GithubTextMemorySkill");
        this._GlobalContext[TextMemorySkill.CollectionParam] = this._GithubCollectionName;
        this._GlobalContext[TextMemorySkill.RelevanceParam] = "0.8";
        //TODO: Set this number according to the requirement
        this._GlobalContext[TextMemorySkill.LimitParam] = "5";


        // ChatGPT
        this._ScanningHistory = await this.createChatGPTHistory();

        // ChatGPT
        // this._ExtractQuestionHistory = await this.createChatGPTHistory(_ExtractQuestionPrompt);


        // QA skills
        var QASkill = this._Kernel.ImportSkill(new CustomQASkill(), "QASkill");



        // // Test registration
        // string question = "Luật này là luật gì?";
        // this._GlobalContext["userInput"] = question;
        // string answer = this._TextMemorySkill.Recall(question, this._GlobalContext);

        // // Process the user message and get an answer
        // Console.WriteLine("User: {0} \nRaw answer from Bot: {1}", question, answer);


        // Summarize the answer using `SummarizeSill`
        // this._SummarizeSkill = this._Kernel.ImportSemanticSkillFromDirectory(RepoFiles.SampleSkillsPath(), "SummarizeSkill")["Summarize"];
        // this._GlobalContext["INPUT"] = answer;

        // var shortenAnswer = await this._SummarizeSkill.InvokeAsync(this._GlobalContext);
        // Console.WriteLine("=====\nUser: {0} \nShorten answer from Bot: {1}", question, shortenAnswer);


        // Summarize using ChatGPT skill

        // summarizeContext.Set("input", answer);
        // string shortenAnswer = await this._Kernel.RunAsync(this._GlobalContext, myJokeFunction);// summarizeSkill["Summarize"]);
        // // Given a history
        // var history = "";
        // context["history"] = history;
        // async Task Chat(string input)
        // {
        //     // Save new message in the context variables
        //     context["userInput"] = input;

        //     // Process the user message and get an answer
        //     var answer = await chatFunction.InvokeAsync(context);

        //     // Append the new interaction to the chat history
        //     history += $"\nUser: {input}\nChatBot: {answer}\n"; context["history"] = history;

        //     // Show the bot response
        //     Console.WriteLine("ChatBot: " + context);
        // }

        // string UserInput = "";

        // while (true)
        // {
        //     Console.Write("User:");
        //     try
        //     {
        //         UserInput = Console.ReadLine();
        //     }
        //     catch (InvalidOperationException ex) when ((ex.HResult & 0xFFFF) == 0x80131509) // Handle control+C
        //     {
        //         break;
        //     }

        //     if (UserInput == null || string.IsNullOrEmpty(UserInput) || string.IsNullOrEmpty(UserInput.Trim()))
        //     {
        //         continue; // Skip empty lines
        //     }

        //     Console.WriteLine("User input was: " + UserInput);

        //     await Chat(UserInput);
        // }
    }

    public async Task<OpenAIChatHistory> createChatGPTHistory(
        string systemMessage = _ScanningSystemMessage
            )
    {
        if (this._ScanningService == null)
        {
            await this.registerSkills();
        }

        return (OpenAIChatHistory)this._ScanningService.CreateNewChat(systemMessage);
    }

    public async Task ChatLoop()
    {
        string? UserInput = "";
        while (true)
        {
            Console.Write("User:");
            try
            {
                UserInput = Console.ReadLine();
            }
            catch (InvalidOperationException ex) when ((ex.HResult & 0xFFFFL) == 0x80131509L) // Handle control+C
            {
                break;
            }

            if (UserInput == null || string.IsNullOrEmpty(UserInput) || string.IsNullOrEmpty(UserInput.Trim()))
            {
                continue; // Skip empty lines
            }

            Console.WriteLine("User input was: " + UserInput);

            await this.handleQuestion(UserInput);
        }

    }

    public async Task handleQuestion(string question = "")
    {
        // this._GlobalContext["history"] = this._ChatHistory;
        // this._GlobalContext["userInput"] = question;

        // // Process the user message and get an answer
        // var answer = await this._ChatFunction.InvokeAsync(this._GlobalContext);

        // // Append the new interaction to the chat history
        // this._ChatHistory += $"\nUser: {question}\nChatBot: {answer}\n";
        // this._GlobalContext["history"] = this._ChatHistory;

        // // Show the bot response
        // Console.WriteLine("ChatBot: " + this._GlobalContext);

        string UserInput = "Tôi cần làm gì để nhận trợ cấp thai sản?";

        // Retrieving text
        this._GlobalContext["userInput"] = UserInput; // This line is not necessary
        string RetrievedText = this._TextMemorySkill.Recall(UserInput, this._GlobalContext);
        if (RetrievedText.Length == 0)
        {
            Console.WriteLine("No text is found! Needs to seek for information else where!");
            // TODO: ask user to find information from internet
        }
        string reply = "";
        string UserPrompt = "QUESTION: " + (UserInput != null ? UserInput : "");
        string _agregatedReplies = "";


        // Iteratively feed the retrieved texts to ChatGPT
        Console.OutputEncoding = Encoding.UTF8;
        int RetrievedTokens = GPT3Tokenizer.Encode(RetrievedText).Count;
        if (RetrievedTokens > MaxTokensForRetrievedText)
        {

            Console.WriteLine("Retrieved Text length {0} > Max tokens {1}", RetrievedTokens, MaxTokensForRetrievedText);
            List<string> lines;
            List<string> paragraphs;
            // Split retrieved text
            //TODO: the following is for Markdown. For others, use different approach (SplitPlainTextLines ...)
            lines = SemanticTextPartitioner.SplitMarkDownLines(RetrievedText, MaxTokensForRetrievedText);
            paragraphs = SemanticTextPartitioner.SplitMarkdownParagraphs(lines, MaxTokensForRetrievedText / 2);

            // Iteratively put it through chatGPT
            for (int i = 0; i < paragraphs.Count; i++)
            {
                // Reset auxiliary ChatGPT history
                CustomOpenAIChatHistory __auxChatGPTHistory = new CustomOpenAIChatHistory(this._ScanningHistory);
                // Console.WriteLine("AuxChatHistory: ");
                // __auxChatGPTHistory.displayChatHistory();

                string _partialUserPrompt = "FACTS: " + (paragraphs[i] != null ? paragraphs[i].ToString() : "") + UserPrompt;
                int _partialUserPromptTokens = GPT3Tokenizer.Encode(_partialUserPrompt).Count;
                Console.WriteLine("-->\nPartialUserPrompt Tokens: {0}", _partialUserPromptTokens);
                Console.WriteLine("[i = {0} / {1}], PartialUserPrompt: \n{2}", i, paragraphs.Count, _partialUserPrompt);
                __auxChatGPTHistory.AddUserMessage(_partialUserPrompt);
                reply = await this._ScanningService.GenerateMessageAsync(__auxChatGPTHistory, new ChatRequestSettings());
                _agregatedReplies += "\n" + reply;
                Console.WriteLine("[i = {0}], Partial Reply: \n{1}", i, reply);
            }

            Console.WriteLine("----\nAll Aggregated Reply: \n{0}", _agregatedReplies);

            // Aggregate Partial Replies
            string _aggregatedUserPrompt = "FACTS: " + (_agregatedReplies != null ? _agregatedReplies : "") + UserPrompt;
            int _aggregatedUserPromptTokens = GPT3Tokenizer.Encode(_aggregatedUserPrompt).Count;
            Console.WriteLine("--> AggregattedUserPrompt Tokens: {0}", _aggregatedUserPromptTokens);
            // Create an auxiliary ChatGPT history
            CustomOpenAIChatHistory _auxChatGPTHistory = new CustomOpenAIChatHistory(this._ScanningHistory);
            // Console.WriteLine("AuxChatHistory: ");
            // _auxChatGPTHistory.displayChatHistory();
            _auxChatGPTHistory.AddUserMessage(_aggregatedUserPrompt);
            ChatRequestSettings _chatRequestSettings = new ChatRequestSettings();
            _chatRequestSettings.MaxTokens = 1024;
            reply = await this._ScanningService.GenerateMessageAsync(_auxChatGPTHistory, _chatRequestSettings);
            Console.WriteLine("Aggregated Reply: \n{0}", reply);

        }
        else
        {
            UserPrompt = "FACTS: " + (RetrievedText != null ? RetrievedText : "") + "QUESTION: " + (UserInput != null ? UserInput : "");
            ChatRequestSettings _chatRequestSettings = new ChatRequestSettings();
            _chatRequestSettings.MaxTokens = 1024;
            reply = await this._ScanningService.GenerateMessageAsync(this._ScanningHistory, _chatRequestSettings);
            Console.WriteLine("Reply: \n{0}", reply);
        }


        this._ScanningHistory.AddUserMessage(UserInput);
        this._ScanningHistory.AddAssistantMessage(reply);
        Console.WriteLine("User: {0}\nBot: {1}", UserInput, reply);


        // // Process the user message and get an answer
        // Console.WriteLine("User: {0} \nRaw answer from Bot: {1}", question, answer);


        // Summarize the answer using `SummarizeSill`
        // this._SummarizeSkill = this._Kernel.ImportSemanticSkillFromDirectory(RepoFiles.SampleSkillsPath(), "SummarizeSkill")["Summarize"];
        // this._GlobalContext["INPUT"] = answer;

        // var shortenAnswer = await this._SummarizeSkill.InvokeAsync(this._GlobalContext);
        // Console.WriteLine("=====\nUser: {0} \nShorten answer from Bot: {1}", question, shortenAnswer);


        // Summarize using ChatGPT skill

    }


    public async Task deleteMemory(string MemoryCollectionName = "")
    {
        Console.WriteLine("== Removing Collection {0} ==", MemoryCollectionName);
        await this._MemoryStore.DeleteCollectionAsync(MemoryCollectionName);

        var collections = this._MemoryStore.GetCollectionsAsync();
        Console.WriteLine("== Printing Collections in DB after deleting {0} ==", MemoryCollectionName);
        await foreach (var collection in collections)
        {
            Console.WriteLine(collection);
        }
    }

    public async Task summarizeGithubRepo(
        string repoBranch = "test",
        string repoURI = "https://github.com/tynguyen/public_vietnamese_laws_regulations",
        string searchPatten = "*.md",
        string destinationDirectoryPath = "downloadedFiles"
    )
    {
        // Check if the information colleciton has existed. If not, start  summarizing. Otherwise, break
        var collections = this._MemoryStore.GetCollectionsAsync();
        Console.WriteLine("== Printing Collections in DB ==");
        await foreach (var collection in collections)
        {
            Console.WriteLine(collection);
            if (collection == this._GithubCollectionName)
            {
                Console.WriteLine("[Note!] Collection {0} has existed in the database. Do not continue the summariztion! Breaking...", collection);
                return;
            }
        }
        // Test saving infor to memory
        WebFileDownloadSkill downloadSkill = new WebFileDownloadSkill();
        var githubSkill = this._Kernel.ImportSkill(new GitHubSkill(this._Kernel, downloadSkill, GithubCollectionName: this._GithubCollectionName), "GithubSkill");

        var myContext = new ContextVariables();
        myContext.Set("INPUT", repoURI);
        myContext.Set("repositoryBranch", repoBranch);
        myContext.Set("searchPattern", searchPatten);
        myContext.Set("destinationDirectoryPath", destinationDirectoryPath);
        myContext.Set("memoryCollectionName", this._GithubCollectionName);

        await this._Kernel.RunAsync(myContext, githubSkill["SummarizeRepository"]);

        // Finish summarization process. Check collections
        collections = this._MemoryStore.GetCollectionsAsync();
        Console.WriteLine("== Printing Collections in DB after the summarization ==");
        await foreach (var collection in collections)
        {
            Console.WriteLine(collection);
        }
    }

    public async Task summarizeGithubRepo(string repoBranch, Uri repoURI, string searchPatten, string destinationDirectoryPath)
    {
        throw new NotImplementedException();
    }
}
