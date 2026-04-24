// ============================================================
// Services/PdfReceiptService.cs
// Generates fee receipt PDFs using QuestPDF.
// NuGet: QuestPDF
// ============================================================

using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolPanel.Controllers.DTOs;

namespace SchoolPanel.Controllers.Services;

public interface IPdfReceiptService
{
    /// <summary>Render a receipt PDF and return the raw bytes.</summary>
    byte[] GenerateReceipt(PaymentReceipt receipt);
}

public sealed class PdfReceiptService : IPdfReceiptService
{
    private readonly ILogger<PdfReceiptService> _log;

    // Colour palette — matches the brand system (#2563EB primary)
    private static readonly string BrandBlue = "#2563EB";
    private static readonly string BrandGreen = "#16A34A";
    private static readonly string LightGray = "#F3F4F6";
    private static readonly string DarkText = "#111827";
    private static readonly string MutedText = "#6B7280";

    public PdfReceiptService(ILogger<PdfReceiptService> log)
    {
        _log = log;
        // Community licence is free for non-commercial use;
        // set QuestPDF.Settings.License = LicenseType.Professional for production
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateReceipt(PaymentReceipt r)
    {
        try
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A5);
                    page.Margin(30);
                    page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9).FontColor(DarkText));

                    page.Header().Element(Header);
                    page.Content().Element(c => Content(c, r));
                    page.Footer().Element(Footer);
                });
            })
            .GeneratePdf();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PDF generation failed for receipt {Rcp}", r.ReceiptNumber);
            throw;
        }
    }

    // ─── Header ───────────────────────────────────────────────

    private static void Header(IContainer c)
    {
        c.Column(col =>
        {
            // School name bar
            col.Item()
               .Background(BrandBlue)
               .Padding(10)
               .AlignCenter()
               .Text(text =>
               {
                   text.Span("SCHOOL MANAGEMENT PANEL")
                       .FontSize(13).Bold().FontColor(Colors.White);
               });

            col.Item().Height(4);

            col.Item()
               .Background(LightGray)
               .PaddingVertical(6).PaddingHorizontal(10)
               .Row(row =>
               {
                   row.RelativeItem()
                      .Text(text =>
                      {
                          text.Span("FEE RECEIPT")
                              .FontSize(11).Bold().FontColor(BrandBlue);
                      });

                   row.AutoItem()
                      .AlignRight()
                      .Text(text =>
                      {
                          text.Span("PAID")
                              .FontSize(10).Bold().FontColor(BrandGreen);
                      });
               });
        });
    }

    // ─── Content ──────────────────────────────────────────────

    private static void Content(IContainer c, PaymentReceipt r)
    {
        c.PaddingTop(12).Column(col =>
        {
            // ── Receipt + Date row ────────────────────────────
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(sub =>
                {
                    sub.Item().Text(t =>
                    {
                        t.Span("Receipt No: ").SemiBold();
                        t.Span(r.ReceiptNumber).FontColor(BrandBlue).SemiBold();
                    });
                    sub.Item().Text(t =>
                    {
                        t.Span("Payment Date: ").SemiBold();
                        t.Span(r.PaymentDate.ToString("dd MMM yyyy"));
                    });
                });

                row.RelativeItem().AlignRight().Column(sub =>
                {
                    sub.Item().AlignRight().Text(t =>
                    {
                        t.Span("Method: ").SemiBold();
                        t.Span(r.PaymentMethod);
                    });
                    if (!string.IsNullOrEmpty(r.ReferenceNumber))
                        sub.Item().AlignRight().Text(t =>
                        {
                            t.Span("Ref: ").SemiBold();
                            t.Span(r.ReferenceNumber);
                        });
                });
            });

            col.Item().Height(8);

            // ── Student info box ──────────────────────────────
            col.Item()
               .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
               .Background(LightGray)
               .Padding(8)
               .Column(sub =>
               {
                   LabelValue(sub, "Student", r.StudentName);
                   LabelValue(sub, "Roll No", r.RollNumber);
                   LabelValue(sub, "Class", r.ClassName);
               });

            col.Item().Height(10);

            // ── Fee details table ─────────────────────────────
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3);  // Description
                    cols.RelativeColumn(2);  // Amount
                });

                // Header row
                table.Header(h =>
                {
                    h.Cell().Background(BrandBlue).Padding(5)
                       .Text("Description").FontColor(Colors.White).SemiBold();
                    h.Cell().Background(BrandBlue).Padding(5).AlignRight()
                       .Text("Amount").FontColor(Colors.White).SemiBold();
                });

                // Fee type row
                TableRow(table, r.FeeTypeName, FormatMoney(r.AmountDue), shaded: false);

                // Discount row (only if non-zero)
                if (r.Discount > 0)
                    TableRow(table, "Discount", $"- {FormatMoney(r.Discount)}", shaded: true,
                        colour: BrandGreen);

                // Fine row (only if non-zero)
                if (r.Fine > 0)
                    TableRow(table, "Late Fine", $"+ {FormatMoney(r.Fine)}", shaded: false,
                        colour: "#DC2626");

                // Amount paid
                table.Cell()
                     .Background(LightGray)
                     .Padding(5)
                     .Text("Amount Paid").SemiBold();
                table.Cell()
                     .Background(LightGray)
                     .Padding(5).AlignRight()
                     .Text(FormatMoney(r.AmountPaid)).SemiBold().FontColor(BrandGreen);

                // Balance
                table.Cell()
                     .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                     .Padding(5)
                     .Text("Balance Due");
                table.Cell()
                     .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                     .Padding(5).AlignRight()
                     .Text(FormatMoney(r.Balance))
                     .FontColor(r.Balance > 0 ? "#DC2626" : BrandGreen).SemiBold();
            });

            col.Item().Height(12);

            // ── Collected by ──────────────────────────────────
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Collected by: ").SemiBold();
                    t.Span(r.CollectedBy);
                });
                row.AutoItem().Text("Authorised Signatory")
                   .FontColor(MutedText).Italic();
            });
        });
    }

    // ─── Footer ───────────────────────────────────────────────

    private static void Footer(IContainer c)
    {
        c.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten2)
         .PaddingTop(4)
         .AlignCenter()
         .Text(t =>
         {
             t.Span("This is a computer-generated receipt and does not require a signature.")
              .FontSize(7).FontColor(MutedText).Italic();
         });
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static void LabelValue(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(55).Text(label + ":").SemiBold().FontColor(MutedText);
            row.RelativeItem().Text(value);
        });
    }

    private static void TableRow(
        TableDescriptor table, string desc, string amount,
        bool shaded, string? colour = null)
    {
        var bg = shaded ? LightGray : "#FFFFFF";
        table.Cell().Background(bg).Padding(5).Text(desc);
        var cell = table.Cell().Background(bg).Padding(5).AlignRight();
        var txt = cell.Text(amount);
        if (colour != null) txt.FontColor(colour);
    }

    private static string FormatMoney(decimal amount)
        => $"Rs. {amount:N2}";
}