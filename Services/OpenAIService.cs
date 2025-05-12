using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IqTest_server.Models;
using IqTest_server.DTOs.Test;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Schema.Generation;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json;
using System.Linq;

namespace IqTest_server.Services
{
    // Class to hold both question data and correct answers
    public class QuestionWithAnswer
    {
        public QuestionDto Question { get; set; }
        public string CorrectAnswer { get; set; }
    }

    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OpenAIService> _logger;
        private readonly string _apiKey;
        private readonly string _apiUrl = "https://api.openai.com/v1/responses";

        public OpenAIService(
            HttpClient httpClient,
            ILogger<OpenAIService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["OpenAI:ApiKey"];

            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogError("OpenAI API key is not configured");
                throw new InvalidOperationException("OpenAI API key is not configured");
            }

            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<List<QuestionWithAnswer>> GenerateNumericalReasoningQuestionsAsync(int count = 20)
        {
            string prompt = $"Generate {count} numerical reasoning IQ test questions";
            var tools = GetNumericalReasoningTools(count);
            var response = await SendPromptToOpenAIAsync(prompt, tools);
            return ParseQuestionsFromResponse(response, "number-logic");
        }

        public async Task<List<QuestionWithAnswer>> GenerateVerbalIntelligenceQuestionsAsync(int count = 20)
        {
            string prompt = $"Generate {count} verbal intelligence IQ test questions";
            var tools = GetVerbalIntelligenceTools(count);
            var response = await SendPromptToOpenAIAsync(prompt, tools);
            return ParseQuestionsFromResponse(response, "word-logic");
        }

        public async Task<List<QuestionWithAnswer>> GenerateMemoryRecallQuestionsAsync(int count = 15)
        {
            string prompt = $"Generate {count} memory recall IQ test questions";
            var tools = GetMemoryRecallTools(count);
            var response = await SendPromptToOpenAIAsync(prompt, tools);
            return ParseQuestionsFromResponse(response, "memory");
        }

        public async Task<List<QuestionWithAnswer>> GenerateComprehensiveIqQuestionsAsync(int count = 16)
        {
            string prompt = $"Generate {count} comprehensive IQ test questions across numerical, verbal, and memory domains";
            var tools = GetComprehensiveIqTools(count);
            var response = await SendPromptToOpenAIAsync(prompt, tools);
            return ParseQuestionsFromResponse(response, "mixed");
        }

        private async Task<string> SendPromptToOpenAIAsync(string promptText, object tools)
        {
            try
            {
                // Prepare system and user messages for the input array
                var systemMessage = new
                {
                    role = "system",
                    content = "You are an AI specialized in generating challenging, varied, and educational IQ test questions. Your task is to create questions with clear, unambiguous answers appropriate for their categories. Use the provided functions to structure your response."
                };

                var userMessage = new
                {
                    role = "user",
                    content = promptText
                };

                // Create the request body following the /v1/responses format with tools
                var request = new
                {
                    model = "o4-mini",
                    input = new[] { systemMessage, userMessage },
                    tools = tools,
                    reasoning = new
                    {
                        effort = "high" // Use high for complex question generation
                    },
                    store = true
                };

                var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
                var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                _logger.LogInformation("Sending request to OpenAI API: {0}", _apiUrl);
                _logger.LogInformation("Request body: {0}", requestJson);

                var response = await _httpClient.PostAsync(_apiUrl, content);
                _logger.LogInformation("Response status code: {0}", response.StatusCode);

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Response content: {0}", responseContent);

                response.EnsureSuccessStatusCode();

                // Parse the response to extract the generated content from tool calls
                using JsonDocument document = JsonDocument.Parse(responseContent);

                // Extract tool calls from the response
                var toolCalls = document.RootElement.GetProperty("tool_calls");
                return toolCalls.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending prompt to OpenAI API: {Message}", ex.Message);
                throw;
            }
        }

