using DevPulse.Application.Errors;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class ProblemTypesTests
{
    [Theory]
    [InlineData(400, ProblemTypes.Validation)]
    [InlineData(401, ProblemTypes.Unauthenticated)]
    [InlineData(403, ProblemTypes.Forbidden)]
    [InlineData(404, ProblemTypes.NotFound)]
    [InlineData(409, ProblemTypes.Conflict)]
    [InlineData(422, ProblemTypes.LimitExceeded)]
    [InlineData(429, ProblemTypes.RateLimited)]
    public void Should_ReturnSlugFromContract_When_StatusHasDefaultType(int statusCode, string expected)
    {
        // Arrange – status z tabeli 0.1 planu 03

        // Act
        var type = ProblemTypes.ForStatus(statusCode);

        // Assert
        type.Should().Be(expected);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void Should_ReturnNull_When_StatusHasNoDefaultType(int statusCode)
    {
        // Arrange – 502/503 to wyłącznie błędy GitHuba ustawiane jawnie, 500 nie ma slugu w kontrakcie

        // Act
        var type = ProblemTypes.ForStatus(statusCode);

        // Assert
        type.Should().BeNull();
    }
}
