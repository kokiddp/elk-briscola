using Briscola.Infrastructure.Auth;

namespace Briscola.Api.IntegrationTests.Auth;

public sealed class BCryptGamePasswordHasherTests
{
    private readonly BCryptGamePasswordHasher _hasher = new();

    [Fact]
    public void Hash_returns_a_non_empty_string_different_from_password()
    {
        var hash = _hasher.Hash("secret");
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe("secret");
    }

    [Fact]
    public void Hash_is_unique_per_call_due_to_salt()
    {
        _hasher.Hash("secret").Should().NotBe(_hasher.Hash("secret"));
    }

    [Fact]
    public void Verify_succeeds_for_correct_password()
    {
        var hash = _hasher.Hash("Strong-Pass-456");
        _hasher.Verify("Strong-Pass-456", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_fails_for_wrong_password()
    {
        var hash = _hasher.Hash("Strong-Pass-456");
        _hasher.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_returns_false_for_malformed_hash()
    {
        _hasher.Verify("anything", "not-a-bcrypt-hash").Should().BeFalse();
    }

    [Fact]
    public void Hash_throws_on_empty_password()
    {
        Action act = () => _hasher.Hash(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_throws_on_empty_inputs()
    {
        Action a = () => _hasher.Verify(string.Empty, "x");
        Action b = () => _hasher.Verify("x", string.Empty);
        a.Should().Throw<ArgumentException>();
        b.Should().Throw<ArgumentException>();
    }
}
