using Microsoft.EntityFrameworkCore;

namespace TicketShield.Application.Common.Models;

/// <summary>
/// DTO generic đại diện cho danh sách dữ liệu được phân trang kèm đầy đủ metadata.
/// Áp dụng cho mọi API Query có phân trang trên toàn hệ thống TicketShield.
/// </summary>
/// <typeparam name="T">Kiểu dữ liệu của từng phần tử trong trang (thường là DTO).</typeparam>
public class PaginatedList<T>
{
    /// <summary>
    /// Danh sách các phần tử thuộc trang hiện tại.
    /// </summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// Số thứ tự trang hiện tại (bắt đầu từ 1).
    /// </summary>
    public int PageNumber { get; init; }

    /// <summary>
    /// Kích thước trang (số phần tử tối đa trên 1 trang).
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Tổng số lượng phần tử thỏa mãn điều kiện lọc trong toàn bộ hệ thống.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Tổng số trang được tính toán dựa trên TotalCount và PageSize.
    /// </summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// Cho biết có trang trước đó hay không.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Cho biết có trang kế tiếp hay không.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>
    /// Constructor rỗng phục vụ JSON deserialization.
    /// </summary>
    public PaginatedList()
    {
    }

    /// <summary>
    /// Khởi tạo một đối tượng phân trang hoàn chỉnh.
    /// </summary>
    /// <param name="items">Danh sách phần tử của trang hiện tại.</param>
    /// <param name="totalCount">Tổng số phần tử trong cơ sở dữ liệu.</param>
    /// <param name="pageNumber">Số thứ tự trang hiện tại.</param>
    /// <param name="pageSize">Kích thước trang.</param>
    public PaginatedList(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        PageNumber = pageNumber > 0 ? pageNumber : 1;
        PageSize = pageSize > 0 ? pageSize : 20;
        TotalCount = totalCount >= 0 ? totalCount : 0;
        TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);
        Items = items ?? [];
    }

    /// <summary>
    /// Tạo nhanh một đối tượng PaginatedList từ danh sách bộ nhớ (In-memory).
    /// </summary>
    public static PaginatedList<T> Create(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        return new PaginatedList<T>(items, totalCount, pageNumber, pageSize);
    }

    /// <summary>
    /// Tạo nhanh PaginatedList bất đồng bộ trực tiếp từ một IQueryable của Entity Framework Core.
    /// Tự động đếm CountAsync và phân trang Skip / Take tối ưu hiệu năng tại Database.
    /// </summary>
    /// <param name="source">IQueryable nguồn từ DbContext.</param>
    /// <param name="pageNumber">Số trang cần lấy (>= 1).</param>
    /// <param name="pageSize">Kích thước trang (>= 1).</param>
    /// <param name="cancellationToken">CancellationToken.</param>
    public static async Task<PaginatedList<T>> CreateAsync(
        IQueryable<T> source,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var validPageNumber = pageNumber > 0 ? pageNumber : 1;
        var validPageSize = pageSize > 0 ? pageSize : 20;

        var count = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((validPageNumber - 1) * validPageSize)
            .Take(validPageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<T>(items, count, validPageNumber, validPageSize);
    }

    /// <summary>
    /// Tiện ích chuyển đổi (Map) các phần tử trong trang sang kiểu dữ liệu DTO đích mà vẫn giữ nguyên metadata phân trang.
    /// </summary>
    /// <typeparam name="TDestination">Kiểu DTO đích.</typeparam>
    /// <param name="mapFunc">Hàm chuyển đổi từng phần tử.</param>
    public PaginatedList<TDestination> Map<TDestination>(Func<T, TDestination> mapFunc)
    {
        var mappedItems = Items.Select(mapFunc).ToList();
        return new PaginatedList<TDestination>(mappedItems, TotalCount, PageNumber, PageSize);
    }
}
