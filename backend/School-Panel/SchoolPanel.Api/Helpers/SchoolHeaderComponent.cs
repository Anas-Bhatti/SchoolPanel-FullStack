// ============================================================
// Helpers/SchoolHeaderComponent.cs
//
// WHY QuestPDF over iText7:
//   - QuestPDF uses a fluent, compile-time-safe C# API.
//     iText7 uses a JavaScript-era object model with
//     PdfWriter, PdfDocument, Document, Paragraph, Cell —
//     verbose and error-prone for complex layouts.
//   - QuestPDF renders ~5× faster for text-heavy documents.
//   - Community license is free for revenue < $1M USD/year.
//   - Layout is declarative (Row/Column/Table) matching how
//     we think about report structure, not PDF primitives.
//   - Hot-reload support for rapid template iteration.
//   - iText7 is preferred only when you need: PDF/A archival,
//     digital signatures, or AcroForm filling — none of which
//     this panel requires.
//
// This file contains:
//   - SchoolHeaderComponent  : reusable page header
//   - PageNumberComponent    : footer with page X of Y
//   - BrandColors            : strongly-typed colour palette
//   - LogoLoader             : fetches + caches logo bytes
// ============================================================

using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SchoolPanel.Reports.Models;

namespace SchoolPanel.Reports.Helpers;

// ─── Brand colour tokens ──────────────────────────────────────

public sealed class BrandColors
{
    public string Primary { get; }
    public string PrimaryBg { get; }   // 10% opacity version for table headers
    public string Success { get; } = "#16A34A";
    public string Danger { get; } = "#DC2626";
    public string Warning { get; } = "#D97706";
    public string Dark { get; } = "#111827";
    public string Gray { get; } = "#6B7280";
    public string LightGray { get; } = "#F3F4F6";
    public string Border { get; } = "#E5E7EB";
    public string White { get; } = "#FFFFFF";

    public BrandColors(string primaryHex)
    {
        Primary = primaryHex;
        // Derive a very light tint (no alpha needed — compute manually)
        PrimaryBg = LightenHex(primaryHex, 0.88f);
    }

    // Lighten a hex colour toward white by factor (0=same, 1=white)
    private static string LightenHex(string hex, float factor)
    {
        hex = hex.TrimStart('#');
        int r = Convert.ToInt32(hex[..2], 16);
        int g = Convert.ToInt32(hex[2..4], 16);
        int b = Convert.ToInt32(hex[4..6], 16);

        r = (int)(r + (255 - r) * factor);
        g = (int)(g + (255 - g) * factor);
        b = (int)(b + (255 - b) * factor);

        return $"#{r:X2}{g:X2}{b:X2}";
    }
}

// ─── Logo loader (HTTP + memory cache) ────────────────────────

public interface ILogoLoader
{
    Task<byte[]?> LoadAsync(string? url, CancellationToken ct = default);
}

public sealed class LogoLoader : ILogoLoader
{
    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LogoLoader> _log;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public LogoLoader(
        IHttpClientFactory http,
        IMemoryCache cache,
        ILogger<LogoLoader> log)
    {
        _http = http;
        _cache = cache;
        _log = log;
    }

    public async Task<byte[]?> LoadAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Check memory cache first — logos rarely change
        if (_cache.TryGetValue(url, out byte[]? cached))
            return cached;

        try
        {
            var client = _http.CreateClient("LogoLoader");
            client.Timeout = TimeSpan.FromSeconds(10);

            var bytes = await client.GetByteArrayAsync(url, ct);

            _cache.Set(url, bytes, CacheDuration);
            return bytes;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not load school logo from {Url}", url);
            return null;
        }
    }
}

// ─── Reusable School Header Component ─────────────────────────

