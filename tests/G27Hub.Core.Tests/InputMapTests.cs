using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class InputMapTests
{
    // volante "diferente": embreagem no Slider, Y parado no meio, marchas nos botões 9–14 e ré no 15
    static G27State State(int x = 8192, int z = 255, int rz = 255, int slider = 255, params int[] buttons) =>
        new(new Dictionary<string, AxisValue>
        {
            ["X"] = new(x, 0, 16383), ["Y"] = new(127, 0, 255), ["Z"] = new(z, 0, 255), ["Rz"] = new(rz, 0, 255), ["Slider"] = new(slider, 0, 255),
        }, buttons, -1, 900);

    [Fact]
    public void Aprende_eixos_e_botoes_de_outro_volante()
    {
        var l = new InputMapLearner(new InputMap());
        l.Begin(State());

        // volante: girar um pouco não basta; todo para a esquerda, depois todo para a direita
        Assert.False(l.Feed(State(x: 6000)));
        Assert.True(l.Feed(State(x: 200)));
        Assert.Equal("X", l.Map.Steer);
        Assert.False(l.Feed(State(x: 300)));    // ainda na esquerda
        Assert.False(l.Feed(State(x: 12000)));  // no caminho
        Assert.True(l.Feed(State(x: 16300)));

        // acelerador: só conta depois de soltar
        Assert.False(l.Feed(State(z: 0)));
        Assert.True(l.Feed(State(z: 250)));
        Assert.Equal("Z", l.Map.Throttle);

        l.Feed(State(rz: 10));
        l.Feed(State(rz: 255));
        Assert.Equal("Rz", l.Map.Brake);

        l.Feed(State(slider: 5));
        Assert.True(l.Feed(State(slider: 255)));
        Assert.Equal("Slider", l.Map.Clutch); // o Y parado no meio não engana

        // marchas 1–6 nos botões 9–14 (passando pelo neutro), ré no 15 com a 6ª ainda "engatada"
        for (int g = 0; g < 6; g++)
        {
            Assert.True(l.Feed(State(buttons: 9 + g)));
            l.Feed(State());
        }
        Assert.Equal([9, 10, 11, 12, 13, 14], l.Map.Gears);
        Assert.True(l.Feed(State(buttons: [14, 15])));
        Assert.Equal(15, l.Map.Reverse);

        // o resto: pula tudo e mantém o padrão
        while (!l.Done) l.Skip(State());
        Assert.Equal(new InputMap().WheelRight, l.Map.WheelRight);
        Assert.False(l.Map.IsDefault);
    }

    [Fact]
    public void Segurar_um_botao_nao_conta_duas_vezes()
    {
        var l = new InputMapLearner(new InputMap());
        for (int i = 0; i < 5; i++) l.Skip(State()); // volante e pedais
        Assert.True(l.Feed(State(buttons: 13)));
        Assert.False(l.Feed(State(buttons: 13))); // 1ª ainda engatada: não vira a 2ª
        Assert.True(l.Feed(State(buttons: 14)));
        Assert.Equal(14, l.Map.Gears[1]);
    }
}
