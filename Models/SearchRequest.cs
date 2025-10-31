using System.ComponentModel.DataAnnotations;

namespace TenderToolSearchLambda.Models
{
    public class SearchRequest
    {
        [Required]
        public string Query { get; set; }

        // We set defaults for pagination to make it easier
        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 10;
    }
}
