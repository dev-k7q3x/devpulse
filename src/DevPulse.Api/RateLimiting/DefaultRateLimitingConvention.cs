using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.RateLimiting;

namespace DevPulse.Api.RateLimiting;

/// <summary>
/// Polityka <see cref="RateLimitPolicies.Default"/> dla akcji bez własnej. Nie używamy
/// <c>MapControllers().RequireRateLimiting(...)</c>: metadane konwencji trafiają za atrybuty akcji
/// i nadpisywałyby <c>[EnableRateLimiting]</c> / <c>[DisableRateLimiting]</c>.
/// </summary>
internal sealed class DefaultRateLimitingConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        var hasOwnPolicy = action.Attributes.Concat(action.Controller.Attributes)
            .Any(attribute => attribute is EnableRateLimitingAttribute or DisableRateLimitingAttribute);
        if (hasOwnPolicy)
        {
            return;
        }

        foreach (var selector in action.Selectors)
        {
            selector.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimitPolicies.Default));
        }
    }
}
