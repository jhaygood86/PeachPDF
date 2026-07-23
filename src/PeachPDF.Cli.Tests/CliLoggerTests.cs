using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class CliLoggerTests
{
    [Fact]
    public void LogFile_ReceivesAllLevels()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "peachpdf-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var options = ArgumentParser.Parse(["--log", logPath, "--verbose", "--debug", "doc.html"]);
            using (var logger = new CliLogger(options))
            {
                logger.Info("info-line");
                logger.Debug("debug-line");
                logger.Warn("warn-line");
            }

            var contents = File.ReadAllText(logPath);
            Assert.Contains("info-line", contents);
            Assert.Contains("debug-line", contents);
            Assert.Contains("warn-line", contents);
        }
        finally
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void WithoutVerbose_StillWritesToLogFile()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "peachpdf-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var options = ArgumentParser.Parse(["--log", logPath, "doc.html"]);
            using (var logger = new CliLogger(options))
            {
                logger.Info("quiet-info");
            }

            Assert.Contains("quiet-info", File.ReadAllText(logPath));
        }
        finally
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void NoLogFile_DoesNotThrow()
    {
        using var logger = new CliLogger(ArgumentParser.Parse(["doc.html"]));
        logger.Info("x");
        logger.Debug("y");
        logger.Warn("z");
    }
}
