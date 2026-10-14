using Microsoft.OpenApi;

namespace ApproveFlow.Web.Api;

public static class SwaggerSetup
{
    public const string BearerScheme = "Bearer";

    public static IServiceCollection AddApproveFlowSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ApproveFlow API",
                Version = "v1",
                Description =
                    "Maker-checker loan approval API for Lanka Demo Bank (a fictional organisation).\n\n" +
                    "1. Call `POST /api/v1/auth/token` with a demo account.\n" +
                    "2. Click **Authorize** and paste the `accessToken`.\n" +
                    "3. Read an application (`GET`) to obtain its `rowVersion`, then send it with any change.\n\n" +
                    "Errors use RFC 7807 problem details: 400 malformed request, 401 missing/invalid token, 403 role or " +
                    "four-eyes violation, 404 not found, 409 stale rowVersion or invalid state transition, 422 validation, 429 rate limit.",
            });

            // Only the versioned REST API is documented; MVC pages are excluded.
            options.DocInclusionPredicate((_, api) => api.RelativePath?.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase) == true);

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT access token from POST /api/v1/auth/token.",
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = new List<string>(),
            });

            foreach (var xml in Directory.EnumerateFiles(AppContext.BaseDirectory, "ApproveFlow.*.xml"))
            {
                options.IncludeXmlComments(xml, includeControllerXmlComments: true);
            }

            options.SupportNonNullableReferenceTypes();
            options.OperationFilter<AllowAnonymousOperationFilter>();
        });

        return services;
    }

    /// <summary>Removes the padlock (security requirement) from anonymous operations such as POST /auth/token.</summary>
    private sealed class AllowAnonymousOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
    {
        public void Apply(OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
        {
            if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
            {
                operation.Security = new List<OpenApiSecurityRequirement>();
            }
        }
    }
}
