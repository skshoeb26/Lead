using System.Net;
using FluentAssertions;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Outreach;
using LeadPilot.Infrastructure.Gmail;
using LeadPilot.Infrastructure.Web;
using Microsoft.Extensions.Options;

namespace LeadPilot.Tests.Unit;

public class SsrfProtectionTests
{
    private readonly UrlSafetyValidator _validator = new(Options.Create(new WebsiteAnalysisOptions()));

    [Theory]
    [InlineData("http://localhost/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://172.16.4.4/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/")]
    [InlineData("gopher://example.com/")]
    [InlineData("http://example.com:22/")]
    [InlineData("http://user:pass@example.com/")]
    [InlineData("http://intranet/")]
    [InlineData("http://printer.local/")]
    public void Unsafe_urls_are_blocked(string url) => _validator.Invoking(v => v.ValidateUrl(url)).Should().Throw<UnsafeUrlException>();

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://www.example.co.in/contact")]
    [InlineData("example.com")]
    public void Public_urls_are_allowed(string url) => _validator.ValidateUrl(url).Should().NotBeNull();

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("fe80::1", false)]
    public void Address_classification(string ip, bool isPublic) => UrlSafetyValidator.IsPublic(IPAddress.Parse(ip)).Should().Be(isPublic);
}

public class RobotsTests
{
    [Fact]
    public void Disallowed_paths_are_respected()
    {
        var r = RobotsRules.Parse("User-agent: *\nDisallow: /private\nAllow: /private/public\n", "LeadPilotBot");
        r.IsAllowed("/contact").Should().BeTrue();
        r.IsAllowed("/private/data").Should().BeFalse();
        r.IsAllowed("/private/public/page").Should().BeTrue();
    }

    [Fact]
    public void Agent_specific_group_wins()
    {
        var r = RobotsRules.Parse("User-agent: *\nDisallow:\n\nUser-agent: LeadPilotBot\nDisallow: /\n", "LeadPilotBot/1.0");
        r.IsAllowed("/anything").Should().BeFalse();
    }
}

public class EmailHashTests
{
    [Fact]
    public void Hash_changes_when_any_field_changes()
    {
        var baseHash = EmailContentHasher.Compute("a@b.in", "Subject", "Body");
        EmailContentHasher.Compute("c@b.in", "Subject", "Body").Should().NotBe(baseHash);
        EmailContentHasher.Compute("a@b.in", "Subject!", "Body").Should().NotBe(baseHash);
        EmailContentHasher.Compute("a@b.in", "Subject", "Body.").Should().NotBe(baseHash);
    }

    [Fact]
    public void Hash_is_stable_across_line_endings_and_case_of_recipient() =>
        EmailContentHasher.Compute("A@B.in", "Subject", "Line1\r\nLine2  ").Should().Be(EmailContentHasher.Compute("a@b.in", "Subject", "Line1\nLine2"));
}

public class MimeBuilderTests
{
    [Fact]
    public void Builds_rfc2822_message_with_utf8_body_and_base64url_raw()
    {
        var mime = MimeBuilder.Build(new OutgoingEmail("Shoeb <me@gmail.com>", "info@abc.in", null, "नमस्ते ABC", "Hello\nनमस्ते", "me@gmail.com", "<mailto:me@gmail.com?subject=unsubscribe>"));
        mime.Should().Contain("To: info@abc.in\r\n");
        mime.Should().Contain("Subject: =?UTF-8?B?");
        mime.Should().Contain("Content-Type: text/plain; charset=UTF-8");
        mime.Should().Contain("List-Unsubscribe: <mailto:me@gmail.com?subject=unsubscribe>");
        var raw = MimeBuilder.ToBase64Url(mime);
        raw.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        MimeBuilder.FromBase64Url(raw).Should().Be(mime);
    }

    [Fact]
    public void Header_injection_is_rejected() =>
        FluentActions.Invoking(() => MimeBuilder.Build(new OutgoingEmail("me@gmail.com", "a@b.in", null, "Hi\r\nBcc: victim@x.com", "body", null, null)))
            .Should().Throw<ArgumentException>();
}
