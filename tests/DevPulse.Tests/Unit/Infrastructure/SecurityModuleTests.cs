using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.DependencyInjection;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.Unit.Infrastructure;

[Trait("Category", "Unit")]
public class SecurityModuleTests
{
    private const string ConnectionString = "Host=localhost;Database=devpulse;Username=devpulse;Password=devpulse";

    [Fact]
    public void Should_ResolveTokenProtectorAndAuthService_When_InfrastructureAdded()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Act
        services.AddInfrastructure(BuildConfiguration());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Assert
        scope.ServiceProvider.GetRequiredService<ITokenProtector>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAuthService>().Should().NotBeNull();
    }

    [Fact]
    public void Should_FailFast_When_CertificateRequiredButMissing()
    {
        // Arrange – produkcja bez certyfikatu zapisałaby klucze jawnym tekstem obok zaszyfrowanych tokenów
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new() { ["DataProtection:RequireCertificate"] = "true" });

        // Act
        var act = () => services.AddInfrastructure(configuration);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*DataProtection__CertificateBase64*");
    }

    [Fact]
    public void Should_ConfigureKeyEncryption_When_CertificateProvided()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new()
        {
            ["DataProtection:RequireCertificate"] = "true",
            ["DataProtection:CertificateBase64"] = CreatePfxBase64("pfx-password"),
            ["DataProtection:CertificatePassword"] = "pfx-password",
        });

        // Act
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        // Assert
        var keyManagement = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        keyManagement.XmlEncryptor.Should().NotBeNull();
        keyManagement.XmlRepository.Should().NotBeNull();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? values = null)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = ConnectionString };
        foreach (var (key, value) in values ?? [])
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static string CreatePfxBase64(string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=DevPulse Data Protection Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(TestClock.Now.AddYears(-1), TestClock.Now.AddYears(10));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, password));
    }
}
