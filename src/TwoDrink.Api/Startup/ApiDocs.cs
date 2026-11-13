using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace TwoDrink.Api.Startup;

/// <summary>
/// OpenAPI document + Swagger UI, for local exploration only: <see cref="AddApiDocs"/> / <see cref="MapApiDocs"/> do nothing
/// outside Development, so production exposes neither (see CLAUDE.md, "Testar localmente").
/// </summary>
public static class ApiDocs
{
    public const string DocumentName = "v1";
    public const string BearerScheme = "Bearer";

    public static IServiceCollection AddApiDocs(this IServiceCollection services, IHostEnvironment env)
    {
        if (!env.IsDevelopment()) return services;

        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "2DRINK API",
                    Version = "v1",
                    Description = "API do app 2DRINK. Erros de regra voltam como ProblemDetails (HTTP 422) com a propriedade `code`.",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Cole apenas o accessToken (sem a palavra Bearer).",
                };
                // MapHealthChecks has no ApiExplorer metadata, so describe /health by hand.
                document.Paths ??= new OpenApiPaths();
                document.Paths["/health"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            Summary = "Saude da API e do banco de dados",
                            Description = "Anonimo. 200 quando saudavel, 503 quando o banco esta indisponivel.",
                            Tags = new HashSet<OpenApiTagReference> { new("Health", document) },
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse { Description = "Healthy" },
                                ["503"] = new OpenApiResponse { Description = "Unhealthy" },
                            },
                        },
                    },
                };
                return Task.CompletedTask;
            });

            // The fallback policy makes every endpoint private unless it says AllowAnonymous.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (!metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [],
                    });
                }
                return Task.CompletedTask;
            });
        });
        return services;
    }

    public static WebApplication MapApiDocs(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;

        app.MapOpenApi().AllowAnonymous().DisableRateLimiting();
        return app;
    }

    /// <summary>Swagger UI static files; must run before authorization (the fallback policy would block them).</summary>
    public static WebApplication UseApiDocsUi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;
        app.UseSwaggerUI(o =>
        {
            o.RoutePrefix = "swagger";
            o.DocumentTitle = "2DRINK API";
            o.SwaggerEndpoint("/openapi/v1.json", "2DRINK API v1");
            o.EnablePersistAuthorization();
        });
        return app;
    }

    // Small helpers so endpoint registrations stay readable.

    public static RouteHandlerBuilder Doc(this RouteHandlerBuilder b, string tag, string summary, string? description = null) =>
        b.WithTags(tag).WithSummary(summary).WithDescription(description ?? summary);

    /// <summary>Documents ProblemDetails responses (with a <c>code</c> extension) for the given status codes.</summary>
    public static RouteHandlerBuilder Problems(this RouteHandlerBuilder b, params int[] statusCodes)
    {
        foreach (var code in statusCodes) b.ProducesProblem(code);
        return b;
    }

    /// <summary>401 + 403 (profile_incomplete) for "Player" policy endpoints, plus any extra codes.</summary>
    public static RouteHandlerBuilder PlayerProblems(this RouteHandlerBuilder b, params int[] extra) =>
        b.Problems([StatusCodes.Status401Unauthorized, StatusCodes.Status403Forbidden, .. extra]);
}
