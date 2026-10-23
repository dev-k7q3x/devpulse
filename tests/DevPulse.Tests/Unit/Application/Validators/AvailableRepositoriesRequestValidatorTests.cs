using DevPulse.Application.DTOs.Repositories;
using DevPulse.Application.Validators;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application.Validators;

[Trait("Category", "Unit")]
public class AvailableRepositoriesRequestValidatorTests
{
    private readonly AvailableRepositoriesRequestValidator _validator = new();

    [Fact]
    public void Should_Pass_When_DefaultsUsed()
    {
        // Arrange
        var request = new AvailableRepositoriesRequest();

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
        request.Page.Should().Be(1);
        request.PageSize.Should().Be(50);
    }

    [Theory]
    [InlineData(0, 50, null)]
    [InlineData(1, 0, null)]
    [InlineData(1, 101, null)]
    [InlineData(1, 50, "x")]
    public void Should_Fail_When_PagingOrSearchOutOfRange(int page, int pageSize, string? searchSeed)
    {
        // Arrange – searchSeed „x” daje frazę dłuższą niż limit
        var search = searchSeed is null ? null : new string('x', AvailableRepositoriesRequestValidator.SearchMaxLength + 1);
        var request = new AvailableRepositoriesRequest { Page = page, PageSize = pageSize, Search = search };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
