using PeachPDF;
using PeachPDF.Layout;
using s2industries.ZUGFeRD;
using System.Globalization;

// A ZUGFeRD / Factur-X e-invoice, built end to end in C#:
//
//   1. One invoice model - the single source of truth.
//   2. The invoice's structured data, as Cross Industry Invoice (CII) XML, from the open-source
//      ZUGFeRD-csharp library. PeachPDF embeds XML you give it; it does not generate it.
//   3. The human-readable invoice, laid out with PeachPDF's declarative API from the same model - so the
//      pages and the XML carry the same information, which is what lets the XML be embedded as an
//      "Alternative" representation of the PDF.
//   4. PdfGenerateConfig.FacturX embeds the XML in a PDF/A-3 document and marks it as an e-invoice in the
//      XMP metadata. The profile (EN 16931 or XRECHNUNG here) is read from the XML itself.
//
// Open the PDF's attachments panel to see the embedded factur-x.xml (or xrechnung.xml).
static class ZugferdInvoiceShowcase
{
    sealed record Party(string Name, string Street, string Postcode, string City, string Email);

    sealed record Line(string Item, decimal Quantity, decimal UnitPrice, decimal VatRate)
    {
        public decimal Total => Round(Quantity * UnitPrice);
    }

    const string InvoiceNumber = "2026-0042";
    const string SellerVatId = "DE123456789";
    const string Iban = "DE02120300000000202051";
    const string Bic = "BYLADEM1001";

    // Germany's Leitweg-ID: how an invoice to a public body names the department it is for. XRechnung
    // requires it as the invoice's buyer reference.
    const string LeitwegId = "04011000-12345-34";

    static readonly DateTime IssueDate = new(2026, 9, 21);
    static readonly DateTime DueDate = IssueDate.AddDays(30);

    static readonly Party Seller = new("Sonnenschein Bakery GmbH", "Hauptstrasse 12", "10115", "Berlin", "invoices@sonnenschein.example");
    static readonly Party Buyer = new("Musterstadt Utilities", "Rathausplatz 1", "80331", "Munich", "inbox@musterstadt-utilities.example");

    static readonly Line[] Lines =
    [
        new("Sourdough loaf, 750 g", 120, 3.20m, 7),
        new("Pretzel assortment, box of 24", 15, 18.50m, 7),
        new("Catering service, staff canteen event", 1, 640.00m, 19),
        new("Delivery", 1, 25.00m, 19),
    ];

