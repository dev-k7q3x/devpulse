using DevPulse.Application.DTOs.Settings;
using DevPulse.Application.Validators;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application.Validators;

[Trait("Category", "Unit")]
public class SettingsUpdateRequestValidatorTests
{
    private readonly SettingsUpdateRequestValidator _validator = new();

    [Theory]
    [InlineData(0, "Europe/Warsaw")]
    [InlineData(23, "America/New_York")]
    [InlineData(7, "UTC")]
    public void Should_Pass_When_HourInRangeAndIanaTimeZone(int sendHour, string timeZoneId)
    {
        // Arrange
        var request = new SettingsUpdateRequest { SendHour = sendHour, TimeZoneId = timeZoneId, DigestEnabled = true };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Should_Fail_When_HourOutOfRange(int sendHour)
    {
        // Arrange
        var request = new SettingsUpdateRequest { SendHour = sendHour, TimeZoneId = "Europe/Warsaw" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(SettingsUpdateRequest.SendHour));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("Central European Standard Time")]
    public void Should_Fail_When_TimeZoneIsNotKnownIanaId(string timeZoneId)
    {
        // Arrange – id Windows też odrzucamy: kontrakt to strefy IANA
        var request = new SettingsUpdateRequest { SendHour = 7, TimeZoneId = timeZoneId };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SettingsUpdateRequest.TimeZoneId));
    }
}
