using System.Collections.Generic;

namespace TenderToolSearchLambda.Models
{
    public class PaginatedSearchResponse
    {
        // Info about the current page
        public int Page { get; set; }
        public int PageSize { get; set; }

        // Info about the entire result set
        public long TotalResults { get; set; }
        public int TotalPages { get; set; }

        // The list of 10 tenders for the current page
        public List<TenderSearchDocument> Results { get; set; } = new List<TenderSearchDocument>();
    }
}