    // Invoice amounts round half away from zero (46.305 -> 46.31), not .NET's default half-to-even; the
    // format's validators recompute every total the way an accountant would.
    static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture) + " EUR";

    /// <summary>The VAT breakdown: net amount and VAT per rate.</summary>
    static IEnumerable<(decimal Rate, decimal Net, decimal Vat)> VatBreakdown() =>
        Lines.GroupBy(line => line.VatRate).OrderBy(group => group.Key)
            .Select(group =>
            {
                var net = group.Sum(line => line.Total);
                return (group.Key, net, Round(net * group.Key / 100));
            });

    /// <summary>The invoice as CII XML, in the EN 16931 profile or, for a public-sector buyer, XRECHNUNG.</summary>
    static byte[] CreateInvoiceXml(bool xRechnung)
    {
        var net = Lines.Sum(line => line.Total);
        var vat = VatBreakdown().Sum(rate => rate.Vat);

        var invoice = InvoiceDescriptor.CreateInvoice(InvoiceNumber, IssueDate, CurrencyCodes.EUR);
        invoice.Type = InvoiceType.Invoice;

        // The delivery (or service) date. Besides being a German requirement, it makes the library write the
        // delivery section the EN 16931 schema insists on.
        invoice.ActualDeliveryDate = IssueDate;

        invoice.SetSeller(Seller.Name, Seller.Postcode, Seller.City, Seller.Street, CountryCodes.DE);
        invoice.AddSellerTaxRegistration(SellerVatId, TaxRegistrationSchemeID.VA);
        invoice.SetSellerContact("Anna Baecker", null, Seller.Email, "+49 30 1234567");
        invoice.SetSellerElectronicAddress(Seller.Email, ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp);

        invoice.SetBuyer(Buyer.Name, Buyer.Postcode, Buyer.City, Buyer.Street, CountryCodes.DE);
        invoice.SetBuyerElectronicAddress(Buyer.Email, ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp);
        if (xRechnung)
        {
            invoice.ReferenceOrderNo = LeitwegId;
            // BT-23: XRechnung requires the business process, and the Peppol billing process is the standard one.
            invoice.BusinessProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";
        }

        invoice.SetPaymentMeans(PaymentMeansTypeCodes.SEPACreditTransfer);
        invoice.AddCreditorFinancialAccount(Iban, Bic);
        invoice.AddTradePaymentTerms($"Payable within 30 days, by {DueDate:yyyy-MM-dd}.", DueDate);

        foreach (var line in Lines)
        {
            invoice.AddTradeLineItem(
                name: line.Item, netUnitPrice: line.UnitPrice, unitCode: QuantityCodes.C62, billedQuantity: line.Quantity,
                taxType: TaxTypes.VAT, categoryCode: TaxCategoryCodes.S, taxPercent: line.VatRate);
        }

        foreach (var (rate, rateNet, rateVat) in VatBreakdown())
            invoice.AddApplicableTradeTax(rateNet, rate, rateVat, TaxTypes.VAT, TaxCategoryCodes.S);

        invoice.SetTotals(lineTotalAmount: net, taxBasisAmount: net, taxTotalAmount: vat, grandTotalAmount: net + vat, duePayableAmount: net + vat);

        using var stream = new MemoryStream();
        invoice.Save(stream, ZUGFeRDVersion.Version23, xRechnung ? Profile.XRechnung : Profile.Comfort);
        return stream.ToArray();
    }

    public static async Task<PeachPdfDocument> BuildAsync(PdfGenerator generator, bool xRechnung)
    {
        var net = Lines.Sum(line => line.Total);
        var vat = VatBreakdown().Sum(rate => rate.Vat);

        var config = new PdfGenerateConfig
        {
            // PDF/A-3 is the container Factur-X requires; the accessible "A" level is the one the
            // specification recommends, and needs a document language.
            PageSize = PageSize.A4,
            PdfAConformance = PdfAConformance.PdfA3A,
            DefaultLanguage = "en",
            Metadata = new PdfDocumentMetadata
            {
                Title = $"Invoice {InvoiceNumber}",
                Author = Seller.Name,
                // An XMP stream needs a creation date; the declarative API has no <meta> tags to read one from.
                CreationDate = new DateTimeOffset(IssueDate, TimeSpan.Zero),
            },
            // The profile is left unset: it is derived from the XML's own guideline identifier.
            FacturX = new FacturXOptions { Xml = CreateInvoiceXml(xRechnung) },
        };

        return await generator.CreateDocument(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSize.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontFamily("Arial", "sans-serif").FontSize(10));

                page.Footer(footer => footer.Text(t =>
                {
                    t.Alignment(TextAlignment.Center);
                    t.Span($"Invoice {InvoiceNumber} - Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                }));

                page.Content(content => content.Column(column =>
                {
                    column.Spacing(16);

                    column.Item().Text(t => t.Span("Invoice").Bold().FontSize(22));

                    column.Item().Row(row =>
                    {
                        row.Spacing(24);
                        row.Item().Grow().Column(from => Address(from, "From", Seller, $"VAT ID {SellerVatId}"));
                        row.Item().Grow().Column(to => Address(to, "To", Buyer));
                        row.Item().Grow().Column(details =>
                        {
                            details.Spacing(2);
                            details.Item().Text(t => t.Span("Details").Bold());
                            details.Item().Text($"Invoice no. {InvoiceNumber}");
                            details.Item().Text($"Date {IssueDate:yyyy-MM-dd}");
                            details.Item().Text($"Due {DueDate:yyyy-MM-dd}");
                            if (xRechnung)
                                details.Item().Text($"Buyer reference (Leitweg-ID) {LeitwegId}");
                        });
                    });

                    column.Item().Table(table =>
                    {
                        table.Columns(columns =>
                        {
                            columns.RelativeColumn(5);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Padding(4).Text(t => t.Span("Item").Bold());
                            header.Cell().Padding(4).AlignRight().Text(t => t.Span("Qty").Bold());
                            header.Cell().Padding(4).AlignRight().Text(t => t.Span("Unit price").Bold());
                            header.Cell().Padding(4).AlignRight().Text(t => t.Span("VAT").Bold());
                            header.Cell().Padding(4).AlignRight().Text(t => t.Span("Total").Bold());
                        });

                        foreach (var line in Lines)
                        {
                            table.Row(row =>
                            {
                                row.Cell().Padding(4).Text(line.Item);
                                row.Cell().Padding(4).AlignRight().Text(line.Quantity.ToString("0", CultureInfo.InvariantCulture));
                                row.Cell().Padding(4).AlignRight().Text(Money(line.UnitPrice));
                                row.Cell().Padding(4).AlignRight().Text($"{line.VatRate:0}%");
                                row.Cell().Padding(4).AlignRight().Text(Money(line.Total));
                            });
                        }
                    });

                    column.Item().Width(240).Column(totals =>
                    {
                        totals.Spacing(2);
                        totals.Item().Text($"Net total: {Money(net)}");
                        foreach (var (rate, rateNet, rateVat) in VatBreakdown())
                            totals.Item().Text($"VAT {rate:0}% on {Money(rateNet)}: {Money(rateVat)}");
                        totals.Item().Text(t => t.Span($"Amount due: {Money(net + vat)}").Bold().FontSize(12));
                    });

                    column.Item().Text($"Please pay by SEPA credit transfer to {Iban} (BIC {Bic}) within 30 days, by {DueDate:yyyy-MM-dd}.");

                    column.Item().Text(t => t.Span(
                        $"This invoice is a hybrid e-invoice: the same data is embedded as structured XML ({(xRechnung ? "xrechnung.xml" : "factur-x.xml")}, " +
                        $"{(xRechnung ? "XRECHNUNG" : "EN 16931")} profile) in this PDF/A-3 file.").FontColor(PdfColor.FromHex("#555555")));
                }));
            });
        }, config);
    }

    static void Address(IColumnDescriptor column, string heading, Party party, string? extraLine = null)
    {
        column.Spacing(2);
        column.Item().Text(t => t.Span(heading).Bold());
        column.Item().Text(party.Name);
        column.Item().Text(party.Street);
        column.Item().Text($"{party.Postcode} {party.City}");
        if (extraLine is not null)
            column.Item().Text(extraLine);
    }
}
