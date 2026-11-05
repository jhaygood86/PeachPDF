using PeachDrawing.Text.Internal.Fonts.OpenType;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Tests;

/// <summary>The AAT tracking table: size-dependent space per glyph, as Apple's system fonts carry.</summary>
public class TrackingTableTests
{
    // A table with one track (value 0) listing tracking at 10, 20 and 40 points.
    private static byte[] Table(int trackFixed = 0, (double Size, short Value)[]? sizes = null, ushort horizontalOffset = 12)
    {
        sizes ??= [(10, 30), (20, -10), (40, -50)];
        var data = new byte[46];
        BinaryPrimitives.WriteUInt32BigEndian(data, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), horizontalOffset);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(14), (ushort)sizes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 28);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), trackFixed);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(26), 40);
        for (var i = 0; i < sizes.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28 + i * 4), (int)(sizes[i].Size * 65536));
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(40 + i * 2), sizes[i].Value);
        }

        return data;
    }

    [Theory]
    [InlineData(5, 30)]       // held below the first size
    [InlineData(10, 30)]
    [InlineData(15, 10)]      // halfway between 30 and -10
    [InlineData(20, -10)]
    [InlineData(30, -30)]     // halfway between -10 and -50
    [InlineData(40, -50)]
    [InlineData(100, -50)]    // held above the last
    public void The_tracking_is_interpolated_between_the_listed_sizes_and_held_at_the_ends(double size, double expected)
    {
        var data = Table();
        var table = TrackingTable.TryRead(data, 0, data.Length);

        Assert.NotNull(table);
        Assert.Equal(expected, table!.At(size), 6);
    }

    [Fact]
    public void The_track_nearest_to_normal_is_used()
    {
        var data = Table(trackFixed: 65536);   // the only track is +1, not 0: still the nearest one
        Assert.NotNull(TrackingTable.TryRead(data, 0, data.Length));
    }

    [Fact]
    public void A_table_at_an_offset_in_a_larger_file_is_read_there()
    {
        var table = Table();
        var file = new byte[100 + table.Length];
        table.CopyTo(file, 100);

        Assert.Equal(-10, TrackingTable.TryRead(file, 100, table.Length)!.At(20), 6);
    }

    [Fact]
    public void Several_sizes_at_one_point_do_not_divide_by_zero()
    {
        var data = Table(sizes: [(10, 5), (10, 9), (20, 1)]);
        Assert.True(double.IsFinite(TrackingTable.TryRead(data, 0, data.Length)!.At(10)));
    }

    [Fact]
    public void Tables_that_are_wrong_are_refused()
    {
        var good = Table();
        Assert.Null(TrackingTable.TryRead(good, 0, 8));                                   // too short
        Assert.Null(TrackingTable.TryRead(Table(horizontalOffset: 0), 0, 46));            // no horizontal data
        Assert.Null(TrackingTable.TryRead(Table(horizontalOffset: 200), 0, 46));          // offset outside the table
        Assert.Null(TrackingTable.TryRead(good, 0, 30));                                  // size table runs off the end

        var badVersion = Table();
        badVersion[1] = 9;
        Assert.Null(TrackingTable.TryRead(badVersion, 0, 46));

        var noTracks = Table();
        noTracks[13] = 0;
        Assert.Null(TrackingTable.TryRead(noTracks, 0, 46));

        var valuesOutside = Table();
        BinaryPrimitives.WriteUInt16BigEndian(valuesOutside.AsSpan(26), 44);              // the values start too near the end
        Assert.Null(TrackingTable.TryRead(valuesOutside, 0, 46));
    }

    [Fact]
    public void A_font_without_a_tracking_table_asks_for_no_tracking()
    {
        var fonts = new FontSet();
        fonts.AddData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")), new AddOptions { FamilyName = "Tracking Test" });
        Assert.True(fonts.TryFindFamily("Tracking Test", out var family));
        Assert.True(family.TryMatch(TypefaceQuery.From(400, 100, false, null, null, null), out var match));

        Assert.Equal(0, match.Typeface.GetTracking(15));
    }
}
