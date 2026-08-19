using ContentImporter.Application.ContentProviders;
using ContentImporter.Application.ContentProviders.Dtos;
using ContentImporter.Application.ContentProviders.WordPress;

namespace ContentImporter.Tests.ContentProviders;

/// <summary>
/// First-level validation: is this raw WordPress item worth mapping at all?
/// Nothing here interprets values - it only rejects items the source should never have exported.
/// </summary>
public sealed class WordPressDtoValidatorTests
{
    private static readonly IDtoValidator<WordPressDto> Validator = new WordPressDtoValidator();

    /// <summary>A post exactly as the sample export writes it. Every test mutates one field of this.</summary>
    private static readonly WordPressDto ValidPost = new()
    {
        PostId = 201,
        Title = "This is dummy title D",
        PostType = "post",
        Status = "publish",
        PostDateGmt = "2026-01-04 00:00:00"
    };

    [Fact]
    public async Task A_well_formed_post_is_accepted()
    {
        Assert.True((await Validator.ValidateAsync(ValidPost)).IsValid);
    }

    [Fact]
    public async Task An_attachment_is_accepted()
    {
        // WXR exports images as items with post_type "attachment" and status "inherit".
        // They are not published, so the publish-date rule must not apply to them.
        //
        // fable5: --st*
        // WXR 把图片导出为 post_type 是 "attachment"、status 是 "inherit" 的 items。
        // 它们并未发布，所以 publish-date 规则不得作用在它们身上。
        // *en--
        WordPressDto attachment = ValidPost with
        {
            PostId = 301,
            Title = "dummy-image-one",
            PostType = "attachment",
            Status = "inherit",
            PostDateGmt = null
        };

        Assert.True((await Validator.ValidateAsync(attachment)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task An_item_without_a_usable_post_id_is_rejected(long? postId)
    {
        Assert.False((await Validator.ValidateAsync(ValidPost with { PostId = postId })).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_item_without_a_title_is_rejected(string? title)
    {
        Assert.False((await Validator.ValidateAsync(ValidPost with { Title = title })).IsValid);
    }

    [Fact]
    public async Task A_title_beyond_the_contract_limit_is_rejected()
    {
        string tooLong = new('x', WordPressDtoValidator.TitleMaxLength + 1);

        Assert.False((await Validator.ValidateAsync(ValidPost with { Title = tooLong })).IsValid);
        Assert.True((await Validator.ValidateAsync(ValidPost with { Title = tooLong[..^1] })).IsValid);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("draft")]
    [InlineData("pending")]
    [InlineData("private")]
    [InlineData("future")]
    [InlineData("trash")]
    [InlineData("inherit")]
    public async Task Every_status_wordpress_can_export_is_recognised(string status)
    {
        // Drafts carry no usable date, so only "publish" is paired with one here.
        // fable5: --st* draft 没有可用的日期，所以这里只有 "publish" 状态会配上日期。 *en--
        WordPressDto item = ValidPost with
        {
            Status = status,
            PostDateGmt = status == "publish" ? ValidPost.PostDateGmt : null
        };

        Assert.True((await Validator.ValidateAsync(item)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("archived")]
    public async Task An_unrecognised_status_is_rejected(string? status)
    {
        Assert.False((await Validator.ValidateAsync(ValidPost with { Status = status })).IsValid);
    }

    [Fact]
    public async Task A_published_item_without_a_date_is_rejected()
    {
        Assert.False((await Validator.ValidateAsync(ValidPost with { PostDateGmt = null })).IsValid);
    }

    [Fact]
    public async Task A_published_item_carrying_the_draft_placeholder_date_is_rejected()
    {
        // The serializer deliberately keeps this literal as text rather than failing to parse it,
        // which makes rejecting it this layer's job.
        //
        // fable5: --st*
        // serializer 刻意把这个字面量保留为文本而不是解析失败，
        // 因此拒绝它就成了这一层（validator）的职责。
        // *en--
        Assert.False((await Validator.ValidateAsync(ValidPost with { PostDateGmt = "0000-00-00 00:00:00" })).IsValid);
    }

    [Fact]
    public async Task A_draft_without_a_date_is_accepted()
    {
        Assert.True((await Validator.ValidateAsync(ValidPost with { Status = "draft", PostDateGmt = null })).IsValid);
    }
}
