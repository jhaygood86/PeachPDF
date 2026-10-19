using System.Reflection;
using PeachDrawing.PDF.Engine;
using PeachPDF.Rendering;

namespace PeachPDF.Tests.PdfBackendTests
{
    /// <summary>
    /// The PDF writer mirrors the option types <see cref="PdfGenerateConfig"/> exposes (it cannot reference PeachPDF), and
    /// <see cref="EngineOptions"/> maps between the two by cast. These tests pin the mirrors to the originals, so adding a member to one
    /// without the other fails here instead of silently mapping to the wrong value.
    /// </summary>
    public class EngineOptionsParityTests
    {
        public static TheoryData<Type, Type> EnumPairs => new()
        {
            { typeof(PeachPDF.PdfAConformance), typeof(PeachDrawing.PDF.Engine.PdfAConformance) },
            { typeof(PeachPDF.PdfXConformance), typeof(PeachDrawing.PDF.Engine.PdfXConformance) },
            { typeof(PeachPDF.PdfVersion), typeof(PeachDrawing.PDF.Engine.PdfVersion) },
            { typeof(PeachPDF.ImageCompression), typeof(PeachDrawing.PDF.Engine.ImageCompression) },
            { typeof(PeachPDF.PageSize), typeof(PeachDrawing.PDF.Engine.PageSize) },
            { typeof(PeachPDF.PageOrientation), typeof(PeachDrawing.PDF.Engine.PageOrientation) },
            { typeof(PeachPDF.PdfPermissions), typeof(PeachDrawing.PDF.Engine.PdfPermissions) },
            { typeof(PeachPDF.ColorBlackGeneration), typeof(PeachDrawing.PDF.Engine.ColorBlackGeneration) },
            { typeof(PeachPDF.ColorConversionMode), typeof(PeachDrawing.PDF.Engine.ColorConversionMode) },
            { typeof(PeachPDF.ColorRenderingIntent), typeof(PeachDrawing.PDF.Engine.ColorRenderingIntent) },
        };

        [Theory]
        [MemberData(nameof(EnumPairs))]
        public void MirroredEnum_HasTheSameNamesAndValuesAsTheOriginal(Type original, Type mirror)
        {
            Assert.Equal(Describe(original), Describe(mirror));
        }

        [Theory]
        [InlineData(typeof(PeachPDF.ColorOptions), typeof(PeachDrawing.PDF.Engine.ColorOptions))]
        [InlineData(typeof(PeachPDF.PdfEncryptionOptions), typeof(PeachDrawing.PDF.Engine.PdfEncryptionOptions))]
        public void MirroredOptionClass_HasTheSamePublicProperties(Type original, Type mirror)
        {
            static string[] Properties(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(Properties(original), Properties(mirror));
        }

        [Fact]
        public void ColorOptions_MapsEveryProperty()
        {
            var original = new PeachPDF.ColorOptions
            {
                OutputIntentProfile = [1, 2, 3],
                OutputIntentIdentifier = "id",
                BlackGeneration = PeachPDF.ColorBlackGeneration.UseRichBlack,
                FallbackCmykProfile = [4],
                ConversionMode = PeachPDF.ColorConversionMode.ConvertToProfile,
                ConvertToProfile = [5, 6],
                RenderingIntent = PeachPDF.ColorRenderingIntent.Perceptual,
                UseBlackPointCompensation = false,
            };

            var mapped = original.ToEngine()!;

            Assert.Equal(original.OutputIntentProfile, mapped.OutputIntentProfile);
            Assert.Equal("id", mapped.OutputIntentIdentifier);
            Assert.Equal(PeachDrawing.PDF.Engine.ColorBlackGeneration.UseRichBlack, mapped.BlackGeneration);
            Assert.Equal(original.FallbackCmykProfile, mapped.FallbackCmykProfile);
            Assert.Equal(PeachDrawing.PDF.Engine.ColorConversionMode.ConvertToProfile, mapped.ConversionMode);
            Assert.Equal(original.ConvertToProfile, mapped.ConvertToProfile);
            Assert.Equal(PeachDrawing.PDF.Engine.ColorRenderingIntent.Perceptual, mapped.RenderingIntent);
            Assert.False(mapped.UseBlackPointCompensation);
            Assert.Null(((PeachPDF.ColorOptions?)null).ToEngine());
        }

        [Fact]
        public void EncryptionOptions_MapsEveryProperty()
        {
            var original = new PeachPDF.PdfEncryptionOptions
            {
                UserPassword = "u",
                OwnerPassword = "o",
                Permissions = PeachPDF.PdfPermissions.ReadOnly,
                EncryptMetadata = false,
            };

            var mapped = original.ToEngine()!;

            Assert.Equal("u", mapped.UserPassword);
            Assert.Equal("o", mapped.OwnerPassword);
            Assert.Equal(PeachDrawing.PDF.Engine.PdfPermissions.ReadOnly, mapped.Permissions);
            Assert.False(mapped.EncryptMetadata);
            Assert.Null(((PeachPDF.PdfEncryptionOptions?)null).ToEngine());
        }

        [Fact]
        public void TheWritersInternalsAreGrantedToExactlyTheDocumentedSet()
        {
            // TEMPORARY: PeachPDF and its tests drive the PDF writer's internals while PeachPDF is moved onto a public API for it. Each step
            // of that move removes a dependence; the end state grants nothing but the writer's own tests.
            var granted = typeof(PeachDrawing.PDF.Units).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
                .Select(a => a.AssemblyName)
                .ToList();

            Assert.Equivalent(new[] { "PeachDrawing.PDF.Tests", "PeachPDF", "PeachPDF.Tests" }, granted);
        }

        private static string Describe(Type enumType) =>
            string.Join(",", Enum.GetNames(enumType).Select(n => n + "=" + Convert.ToInt64(Enum.Parse(enumType, n))));
    }
}
