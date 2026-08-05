using System.Text.Json.Serialization;

namespace ContentImporter.Application.ContentProviders.Dtos;

/// <summary>
/// The WordPress side of the wire, transcribed exactly as WordPress writes it.
/// </summary>
/// <remarks>
/// <para>
/// These types deliberately do no work. Every property is nullable, nothing is
/// <c>required</c>, dates are <see cref="string"/> and flags are <see cref="int"/> — because a DTO
/// that validates is a DTO that throws, and this pipeline reports bad items as results rather than
/// exceptions. Deserialization must succeed for anything that is well-formed JSON so that the
/// per-item validator gets a chance to say precisely what was wrong. Marking
/// <c>Title</c> as <c>required</c> would trade a usable import report for a
/// <c>JsonException</c> naming a byte offset.
/// </para>
/// <para>
/// Names are pinned with <see cref="JsonPropertyNameAttribute"/> rather than left to a
/// <c>SnakeCaseLower</c> naming policy. The policy would in fact map every name here correctly, but
/// it puts the contract in whoever configures <c>JsonSerializerOptions</c> instead of in the type,
/// and a provider DTO that only parses when someone remembers a setting is a trap.
/// </para>
/// <para>
/// WordPress's native export is WXR — RSS 2.0 carrying WordPress data in the <c>wp:</c>,
/// <c>dc:</c> and <c>content:</c> namespaces. This is that structure with the prefixes flattened,
/// which is what WXR-to-JSON tooling emits: <c>content:encoded</c> becomes
/// <c>content_encoded</c>, <c>dc:creator</c> becomes <c>creator</c>, <c>wp:post_id</c> becomes
/// <c>post_id</c>.
/// </para>
/// </remarks>
/// <summary>
/// One row of the export's items array: a post, a page, an attachment or any custom post type.
/// </summary>
/// <remarks>
/// WXR has no separate media section — an image is an item with <see cref="PostType"/> of
/// <c>attachment</c> and <see cref="Status"/> of <c>inherit</c>. Anything mapping this to content
/// must filter on <see cref="PostType"/> or it will import the site's media library as pages whose
/// title is a filename.
/// </remarks>
public sealed record WordPressDto
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("link")]
    public string? Link { get; init; }

    [JsonPropertyName("pub_date")]
    public string? PubDate { get; init; }

    /// <summary>The author's login name, matching <c>author_login</c> in the export's header.</summary>
    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    /// <summary>
    /// Permalink-shaped, and not a URL. WordPress documents this as an opaque identifier that
    /// merely looks like an address, so identity comes from <see cref="PostId"/> instead.
    /// </summary>
    [JsonPropertyName("guid")]
    public string? Guid { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The post body — <c>content:encoded</c> in WXR. Carries Gutenberg block markup
    /// (<c>&lt;!-- wp:paragraph --&gt;</c>) interleaved with the HTML, which is real content as far
    /// as WordPress is concerned and a mapper's decision to keep or strip.
    /// </summary>
    [JsonPropertyName("content_encoded")]
    public string? ContentEncoded { get; init; }

    [JsonPropertyName("excerpt_encoded")]
    public string? ExcerptEncoded { get; init; }

    /// <summary>
    /// Identity within the source site. <c>long</c> because WordPress stores this as
    /// <c>BIGINT(20) UNSIGNED</c>; a site with a long history of revisions and imports can push
    /// IDs past <see cref="int"/> even with a modest number of live posts.
    /// </summary>
    [JsonPropertyName("post_id")]
    public long? PostId { get; init; }

    /// <summary>
    /// Site-local time, in MySQL's <c>yyyy-MM-dd HH:mm:ss</c> — no <c>T</c>, no offset. A string
    /// rather than a <see cref="DateTimeOffset"/> on purpose: drafts carry the literal
    /// <c>"0000-00-00 00:00:00"</c>, which is not a date any parser accepts, so binding this to a
    /// date type turns an ordinary draft into a deserialization failure that kills the whole item.
    /// Parsing belongs in the mapper, where "unparseable" can become a null publication date.
    /// </summary>
    [JsonPropertyName("post_date")]
    public string? PostDate { get; init; }

    /// <summary>The UTC counterpart of <see cref="PostDate"/>, and the one to map from.</summary>
    [JsonPropertyName("post_date_gmt")]
    public string? PostDateGmt { get; init; }

    [JsonPropertyName("post_modified")]
    public string? PostModified { get; init; }

    [JsonPropertyName("post_modified_gmt")]
    public string? PostModifiedGmt { get; init; }

    [JsonPropertyName("comment_status")]
    public string? CommentStatus { get; init; }

    [JsonPropertyName("ping_status")]
    public string? PingStatus { get; init; }

    /// <summary>The URL slug — <c>wp:post_name</c>, not the display title.</summary>
    [JsonPropertyName("post_name")]
    public string? PostName { get; init; }

    /// <summary>
    /// <c>publish</c>, <c>draft</c>, <c>pending</c>, <c>private</c>, <c>future</c>, <c>trash</c>,
    /// or <c>inherit</c> for attachments. This — not the presence of a date — is what decides
    /// whether content is published, and an export contains drafts and trashed posts unless
    /// somebody filtered them out first.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// Two different meanings behind one field. On a page it is the parent page, giving the site
    /// its hierarchy; on an attachment it is the post the file was uploaded to. Zero means neither.
    /// </summary>
    [JsonPropertyName("post_parent")]
    public long? PostParent { get; init; }

    [JsonPropertyName("menu_order")]
    public int? MenuOrder { get; init; }

    /// <summary><c>post</c>, <c>page</c>, <c>attachment</c>, or any registered custom post type.</summary>
    [JsonPropertyName("post_type")]
    public string? PostType { get; init; }

    [JsonPropertyName("post_password")]
    public string? PostPassword { get; init; }

    /// <summary>WordPress writes 0 or 1 here, not a JSON boolean.</summary>
    [JsonPropertyName("is_sticky")]
    public int? IsSticky { get; init; }

    /// <summary>Present only on attachments: the absolute URL of the uploaded file.</summary>
    [JsonPropertyName("attachment_url")]
    public string? AttachmentUrl { get; init; }

    /// <summary>
    /// Both categories and tags, told apart by <see cref="WordPressTaxonomyRefDto.Domain"/>.
    /// </summary>
    [JsonPropertyName("categories")]
    public IReadOnlyList<WordPressTaxonomyRefDto>? Categories { get; init; }

    /// <summary>
    /// Arbitrary key/value pairs, and where WordPress keeps things a typed schema would model
    /// properly — the featured image is <c>_thumbnail_id</c> holding an attachment ID as text, the
    /// page template is <c>_wp_page_template</c>. Plugins add their own freely, so this is
    /// open-ended by nature.
    /// </summary>
    [JsonPropertyName("postmeta")]
    public IReadOnlyList<WordPressPostMetaDto>? PostMeta { get; init; }
}



/// <summary>
/// An item's reference to a term. Distinct from the term declared in the export's header, because
/// WXR really does use different field names in the two places: the taxonomy is <c>domain</c> here and
/// <c>taxonomy</c> there, the slug is <c>nicename</c> here and <c>slug</c> there, and no term id
/// travels with the reference at all — terms are matched by slug.
/// </summary>
public sealed record WordPressTaxonomyRefDto
{
    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("nicename")]
    public string? NiceName { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>One custom field. Values are always text on the wire, whatever they represent.</summary>
public sealed record WordPressPostMetaDto
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}
