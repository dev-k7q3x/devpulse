using DevPulse.Application.DTOs.Repositories;
using DevPulse.Application.Validators;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application.Validators;

[Trait("Category", "Unit")]
public class RepositoryAddRequestValidatorTests
{
    private readonly RepositoryAddRequestValidator _validator = new();

    [Fact]
    public void Should_Pass_When_GitHubRepoIdPositive()
    {
        // Arrange
        var request = new RepositoryAddRequest { GitHubRepoId = 1296269 };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Should_FailWithContractFieldName_When_GitHubRepoIdNotPositive(long gitHubRepoId)
    {
        // Arrange
        var request = new RepositoryAddRequest { GitHubRepoId = gitHubRepoId };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("githubRepoId");
    }
}
