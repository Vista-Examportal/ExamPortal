using System;
using System.Collections.Generic;
using System.Linq;

namespace ExamPortal.Models
{
    /// <summary>
    /// Generic server-side paging container. Wraps one page of items plus
    /// the metadata a pagination control needs (current page, total pages,
    /// total item count). Reused across any controller/list that needs
    /// paging, so the paging logic and its numbers only live in one place.
    /// </summary>
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalCount { get; set; }

        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;

        public static PagedResult<T> Empty => new();
    }

    public static class PagingExtensions
    {
        /// <summary>
        /// Applies Skip/Take paging to an IQueryable and materializes just
        /// that page, alongside the total count needed to render page
        /// numbers. Page/pageSize are clamped to sane minimums so an odd
        /// query-string value (page=0, page=-3) can't throw.
        /// </summary>
        public static PagedResult<T> ToPagedResult<T>(this IQueryable<T> query, int page, int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            var totalCount = query.Count();
            var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return new PagedResult<T>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        /// <summary>
        /// Reduces any PagedResult&lt;T&gt; down to the bit the shared
        /// _Pagination partial actually needs (page/totalPages), plus the
        /// other query-string params (filters, search text, etc.) that
        /// should be preserved when a page link is clicked.
        /// </summary>
        public static PaginationInfo ToPaginationInfo<T>(this PagedResult<T> result, string queryStringPrefix = "")
        {
            return new PaginationInfo
            {
                Page = result.Page,
                TotalPages = result.TotalPages,
                QueryStringPrefix = queryStringPrefix
            };
        }
    }

    /// <summary>
    /// Everything the shared Views/Shared/_Pagination.cshtml partial needs
    /// to render Previous/Next + page numbers and build each page's link,
    /// without the partial having to know about a specific entity type.
    /// </summary>
    public class PaginationInfo
    {
        public int Page { get; set; } = 1;
        public int TotalPages { get; set; }
        /// <summary>Other query-string params to keep on every page link, already
        /// URL-encoded and ending in "&amp;" (e.g. "q=John&amp;stage=Shortlisted&amp;"). Empty is fine.</summary>
        public string QueryStringPrefix { get; set; } = "";
    }
}
