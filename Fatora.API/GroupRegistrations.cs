namespace Fatora.API;

using Fatora.API.Services;
using Fatora.API.Validators;
using Fatora.API.Validators.CustomerValidators;
using Fatora.API.Validators.OrderValidators;
using Fatora.API.Validators.ProductValidators;
using Fatora.API.Validators.SyncValidators;
using Fatora.API.Validators.UserValidators;
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
        Services.AddScoped<IReceiptService, ReceiptService>();
        Services.AddScoped<IReportService, ReportService>();
        Services.AddScoped<IFileStorageService, FileStorageService>();
        Services.AddScoped<ISyncService, SyncService>();
        Services.AddScoped<IEmailService, EmailService>();
        Services.AddScoped<IAdminRecoveryService, AdminRecoveryService>();

        Services.AddScoped<LoginRequestValidator>();
        Services.AddScoped<RefreshTokenValidator>();
        Services.AddScoped<CreateCustomerRequestValidator>();
        Services.AddScoped<UpdateCustomerRequestValidator>();
        Services.AddScoped<CreateProductRequestValidator>();
        Services.AddScoped<UpdateProductRequestValidator>();
        Services.AddScoped<CreateOrderRequestValidator>();
        Services.AddScoped<UpdateOrderRequestValidator>();
        Services.AddScoped<RecordPaymentRequestValidator>();
        Services.AddScoped<CreateSalesRepRequestValidator>();
        Services.AddScoped<RegisterRequestValidator>();
        Services.AddScoped<UpdateSubscriptionRequestValidator>();
        Services.AddScoped<ResetPasswordRequestValidator>();
        Services.AddScoped<AdminForgotPasswordRequestValidator>();
        Services.AddScoped<AdminResetPasswordRequestValidator>();
        Services.AddScoped<ChangePasswordRequestValidator>();
        Services.AddScoped<DeleteAccountRequestValidator>();
        Services.AddScoped<UpdateProfileRequestValidator>();
        Services.AddScoped<SyncPushRequestValidator>();

        return Services;
    }
}
