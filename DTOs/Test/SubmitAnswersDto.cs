using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using IqTest_server.Converters;

namespace IqTest_server.DTOs.Test
{
    public class SubmitAnswersDto
    {
        [Required]
        public required string TestTypeId { get; set; }

        [Required]
        public required List<AnswerDto> Answers { get; set; }

        // Time taken in seconds
        public double? TimeTaken { get; set; }
    }

    public class AnswerDto
    {
        public int QuestionId { get; set; }
        
        [JsonConverter(typeof(AnswerValueJsonConverter))]
        public required object Value { get; set; } // Can be int (index) or string depending on question type
        
        public required string Type { get; set; }
    }
}