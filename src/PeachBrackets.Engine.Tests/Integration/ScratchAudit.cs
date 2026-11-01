namespace PeachBrackets.Engine.Tests.Integration;

public class ScratchAudit
{
    [Fact]
    public async Task Gen()
    {
        var sp = @"C:\Users\jhayg\AppData\Local\Temp\claude\C--Sources-GitHub-PeachPDF--claude-worktrees-issue-2019-043713\7b0ba0a6-a462-41ce-a14c-f8be8dcfa56b\scratchpad\";
        Directory.CreateDirectory(sp + "audit");
        var cases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(sp + "cases.json"))!;
        foreach (var (name, svg) in cases)
            foreach (var ppi in new[] { 72d, 96d, 144d })
            {
                var generator = new PeachPDF.PdfGenerator();
                var config = new PeachPDF.PdfGenerateConfig { PageSize = PeachPDF.PageSize.A4, PixelsPerInch = ppi };
                var doc = await generator.GeneratePdf($"<!DOCTYPE html><html><body style='margin:0'>{svg}</body></html>", config);
                using var fs = File.Create(sp + $"audit\\{name}_{ppi}.pdf");
                doc.Save(fs);
            }
    }
}
