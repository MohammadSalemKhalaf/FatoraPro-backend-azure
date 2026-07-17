namespace Fatora.API;

using Fatora.API.Validators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Fatora.BL.Services.Classes;

public static class GroupRegistrations
{
    public static IServiceCollection AddGroupedServices(this IServiceCollection Services)
    {
        Services.AddScoped<IJwtTokenProviderService, JwtTokenProviderService>();
        Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        Services.AddScoped<ILoginService, LoginService>();
        Services.AddScoped<IUserService, UserService>();
        Services.AddScoped<IProductService, ProductService>();
        Services.AddScoped<ICustomerService, CustomerService>();
        Services.AddScoped<IOrderService, OrderService>();
        Services.AddScoped<IPaymentService, PaymentService>();
        Services.AddScoped<IReportService, ReportService>();
        Services.AddScoped<LoginRequestValidator>();
        Services.AddScoped<RefreshTokenValidator>();
        return Services;
    }
}