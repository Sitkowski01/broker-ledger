using Microsoft.OpenApi;

namespace BrokerLedger.Api;

/// <summary>
/// Opis API w OpenAPI (<c>/openapi/v1.json</c>) i przegladarka dokumentacji (<c>/docs</c>, Swagger UI z CDN).
/// Endpointy pod <c>/api/me</c> wymagaja tokenu — w Swagger UI wkleja sie go przyciskiem Authorize.
/// </summary>
internal static class ApiDocs
{
    /// <summary>Swagger UI przypiety do konkretnej wersji — nowa wersja z CDN nie zmieni strony bez naszej wiedzy.</summary>
    private const string SwaggerUi = "https://cdn.jsdelivr.net/npm/swagger-ui-dist@5.32.15";

    public static IServiceCollection AddApiDocs(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "broker-ledger",
                Version = "v1",
                Description = "Dom maklerski przed gielda Parkiet: ksiega podwojnego zapisu, zlecenia (saga), rozliczenie z Kafki. "
                    + "Kwoty jako tekst (\"250.40\"), operacje zmieniajace srodki wymagaja naglowka Idempotency-Key.",
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Token z POST /api/auth/login",
            };
            document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
            return Task.CompletedTask;
        }));

    public static IEndpointRouteBuilder MapApiDocs(this IEndpointRouteBuilder app)
    {
        app.MapOpenApi();
        app.MapGet("/docs", () => Results.Content($$"""
            <!doctype html>
            <html lang="pl">
            <head>
              <meta charset="utf-8">
              <title>broker-ledger · API</title>
              <link rel="stylesheet" href="{{SwaggerUi}}/swagger-ui.css">
            </head>
            <body>
              <div id="swagger-ui"></div>
              <script src="{{SwaggerUi}}/swagger-ui-bundle.js"></script>
              <script>SwaggerUIBundle({ url: '/openapi/v1.json', dom_id: '#swagger-ui', persistAuthorization: true });</script>
            </body>
            </html>
            """, "text/html")).ExcludeFromDescription();
        return app;
    }
}
