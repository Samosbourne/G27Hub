using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class G27CommandsTests
{
    [Fact]
    public void NativeModeSequence()
    {
        Assert.Equal(new byte[] { 0xF8, 0x0A, 0, 0, 0, 0, 0 }, G27Commands.RevertOnReset);
        Assert.Equal(new byte[] { 0xF8, 0x09, 0x04, 0x01, 0, 0, 0 }, G27Commands.NativeMode);
    }

    [Theory]
    [InlineData(900, 0x84, 0x03)]
    [InlineData(540, 0x1C, 0x02)]
    [InlineData(200, 0xC8, 0x00)]
    [InlineData(5000, 0x84, 0x03)] // acima do máximo → 900
    [InlineData(10, 0x28, 0x00)]   // abaixo do mínimo → 40
    public void RangeIsLittleEndianAndClamped(int degrees, byte lsb, byte msb) =>
        Assert.Equal(new byte[] { 0xF8, 0x81, lsb, msb, 0, 0, 0 }, G27Commands.Range(degrees));

    [Fact]
    public void LedsUseOnlyFiveBits()
    {
        Assert.Equal(new byte[] { 0xF8, 0x12, 0x1F, 0, 0, 0, 0 }, G27Commands.Leds(0xFF));
        Assert.Equal(new byte[] { 0xF8, 0x12, 0x05, 0, 0, 0, 0 }, G27Commands.Leds(0b00101));
    }

    [Fact]
    public void AutocenterZeroTurnsOff() =>
        Assert.Equal(new[] { G27Commands.AutocenterOff }, G27Commands.Autocenter(0));

    [Fact]
    public void AutocenterFullAndHalf()
    {
        var full = G27Commands.Autocenter(100);
        Assert.Equal(G27Commands.AutocenterOff, full[0]); // desliga antes de reconfigurar
        Assert.Equal(new byte[] { 0xFE, 0x0D, 0x07, 0x07, 0xFF, 0, 0 }, full[1]);
        Assert.Equal(new byte[] { 0x14, 0, 0, 0, 0, 0, 0 }, full[2]);

        Assert.Equal(new byte[] { 0xFE, 0x0D, 0x04, 0x04, 0x5F, 0, 0 }, G27Commands.Autocenter(50)[1]);
    }

    [Theory]
    [InlineData(0, 0x80)]
    [InlineData(100, 0xFF)]
    [InlineData(-100, 0x01)]
    [InlineData(50, 0xBF)]
    public void ConstantForceLevel(int percent, byte level) =>
        Assert.Equal(new byte[] { 0x11, 0x08, level, 0x80, 0, 0, 0 }, G27Commands.ConstantForce(percent));

    [Theory]
    [InlineData(new int[0], "N")]
    [InlineData(new[] { 9, 10, 11, 12 }, "N")] // vermelhos do câmbio não são marcha
    [InlineData(new[] { 13 }, "1")]
    [InlineData(new[] { 15, 2 }, "3")]
    [InlineData(new[] { 18 }, "6")]
    [InlineData(new[] { 23 }, "R")]
    public void GearFromButtons(int[] buttons, string gear) =>
        Assert.Equal(gear, new G27State(new Dictionary<string, AxisValue>(), buttons, -1, 900).Gear);

    [Fact]
    public void PedalsAreInvertedAndMapped()
    {
        var axes = new Dictionary<string, AxisValue>
        {
            ["Z"] = new(0, 0, 255),   // acelerador no fundo
            ["Rz"] = new(255, 0, 255), // freio solto
            ["Y"] = new(128, 0, 255),  // embreagem pela metade
        };
        var s = new G27State(axes, [], -1, 900);
        Assert.Equal(1, s.Throttle, 3);
        Assert.Equal(0, s.Brake, 3);
        Assert.Equal(0.498, s.Clutch, 3);
    }

    [Theory]
    [InlineData(0.5, 900, 0)]
    [InlineData(0.0, 900, -450)]
    [InlineData(1.0, 900, 450)]
    [InlineData(0.75, 540, 135)]
    public void AngleFromAxis(double normalized, int range, double expected) =>
        Assert.Equal(expected, G27State.AngleFrom(normalized, range), 3);
}
