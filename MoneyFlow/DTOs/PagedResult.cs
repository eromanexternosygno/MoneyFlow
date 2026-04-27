namespace MoneyFlow.DTOs
{
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page {  get; set; }
        public int PageSize { get; set; }

        public int TotalPages => (int)Math.Ceiling((double)TotalCount / Page);
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;

        public static PagedResult<T> Empty => new() { Items = new(), TotalCount = 0 };
    }
}
