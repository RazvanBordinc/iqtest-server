using IqTest_server.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace IqTest_server.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<TestType> TestTypes { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<Answer> Answers { get; set; }
        public DbSet<TestResult> TestResults { get; set; }
        public DbSet<LeaderboardEntry> LeaderboardEntries { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply configurations from Data/EntityConfigurations
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            // Seed initial data for test types based on frontend constants
            modelBuilder.Entity<TestType>().HasData(
                new TestType
                {
                    Id = 1,
                    TypeId = "number-logic",
                    Title = "Numerical Reasoning",
                    Description = "Analyze patterns, solve equations, and demonstrate mathematical intelligence",
                    LongDescription = "Test your ability to recognize numerical patterns, solve complex mathematical puzzles, and think quantitatively under time constraints.",
                    Icon = "Calculator",
                    Color = "from-blue-500 to-cyan-500 dark:from-blue-600 dark:to-cyan-600",
                    QuestionsCount = 24,
                    TimeLimit = "18 minutes",
                    Difficulty = "Adaptive"
                },
                new TestType
                {
                    Id = 2,
                    TypeId = "word-logic",
                    Title = "Verbal Intelligence",
                    Description = "Process language, understand relationships between words, and analyze text",
                    LongDescription = "Challenge your vocabulary knowledge, comprehension of word relationships, and ability to extract meaning from complex language structures.",
                    Icon = "BookText",
                    Color = "from-emerald-500 to-green-500 dark:from-emerald-600 dark:to-green-600",
                    QuestionsCount = 28,
                    TimeLimit = "20 minutes",
                    Difficulty = "Adaptive"
                },
                new TestType
                {
                    Id = 3,
                    TypeId = "memory",
                    Title = "Memory & Recall",
                    Description = "Test working memory capacity, recall accuracy, and information retention",
                    LongDescription = "Evaluate your short-term memory capacity, information retention abilities, and recall accuracy across various cognitive challenges.",
                    Icon = "Brain",
                    Color = "from-amber-500 to-yellow-500 dark:from-amber-600 dark:to-yellow-600",
                    QuestionsCount = 20,
                    TimeLimit = "15 minutes",
                    Difficulty = "Adaptive"
                },
                new TestType
                {
                    Id = 4,
                    TypeId = "mixed",
                    Title = "Comprehensive IQ",
                    Description = "Full cognitive assessment combining all major intelligence domains",
                    LongDescription = "A balanced assessment combining multiple cognitive domains for a complete evaluation of general intelligence and cognitive capability.",
                    Icon = "Sparkles",
                    Color = "from-purple-500 to-indigo-500 dark:from-purple-600 dark:to-indigo-600",
                    QuestionsCount = 40,
                    TimeLimit = "35 minutes",
                    Difficulty = "Adaptive"
                }
            );
        }
    }
}