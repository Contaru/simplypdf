namespace SimplyPdf.Tests;

public class TransformTests
{
    [Fact]
    public void Scale_keeps_six_decimals_of_the_factor()
    {
        // Three decimals would write 0.6 and shrink everything drawn afterwards by 0.025 %.
        var content = Content(page => page.Scale(0.6001514, 0.6001514));

        Assert.Contains("\n0.600151 0 0 0.600151 0 0 cm\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotate_by_a_small_angle_is_not_rounded_away()
    {
        // sin(0.05°) = 0.000873; three decimals would write 0.001, a 15 % larger angle.
        var content = Content(page => page.Rotate(0.05));

        Assert.Contains("\n1 0.000873 -0.000873 1 0 0 cm\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotate_by_a_quarter_turn_writes_clean_integers()
    {
        // cos(90°) is 6e-17 in floating point and must come out as 0, never as -0 or an exponent.
        var content = Content(page => page.Rotate(90, 50, 50));

        Assert.Contains("\n0 1 -1 0 100 0 cm\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Translation_is_a_coordinate_and_keeps_three_decimals()
    {
        var content = Content(page => page.Translate(10.12345, 20.5));

        Assert.Contains("\n1 0 0 1 10.123 20.5 cm\n", content, StringComparison.Ordinal);
    }

    private static string Content(Action<PdfPage> draw)
    {
        var doc = new PdfDocument { CompressStreams = false };
        draw(doc.AddPage());
        return new PdfProbe(doc.ToArray()).FirstPageContent();
    }
}
