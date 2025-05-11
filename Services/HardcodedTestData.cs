// Services/TestTypeData.cs
using System.Collections.Generic;
using System.Linq;
using IqTest_server.DTOs.Test;

namespace IqTest_server.Services
{
    public static class TestTypeData
    {
        // Hardcoded test types (these are fixed and never change)
        public static readonly List<TestTypeDto> TestTypes = new List<TestTypeDto>
        {
            new TestTypeDto
            {
                Id = "number-logic",
                Title = "Numerical Reasoning",
                Description = "Analyze patterns, solve equations, and demonstrate mathematical intelligence",
                LongDescription = "Test your ability to recognize numerical patterns, solve complex mathematical puzzles, and think quantitatively under time constraints.",
                Icon = "Calculator",
                Color = "from-blue-500 to-cyan-500 dark:from-blue-600 dark:to-cyan-600",
                Stats = new TestStatsDto
                {
                    QuestionsCount = 24,
                    TimeLimit = "25 minutes",
                    Difficulty = "Adaptive"
                }
            },
            new TestTypeDto
            {
                Id = "word-logic",
                Title = "Verbal Intelligence",
                Description = "Process language, understand relationships between words, and analyze text",
                LongDescription = "Challenge your vocabulary knowledge, comprehension of word relationships, and ability to extract meaning from complex language structures.",
                Icon = "BookText",
                Color = "from-emerald-500 to-green-500 dark:from-emerald-600 dark:to-green-600",
                Stats = new TestStatsDto
                {
                    QuestionsCount = 28,
                    TimeLimit = "30 minutes",
                    Difficulty = "Adaptive"
                }
            },
            new TestTypeDto
            {
                Id = "memory",
                Title = "Memory & Recall",
                Description = "Test working memory capacity, recall accuracy, and information retention",
                LongDescription = "Evaluate your short-term memory capacity, information retention abilities, and recall accuracy across various cognitive challenges.",
                Icon = "Brain",
                Color = "from-amber-500 to-yellow-500 dark:from-amber-600 dark:to-yellow-600",
                Stats = new TestStatsDto
                {
                    QuestionsCount = 20,
                    TimeLimit = "22 minutes",
                    Difficulty = "Adaptive"
                }
            },
            new TestTypeDto
            {
                Id = "mixed",
                Title = "Comprehensive IQ",
                Description = "Full cognitive assessment combining all major intelligence domains",
                LongDescription = "A balanced assessment combining multiple cognitive domains for a complete evaluation of general intelligence and cognitive capability.",
                Icon = "Sparkles",
                Color = "from-purple-500 to-indigo-500 dark:from-purple-600 dark:to-indigo-600",
                Stats = new TestStatsDto
                {
                    QuestionsCount = 40,
                    TimeLimit = "45 minutes",
                    Difficulty = "Adaptive"
                }
            }
        };

        public static TestTypeDto GetTestTypeById(string testTypeId)
        {
            return TestTypes.FirstOrDefault(t => t.Id == testTypeId);
        }

        public static List<TestTypeDto> GetAllTestTypes()
        {
            return TestTypes;
        }
    }
}