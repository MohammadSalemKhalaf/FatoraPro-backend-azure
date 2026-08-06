namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IReportService
{
    Task<List<SalesBucketResponse>> GetSalesAsync(Guid userId, SummaryPeriod period, Guid? scopeToRepId = null);
    Task<List<ItemReportResponse>> GetItemsAsync(Guid userId, SummaryPeriod period, ItemsSortBy sortBy, Guid? scopeToRepId = null);
    Task<List<ClientReportResponse>> GetClientsAsync(Guid userId, SummaryPeriod period, ClientsSortBy sortBy, Guid? scopeToRepId = null);
}