        private List<QuestionWithAnswer> ParseQuestionsFromResponse(string responseContent, string testTypeId)
        {
            try
            {
                _logger.LogInformation("Parsing questions from OpenAI response");

                // Parse the tool calls response
                using JsonDocument document = JsonDocument.Parse(responseContent);
                var result = new List<QuestionWithAnswer>();

                // Extract questions from different function calls depending on type
                if (testTypeId == "number-logic")
                {
                    foreach (var toolCall in document.RootElement.EnumerateArray())
                    {
                        if (toolCall.TryGetProperty("function", out var function) &&
                            function.TryGetProperty("name", out var name) &&
                            name.GetString() == "generate_numerical_questions")
                        {
                            if (function.TryGetProperty("arguments", out var args))
                            {
                                var arguments = JsonDocument.Parse(args.GetString());
                                if (arguments.RootElement.TryGetProperty("questions", out var questions))
                                {
                                    ParseQuestionsArray(questions, result, testTypeId);
                                }
                            }
                        }
                    }
                }
                else if (testTypeId == "word-logic")
                {
                    foreach (var toolCall in document.RootElement.EnumerateArray())
                    {
                        if (toolCall.TryGetProperty("function", out var function) &&
                            function.TryGetProperty("name", out var name) &&
                            name.GetString() == "generate_verbal_questions")
                        {
                            if (function.TryGetProperty("arguments", out var args))
                            {
                                var arguments = JsonDocument.Parse(args.GetString());
                                if (arguments.RootElement.TryGetProperty("questions", out var questions))
                                {
                                    ParseQuestionsArray(questions, result, testTypeId);
                                }
                            }
                        }
                    }
                }
                else if (testTypeId == "memory")
                {
                    foreach (var toolCall in document.RootElement.EnumerateArray())
                    {
                        if (toolCall.TryGetProperty("function", out var function) &&
                            function.TryGetProperty("name", out var name) &&
                            name.GetString() == "generate_memory_questions")
                        {
                            if (function.TryGetProperty("arguments", out var args))
                            {
                                var arguments = JsonDocument.Parse(args.GetString());
                                if (arguments.RootElement.TryGetProperty("questions", out var questions))
                                {
                                    ParseQuestionsArray(questions, result, testTypeId);
                                }
                            }
                        }
                    }
                }
                else if (testTypeId == "mixed")
                {
                    foreach (var toolCall in document.RootElement.EnumerateArray())
                    {
                        if (toolCall.TryGetProperty("function", out var function) &&
                            function.TryGetProperty("name", out var name))
                        {
                            string functionName = name.GetString();
                            if (function.TryGetProperty("arguments", out var args))
                            {
                                var arguments = JsonDocument.Parse(args.GetString());
                                if (arguments.RootElement.TryGetProperty("questions", out var questions))
                                {
                                    string category = "";
                                    if (functionName == "generate_numerical_questions") category = "numerical";
                                    else if (functionName == "generate_verbal_questions") category = "verbal";
                                    else if (functionName == "generate_memory_questions") category = "memory";

                                    ParseQuestionsArray(questions, result, category);
                                }
                            }
                        }
                    }
                }

                _logger.LogInformation("Successfully parsed {Count} questions", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing questions from OpenAI response: {Message}", ex.Message);
                throw;
            }
        }

        private void ParseQuestionsArray(JsonElement questions, List<QuestionWithAnswer> result, string testTypeId)
        {
            int startId = result.Count + 1;

            foreach (var q in questions.EnumerateArray())
            {
                string type = q.GetProperty("type").GetString();
                string text = q.GetProperty("text").GetString();
                string correctAnswer = q.GetProperty("correctAnswer").GetString();

                var questionDto = new QuestionDto
                {
                    Id = startId++,
                    Type = type,
                    Category = testTypeId == "mixed" ? testTypeId : (type == "memory-pair" ? "memory" : testTypeId),
                    Text = text
                };

                // Add options for multiple-choice questions
                if (type == "multiple-choice" && q.TryGetProperty("options", out var options))
                {
                    questionDto.Options = new List<string>();
                    foreach (var option in options.EnumerateArray())
                    {
                        questionDto.Options.Add(option.GetString());
                    }
                }

                // Add memory-specific properties
                if (type == "memory-pair")
                {
                    if (q.TryGetProperty("memorizationTime", out var memTime))
                    {
                        questionDto.MemorizationTime = memTime.GetInt32();
                    }

                    if (q.TryGetProperty("pairs", out var pairs))
                    {
                        questionDto.Pairs = new List<List<string>>();
                        foreach (var pair in pairs.EnumerateArray())
                        {
                            var pairList = new List<string>();
                            foreach (var word in pair.EnumerateArray())
                            {
                                pairList.Add(word.GetString());
                            }
                            questionDto.Pairs.Add(pairList);
                        }
                    }

                    if (q.TryGetProperty("missingIndices", out var indices))
                    {
                        questionDto.MissingIndices = new List<List<int>>();
                        foreach (var indexArray in indices.EnumerateArray())
                        {
                            var indexList = new List<int>();
                            foreach (var index in indexArray.EnumerateArray())
                            {
                                indexList.Add(index.GetInt32());
                            }
                            questionDto.MissingIndices.Add(indexList);
                        }
                    }
                }

                result.Add(new QuestionWithAnswer
                {
                    Question = questionDto,
                    CorrectAnswer = correctAnswer
                });
            }
        }

        #region Tool Definitions

        private object GetNumericalReasoningTools(int count)
        {
            // Dictionary for enum values to avoid implicit array typing issues
            var typeEnum = new Dictionary<string, string>
            {
                { "option1", "multiple-choice" },
                { "option2", "fill-in-gap" }
            };

            return new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "generate_numerical_questions",
                        description = $"Generate {count} numerical reasoning questions for IQ testing",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                questions = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            type = new
                                            {
                                                type = "string",
                                                @enum = new string[] { "multiple-choice", "fill-in-gap" }
                                            },
                                            text = new
                                            {
                                                type = "string",
                                                description = "The question text"
                                            },
                                            options = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "string"
                                                },
                                                description = "For multiple-choice questions, the answer options (array of 6 choices)"
                                            },
                                            correctAnswer = new
                                            {
                                                type = "string",
                                                description = "The correct answer"
                                            }
                                        },
                                        required = new string[] { "type", "text", "correctAnswer" },
                                        additionalProperties = false
                                    },
                                    minItems = count,
                                    maxItems = count
                                }
                            },
                            required = new string[] { "questions" },
                            additionalProperties = false
                        },
                        strict = true
                    }
                }
            };
        }

        private object GetVerbalIntelligenceTools(int count)
        {
            return new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "generate_verbal_questions",
                        description = $"Generate {count} verbal intelligence questions for IQ testing",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                questions = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            type = new
                                            {
                                                type = "string",
                                                @enum = new string[] { "multiple-choice", "fill-in-gap" }
                                            },
                                            text = new
                                            {
                                                type = "string",
                                                description = "The question text"
                                            },
                                            options = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "string"
                                                },
                                                description = "For multiple-choice questions, the answer options (array of 6 choices)"
                                            },
                                            correctAnswer = new
                                            {
                                                type = "string",
                                                description = "The correct answer"
                                            }
                                        },
                                        required = new string[] { "type", "text", "correctAnswer" },
                                        additionalProperties = false
                                    },
                                    minItems = count,
                                    maxItems = count
                                }
                            },
                            required = new string[] { "questions" },
                            additionalProperties = false
                        },
                        strict = true
                    }
                }
            };
        }

        private object GetMemoryRecallTools(int count)
        {
            return new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "generate_memory_questions",
                        description = $"Generate {count} memory recall questions for IQ testing",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                questions = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            type = new
                                            {
                                                type = "string",
                                                @enum = new string[] { "memory-pair" }
                                            },
                                            text = new
                                            {
                                                type = "string",
                                                description = "The question text/instructions"
                                            },
                                            memorizationTime = new
                                            {
                                                type = "integer",
                                                minimum = 5,
                                                maximum = 60,
                                                description = "Time in seconds allowed for memorization"
                                            },
                                            pairs = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "array",
                                                    items = new
                                                    {
                                                        type = "string"
                                                    },
                                                    minItems = 2
                                                },
                                                description = "Pairs or triplets of words to memorize"
                                            },
                                            missingIndices = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "array",
                                                    items = new
                                                    {
                                                        type = "integer",
                                                        minimum = 0
                                                    }
                                                },
                                                description = "Indices of words that will be missing and need to be recalled"
                                            },
                                            correctAnswer = new
                                            {
                                                type = "string",
                                                description = "The correct answer in format 'pair-0-word-1:apple,pair-1-word-0:mountain,...'"
                                            }
                                        },
                                        required = new string[] { "type", "text", "memorizationTime", "pairs", "missingIndices", "correctAnswer" },
                                        additionalProperties = false
                                    },
                                    minItems = count,
                                    maxItems = count
                                }
                            },
                            required = new string[] { "questions" },
                            additionalProperties = false
                        },
                        strict = true
                    }
                }
            };
        }

        private object GetComprehensiveIqTools(int count)
        {
            // Calculate number of questions of each type
            int numNumerical = count / 3;
            int numVerbal = count / 3;
            int numMemory = count - numNumerical - numVerbal;

            return new object[]
            {
                // Numerical tool
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "generate_numerical_questions",
                        description = $"Generate {numNumerical} numerical reasoning questions for IQ testing",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                questions = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            type = new
                                            {
                                                type = "string",
                                                @enum = new string[] { "multiple-choice", "fill-in-gap" }
                                            },
                                            text = new
                                            {
                                                type = "string",
                                                description = "The question text"
                                            },
                                            options = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "string"
                                                },
                                                description = "For multiple-choice questions, the answer options (array of 6 choices)"
                                            },
                                            correctAnswer = new
                                            {
                                                type = "string",
                                                description = "The correct answer"
                                            }
                                        },
                                        required = new string[] { "type", "text", "correctAnswer" },
                                        additionalProperties = false
                                    },
                                    minItems = numNumerical,
                                    maxItems = numNumerical
                                }
                            },
                            required = new string[] { "questions" },
                            additionalProperties = false
                        },
                        strict = true
                    }
                },
                
                // Verbal tool
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "generate_verbal_questions",
                        description = $"Generate {numVerbal} verbal intelligence questions for IQ testing",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                questions = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            type = new
                                            {
                                                type = "string",
                                                @enum = new string[] { "multiple-choice", "fill-in-gap" }
                                            },
                                            text = new
                                            {
                                                type = "string",
                                                description = "The question text"
                                            },
                                            options = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "string"
                                                },
                                                description = "For multiple-choice questions, the answer options (array of 6 choices)"
                                            },
                                            correctAnswer = new
                                            {
                                                type = "string",
                                                description = "The correct answer"
                                            }
                                        },
                                        required = new string[] { "type", "text", "correctAnswer" },
                                        additionalProperties = false
                                    },
                                    minItems = numVerbal,
                                    maxItems = numVerbal
                                }
                            },
                            required = new string[] { "questions" },
                            additionalProperties = false
                        },
                        strict = true
                    }
                },
                
                // Memory tool
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "generate_memory_questions",
                        description = $"Generate {numMemory} memory recall questions for IQ testing",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                questions = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            type = new
                                            {
                                                type = "string",
                                                @enum = new string[] { "memory-pair" }
                                            },
                                            text = new
                                            {
                                                type = "string",
                                                description = "The question text/instructions"
                                            },
                                            memorizationTime = new
                                            {
                                                type = "integer",
                                                minimum = 5,
                                                maximum = 60,
                                                description = "Time in seconds allowed for memorization"
                                            },
                                            pairs = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "array",
                                                    items = new
                                                    {
                                                        type = "string"
                                                    },
                                                    minItems = 2
                                                },
                                                description = "Pairs or triplets of words to memorize"
                                            },
                                            missingIndices = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "array",
                                                    items = new
                                                    {
                                                        type = "integer",
                                                        minimum = 0
                                                    }
                                                },
                                                description = "Indices of words that will be missing and need to be recalled"
                                            },
                                            correctAnswer = new
                                            {
                                                type = "string",
                                                description = "The correct answer in format 'pair-0-word-1:apple,pair-1-word-0:mountain,...'"
                                            }
                                        },
                                        required = new string[] { "type", "text", "memorizationTime", "pairs", "missingIndices", "correctAnswer" },
                                        additionalProperties = false
                                    },
                                    minItems = numMemory,
                                    maxItems = numMemory
                                }
                            },
                            required = new string[] { "questions" },
                            additionalProperties = false
                        },
                        strict = true
                    }
                }
            };
        }

        #endregion
    }
}