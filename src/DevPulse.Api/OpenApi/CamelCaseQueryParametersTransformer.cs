using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DevPulse.Api.OpenApi;

/// <summary>
/// Parametry query z DTO (<c>[FromQuery] AuthLoginRequest</c>) ApiExplorer nazywa jak właściwości C# (<c>ReturnUrl</c>).
/// Kontrakt (plan 03) używa camelCase jak w JSON – binding i tak nie rozróżnia wielkości liter.
/// </summary>
internal sealed class CamelCaseQueryParametersTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter.In == ParameterLocation.Query && parameter.Name is { } name)
            {
                parameter.Name = JsonNamingPolicy.CamelCase.ConvertName(name);
            }
        }

        return Task.CompletedTask;
    }
}
