using DevPulse.Domain.Entities;

namespace DevPulse.Tests.Builders;

public sealed class UserBuilder
{
    private static long _nextGitHubId = 1_000_000;

    private long _gitHubId = Interlocked.Increment(ref _nextGitHubId);
    private string _login = "octocat";
    private string? _avatarUrl = "https://avatars.githubusercontent.com/u/1";
    private string _accessTokenEncrypted = "CfDJ8-encrypted-token";
    private string _tokenScopes = "read:user,user:email,repo";
    private DateTimeOffset _now = TestClock.Now;
    private bool _tokenInvalid;

    public UserBuilder WithGitHubId(long gitHubId)
    {
        _gitHubId = gitHubId;
        return this;
    }

    public UserBuilder WithLogin(string login)
    {
        _login = login;
        return this;
    }

    public UserBuilder WithTokenScopes(string tokenScopes)
    {
        _tokenScopes = tokenScopes;
        return this;
    }

    public UserBuilder WithInvalidToken()
    {
        _tokenInvalid = true;
        return this;
    }

    public UserBuilder At(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    public User Build()
    {
        var user = User.Create(_gitHubId, _login, _avatarUrl, _accessTokenEncrypted, _tokenScopes, _now);
        if (_tokenInvalid)
        {
            user.MarkTokenInvalid(_now);
        }

        return user;
    }
}
