using System.Windows;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>Fase 9: botão ⓘ do canto inferior direito, com dicas curtas da aba aberta.</summary>
public partial class MainWindow
{
    static readonly Dictionary<string, (string Title, string Text)> Tips = new()
    {
        ["volante"] = (T("Volante"),
            T("• Rotação (40–900°): até onde o volante gira. Deixe igual ao ângulo configurado no jogo.\n") +
            T("• Mola centralizadora: puxa o volante para o centro fora do jogo. Jogos com force feedback desligam a mola enquanto estão abertos.\n") +
            T("• Segure o desenho do volante com o mouse e gire: o G27 de verdade gira junto.\n") +
            T("• TESTAR VOLANTE: como no LGS, gira de ponta a ponta devagar e depois de uma vez, e volta ao centro.\n") +
            T("• Atalho: segure os dois vermelhos de cima do volante para trocar entre 180/270/540/900°.\n") +
            T("• Sensibilidade: só no Modo Compatibilidade Total.")),
        ["pedais"] = (T("Pedais"),
            T("• As barras mostram quanto cada pedal está pisado agora.\n") +
            T("• Calibrar pedais: se um pedal não chega a 100% ou não volta a 0%, o assistente corrige para todos os jogos.\n") +
            T("• Pedal como tecla: ex.: embreagem vira freio de mão em jogos de rally.\n") +
            T("• Sensibilidade e pedais combinados: no Modo Compatibilidade Total.")),
        ["forca"] = (T("Força (force feedback)"),
            T("• Intensidade global: força total que os jogos mandam ao G27.\n") +
            T("• Força mínima: pula a zona morta do centro do G27, onde força pequena não mexe o volante.\n") +
            T("• Mola / amortecedor: intensidade desses efeitos quando o jogo usa.\n") +
            T("• Força central: mola extra somada à do jogo.\n") +
            T("• Inverter força: use só se o volante empurrar para o lado errado num jogo.\n") +
            T("• O gráfico mostra a força que o jogo está mandando; vermelho = saturou (100%).\n") +
            T("• TESTAR FORCE FEEDBACK: minijogo para sentir os efeitos.")),
        ["cambio"] = (T("Câmbio"),
            T("• O desenho acende a marcha engatada e os botões apertados.\n") +
            T("• Câmbio como sequencial: na aba Atribuições, cada troca de marcha vira uma tecla (para jogos sem câmbio H).")),
        ["leds"] = (T("LEDs de RPM"),
            T("• Os LEDs acendem com a rotação do motor do jogo. Funciona com o app na bandeja.\n") +
            T("• Progressivo: enche e pisca no ponto de troca. Ponto de troca: só pisca. Limitador: pisca no corte.\n") +
            T("• BeamNG e ETS2/ATS precisam de um pequeno complemento: use os botões INSTALAR desta tela.\n") +
            T("• F1, Forza e Live for Speed: ligue a telemetria UDP no jogo (veja as instruções nesta tela).")),
        ["atribuicoes"] = (T("Atribuições"),
            T("• Clique num botão no desenho (ou aperte no volante) e escolha o que ele faz: tecla, combinação, macro, mídia, abrir programa ou site, trocar de perfil.\n") +
            T("• Camada shift: segurar um botão dobra as funções dos outros.\n") +
            T("• Dentro do G27 Hub as teclas ficam pausadas, para não atrapalhar a configuração.")),
        ["teste"] = (T("Teste"),
            T("• Tudo ao vivo: volante, pedais, câmbio, direcional e cada botão.\n") +
            T("• REAPLICAR MODO NATIVO E ROTAÇÃO: use se o volante voltar a girar só 200°.")),
    };

    void Info_Click(object sender, RoutedEventArgs e)
    {
        var (title, text) = Tips.GetValueOrDefault(CurrentSection, (T("G27 Hub"), T("Escolha uma aba na barra da esquerda.")));
        InfoTitle.Text = title;
        InfoText.Text = text;
        InfoPopup.IsOpen = true;
    }
}
