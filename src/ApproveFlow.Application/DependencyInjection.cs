using ApproveFlow.Application.LoanApplications;
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

        return services;
    }
}