/// <summary>
/// Renders the standard school header used on every PDF report:
///   ┌──────────────────────────────────────────────────────┐
///   │  [LOGO]   SCHOOL NAME                               │
///   │           Address · Phone · Email                   │
///   │           ─────────────────────────────────────────  │
///   │           REPORT TITLE                              │
///   └──────────────────────────────────────────────────────┘
/// </summary>
public static class SchoolHeaderComponent
{
    public static void Compose(
        IContainer container,
        SchoolSettings school,
        string reportTitle,
        BrandColors colors,
        byte[]? logoBytes)
    {
        container.Column(col =>
        {
            // ── Top bar: logo + school identity ────────────────────────────
            col.Item()
               .BorderBottom(2).BorderColor(colors.Primary)
               .PaddingBottom(8)
               .Row(row =>
               {
                   // Logo (optional)
                   if (logoBytes != null && logoBytes.Length > 0)
                   {
                       row.ConstantItem(56)
                          .Padding(2)
                          .Image(logoBytes, ImageScaling.FitArea);
                   }

                   // School name + contact
                   row.RelativeItem()
                      .PaddingLeft(logoBytes != null ? 10 : 0)
                      .Column(inner =>
                      {
                          inner.Item()
                               .Text(school.Name)
                               .FontSize(14)
                               .FontColor(colors.Primary)
                               .Bold()
                               .LineHeight(1.2f);

                          var contact = new[]
                          {
                              school.Address,
                              school.Phone,
                              school.Email,
                              school.Website
                          }
                          .Where(v => !string.IsNullOrWhiteSpace(v))
                          .ToList();

                          if (contact.Count > 0)
                          {
                              inner.Item()
                                   .Text(string.Join("  ·  ", contact))
                                   .FontSize(7.5f)
                                   .FontColor(colors.Gray);
                          }
                      });
               });

            // ── Report title bar ───────────────────────────────────────────
            col.Item()
               .Background(colors.Primary)
               .PaddingVertical(6)
               .PaddingHorizontal(10)
               .AlignCenter()
               .Text(reportTitle.ToUpperInvariant())
               .FontSize(11)
               .FontColor(colors.White)
               .Bold()
               .LetterSpacing(1.5f);
        });
    }
}

// ─── Page number footer ───────────────────────────────────────

public static class PageFooterComponent
{
    public static void Compose(
        IContainer container,
        BrandColors colors,
        string schoolName)
    {
        container
            .BorderTop(0.5f).BorderColor(colors.Border)
            .PaddingTop(4)
            .Row(row =>
            {
                row.RelativeItem()
                   .Text(schoolName)
                   .FontSize(7)
                   .FontColor(colors.Gray)
                   .Italic();

                row.AutoItem()
                   .Text(text =>
                   {
                       text.Span("Page ")
                           .FontSize(7)
                           .FontColor(colors.Gray);
                       text.CurrentPageNumber()
                           .FontSize(7)
                           .FontColor(colors.Gray);
                       text.Span(" of ")
                           .FontSize(7)
                           .FontColor(colors.Gray);
                       text.TotalPages()
                           .FontSize(7)
                           .FontColor(colors.Gray);
                   });
            });
    }
}

// ─── Table styling helpers ────────────────────────────────────

public static class TableStyles
{
    public static void HeaderCell(
        IContainer cell,
        string text,
        BrandColors colors,
        bool alignRight = false)
    {
        var c = cell
            .Background(colors.Primary)
            .PaddingVertical(5)
            .PaddingHorizontal(6);

        var t = (alignRight ? c.AlignRight() : c)
            .Text(text)
            .FontSize(8)
            .FontColor(colors.White)
            .SemiBold();
    }

    public static void DataCell(
        IContainer cell,
        string text,
        BrandColors colors,
        bool shaded = false,
        bool alignRight = false,
        bool bold = false,
        string? textColor = null)
    {
        var bg = shaded ? colors.LightGray : colors.White;
        var c = cell
            .Background(bg)
            .BorderBottom(0.25f).BorderColor(colors.Border)
            .PaddingVertical(4)
            .PaddingHorizontal(6);

        var t = (alignRight ? c.AlignRight() : c)
            .Text(text)
            .FontSize(8)
            .FontColor(textColor ?? colors.Dark);

        if (bold) t.Bold();
    }
}