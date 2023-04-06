// Copyright (c) Microsoft. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.SemanticKernel.SkillDefinition;
namespace CustomQASkills;

public class CustomQASkill
{
    public string aggregatedQuestions = "";
    public string aggregatedReplies = "";
    public string aggregatedUserResponses = "";

    [SKFunction("Append a question or a list of questions to memory")]
    [SKFunctionInput(Description = "Questions to add")]
    public void appendQuestions(string input)
    {
        this.aggregatedQuestions += "\n" + input;
    }

    [SKFunction("Append a question or a list of partials replies to memory")]
    [SKFunctionInput(Description = "Questions to append")]
    public void appendReplies(string input)
    {
        this.aggregatedReplies += "\n" + input;
    }

    [SKFunction("Ask user a question and record user's response ")]
    [SKFunctionInput(Description = "Questions to append")]
    public void askUserAQuestion(string questions)
    {
        string[] _questionsList = questions.Split("<\n>");
        List<string> questionsList = _questionsList.ToList();
        Console.WriteLine("List of questions: ");
        foreach (var question in questionsList)
        {
            Console.WriteLine("--> {0}", question);
            Console.WriteLine("--> User: ");
            this.aggregatedUserResponses += "\n" + this._getUserResponse();
            Console.WriteLine("----\n Aggregated responses from user: {0}", this.aggregatedUserResponses);
        }
    }

    internal string _getUserResponse()
    {
        string userInput;
        while (true)
        {
            userInput = Console.ReadLine();

            if (userInput == null || string.IsNullOrEmpty(userInput) || string.IsNullOrEmpty(userInput.Trim()))
            {
                continue; // Skip empty lines
            }
            else
            {
                break;
            }
        }

        return userInput;
    }
}
