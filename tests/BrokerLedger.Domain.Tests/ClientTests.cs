using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class ClientTests
{
    [Theory]
    [InlineData("jan@example.com", "jan@example.com")]
    [InlineData("  Jan.Kowalski@Example.PL ", "jan.kowalski@example.pl")]
    [InlineData("a@b", "a@b")]
    public void Email_is_trimmed_and_lower_cased(string input, string expected)
    {
        var email = new EmailAddress(input);

        Assert.Equal(expected, email.Value);
        Assert.Equal(expected, email.ToString());
        Assert.Equal(new EmailAddress(expected), email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("jan")]
    [InlineData("@example.com")]
    [InlineData("jan@")]
    [InlineData("jan@@example.com")]
    [InlineData("j@n@example.com")]
    [InlineData("jan kowalski@example.com")]
    public void Rejects_what_is_not_an_email(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => new EmailAddress(input));

        Assert.Equal("value", ex.ParamName);
        Assert.Contains("is not a valid e-mail address", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_has_a_length_limit()
    {
        var local = new string('a', 64);
        var longest = local + "@" + new string('b', EmailAddress.MaxLength - local.Length - 1);

        Assert.Equal(254, new EmailAddress(longest).Value.Length);
        Assert.Throws<ArgumentException>(() => new EmailAddress(longest + "b"));
        Assert.Throws<ArgumentNullException>(() => new EmailAddress(null!));
    }

    [Fact]
    public void Client_account_needs_all_parts()
    {
        var id = ClientId.New();
        var email = new EmailAddress("jan@example.com");

        Assert.Equal((id, email, "hash"), (new ClientAccount(id, email, "hash").Id, new ClientAccount(id, email, "hash").Email, new ClientAccount(id, email, "hash").PasswordHash));
        Assert.Throws<ArgumentNullException>(() => new ClientAccount(null!, email, "hash"));
        Assert.Throws<ArgumentNullException>(() => new ClientAccount(id, null!, "hash"));
        Assert.Contains("password hash must not be empty", Assert.Throws<ArgumentException>(() => new ClientAccount(id, email, " ")).Message, StringComparison.Ordinal);
    }
}
