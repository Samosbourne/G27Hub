using G27Hub.Core;
using static G27Hub.Core.Lang;

namespace G27Hub.Core.Tests;

public class LangTests
{
    [Fact]
    public void Traduz_frases_com_valores()
    {
        Set("en");
        try
        {
            int graus = 900;
            double speed = 86.4;
            string floor = "asfalto";
            Assert.Equal("Rotation: 900°", T($"Rotação: {graus}°"));
            Assert.Equal("86 km/h · surface: asphalt", T($"{speed:0} km/h · piso: {T(floor)}"));
            Assert.Equal("Back", T("Voltar"));
            Assert.DoesNotContain("86 km/h · surface: asphalt", Missing);
        }
        finally { Set("pt"); }
        Assert.Equal("Voltar", T("Voltar"));
    }

    [Fact]
    public void Todo_texto_tem_ingles()
    {
        var en = LoadEmbedded();
        Assert.True(en.Count > 400);
        Assert.All(en, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
        // os {0}, {1}... do português têm que existir no inglês
        Assert.All(en, kv =>
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(kv.Key, @"\{\d+(:[^}]*)?\}"))
                Assert.Contains(m.Value, kv.Value);
        });
    }
}
