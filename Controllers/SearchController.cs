using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using TenderToolSearchLambda.Models;

namespace TenderToolSearchLambda.Controllers
{
    [ApiController]
    [Route("api/search")]
    public class SearchController : Controller
    {
        private readonly IOpenSearchClient _openSearchClient;
        private readonly ILogger<SearchController> _logger;
        private const string IndexName = "tenders"; // The index we are searching

        public SearchController(
            IOpenSearchClient openSearchClient,
            ILogger<SearchController> logger)
        {
            _openSearchClient = openSearchClient;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> SearchAsync([FromBody] TenderToolSearchLambda.Models.SearchRequest request)
        {
            try
            {
                _logger.LogInformation("Search request received. Query: '{Query}', Page: {Page}, Size: {PageSize}",
                    request.Query, request.Page, request.PageSize);

                // --- 1. Validate and Set Pagination ---
                var page = request.Page < 1 ? 1 : request.Page;
                var size = request.PageSize < 1 ? 10 : request.PageSize;
                var from = (page - 1) * size; // 'From' is the 0-based index

                // --- 2. Build the OpenSearch Query ---
                var searchResponse = await _openSearchClient.SearchAsync<TenderSearchDocument>(s => s
                    .Index(IndexName)
                    .From(from)
                    .Size(size)
                    .Query(q =>
                    {
                        // If the search term is empty, we'll do a "match all"
                        if (string.IsNullOrWhiteSpace(request.Query))
                        {
                            return q.MatchAll();
                        }

                        // Otherwise, perform a weighted multi-match query
                        return q.MultiMatch(m => m
                            .Query(request.Query)
                            .Fields(f => f
                                .Field(d => d.Title, boost: 3.0) // Title is most important
                                .Field(d => d.Tags, boost: 2.0)  // Tags are very important
                                .Field(d => d.TenderNumber)
                                .Field(d => d.Description)
                                .Field(d => d.AISummary)
                                .Field(d => d.Source)
                                .Field(d => d.Province)
                                .Field(d => d.Category)
                            )
                            .Type(TextQueryType.BestFields)
                        );
                    })
                );

                // --- 3. Handle Failed Query ---
                if (!searchResponse.IsValid)
                {
                    _logger.LogError("OpenSearch query failed: {DebugInfo}", searchResponse.DebugInformation);
                    return StatusCode(500, new { message = $"Search query failed: {searchResponse.DebugInformation}" });
                }

                // --- 4. Build the Paginated Response ---
                var totalResults = searchResponse.Total;
                var totalPages = (int)Math.Ceiling((double)totalResults / size);
                var documents = searchResponse.Documents.ToList();

                var response = new PaginatedSearchResponse
                {
                    Page = page,
                    PageSize = size,
                    TotalPages = totalPages,
                    TotalResults = totalResults,
                    Results = documents
                };

                _logger.LogInformation("Search successful. Found {TotalResults} results. Returning page {Page} of {TotalPages}.",
                    totalResults, page, totalPages);

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during search.");
                return StatusCode(500, new { message = "An unexpected error occurred. Please check logs." });
            }
        }
    }
}
