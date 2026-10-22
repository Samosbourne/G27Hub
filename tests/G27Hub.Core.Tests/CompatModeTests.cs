using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class CompatModeTests
{
    [Fact]
    public void Ponto_de_restauracao_vem_antes_de_qualquer_instalacao()
    {
        var s = CompatMode.InstallScript([(CompatMode.VJoy, @"C:\x\vjoy's.exe"), (CompatMode.HidHide, @"C:\x\hh.exe")], @"C:\log.txt");
        int restore = s.IndexOf("Checkpoint-Computer"), first = s.IndexOf("Install-One 'vJoy");
        Assert.True(restore > 0 && first > restore);
        Assert.Contains(@"'C:\x\vjoy''s.exe'", s); // aspas simples escapadas no PowerShell
        Assert.Contains("O=$publisher", s);
        Assert.Contains("'Brunner Elektronik AG'", s);
    }

    [Fact]
    public void Sha256_confere_o_arquivo()
    {
        var f = Path.GetTempFileName();
        File.WriteAllText(f, "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", CompatMode.Sha256(f));
        File.Delete(f);
    }
}

public class VirtualWheelTests
{
    [Theory]
    [InlineData(0.5, 50, 0.5)]
    [InlineData(-0.5, 50, -0.5)]
    [InlineData(1.0, 0, 1.0)]
    [InlineData(0.5, 0, 0.125)]   // menos sensível: começo mais suave
    [InlineData(0.0, 100, 0.0)]
    public void Curva_de_sensibilidade(double v, int sens, double expected) =>
        Assert.Equal(expected, VirtualWheel.Curve(v, sens), 3);

    [Fact]
    public void Mais_sensivel_responde_mais_no_comeco() =>
        Assert.True(VirtualWheel.Curve(0.2, 80) > 0.2 && VirtualWheel.Curve(0.2, 20) < 0.2);

    [Fact]
    public void Acha_o_G27_na_lista_do_HidHide()
    {
        const string json = """
            [ { "friendlyName" : "G27 Racing Wheel" , "devices" : [ { "deviceInstancePath" : "HID\\VID_046D&PID_C29B\\7&58e1df2&0&0000" } ] } ,
              { "friendlyName" : "vJoy" , "devices" : [ { "deviceInstancePath" : "HID\\HIDCLASS\\1&2d595ca7&0&0000" } ] } ]
            """;
        Assert.Equal([@"HID\VID_046D&PID_C29B\7&58e1df2&0&0000"], HidHide.ParseG27Paths(json));
    }

    [Fact]
    public void Ativar_configura_o_vjoy_depois_de_instalar()
    {
        var s = CompatMode.InstallScript([(CompatMode.VJoy, "v.exe")], "l");
        Assert.True(s.IndexOf("vJoyConfig.exe") > s.IndexOf("Install-One 'vJoy"));
        Assert.Contains(VirtualWheel.ConfigArgs, s);
    }
}

public class VJoyFfbTests
{
    [Theory]
    [InlineData(5000, 5000)]
    [InlineData(60536, -5000)]   // -5000 em 16 bits sem sinal
    [InlineData(-5000, -5000)]
    [InlineData(10000, 10000)]
    public void Valores_com_sinal_do_vjoy(int raw, int expected) => Assert.Equal(expected, VJoyFfb.S(raw));
}

public class WheelSpinnerTests
{
    [Fact]
    public void Empurra_para_o_lado_do_alvo_e_solta_ao_chegar()
    {
        var s = new WheelSpinner();
        Assert.True(s.Step(0, 90, 0.001) < 0);            // alvo à direita: força negativa = empurra para a direita
        s.Reset();
        Assert.True(s.Step(0, -90, 0.001) > 0);           // alvo à esquerda
        s.Reset();
        Assert.Equal(0, s.Step(45.5, 45, 0.001));         // dentro da tolerância e parado: solta
        s.Reset();
        Assert.True(Math.Abs(s.Step(0, 900, 0.001)) <= s.MaxForce); // força sempre limitada
    }
}
