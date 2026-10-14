using ApproveFlow.Application.Administration;
using ApproveFlow.Application.Audit;
using ApproveFlow.Application.Customers;
using ApproveFlow.Application.Dashboard;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Application.Lookups;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApproveFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton, includeInternalTypes: true);

        services.AddScoped<ILoanApplicationService, LoanApplicationService>();
        services.AddScoped<ILoanWorkflowService, LoanWorkflowService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IReferenceDataAdminService, ReferenceDataAdminService>();

        return services;
    }
}
