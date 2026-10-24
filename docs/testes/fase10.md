# Teste da Fase 10: extras (atualização, celular e força)

Escolhidos pelo John: aviso de atualização e painel no celular (os dois **desligados de fábrica**), vibração extra
pela telemetria e reforço das forças fracas.

Já validado pelo Claude Code (22/10/2026):
- versão 1.0.0 publicada em https://github.com/Samosbourne/G27Hub/releases/tag/v1.0.0 (instalador de 50,6 MB);
- painel no celular: servidor testado (página abre, código errado é recusado, o toque no 540° chega ao app e volta
  na tela), visual conferido no tamanho de celular;
- vibração: cálculo testado (motor, pista/zebra, ABS, troca de marcha, batida); DLL e plugin do ETS2 recompilados;
- inglês completo; 102 testes automáticos.
- **Não testado no volante** (estava desconectado): a vibração e o reforço das forças fracas. Por isso começam aqui.

**Kaspersky (22/10, 18:10):** marcou a `g27ffb.dll` x64 recompilada como `VHO:Trojan-PSW.Win32.PPLdump.gen` e apagou
as cópias. Era falso positivo da análise heurística (o código é nosso e está no GitHub): disparou quando a DLL passou a
usar as funções de matemática do C (seno e potência). Tirei essas funções; a DLL nova (18:15) não foi mais marcada.
- [ ] Confira na Quarentena do Kaspersky que não há DLL nova do G27 Hub depois das 18:15. Se aparecer de novo, me avise
      (não clique em "Resolver": ele apaga a DLL e o force feedback dos jogos 64 bits para).

Antes de tudo: feche o app e abra de novo com `.\g27hub` (a DLL de força nova só vale para jogos abertos depois).

## 1. Reforço das forças fracas (aba Força)
- [ ] Abra o BeamNG com o perfil de sempre. Dirija devagar em linha reta numa estrada com pequenas ondulações.
- [ ] Aba Força → **Reforço das forças fracas** em 30%: os detalhes pequenos (textura, leve puxada do volante)
      devem ficar mais perceptíveis, sem mudar a força máxima nas curvas fortes.
- [ ] 0% volta a ser como antes. Me diga qual valor ficou melhor.

## 2. Vibração extra (aba Força → VIBRAÇÃO EXTRA)
Precisa da telemetria: BeamNG com o mod e ETS2 com o plugin (aba LEDs). O app atualiza os dois sozinho ao abrir;
no BeamNG vale a partir do próximo carro carregado (Ctrl+R recarrega). No ETS2, feche o jogo antes de abrir o app.
- [ ] Marque **Somar a vibração do jogo ao force feedback**. Embaixo deve aparecer "Recebendo telemetria: BeamNG.drive".
- [ ] **Motor**: parado em ponto morto, acelere: um tremor leve que acompanha o giro.
- [ ] **Pista, zebras e buracos**: passe por cima de uma zebra ou estrada de terra: o volante chacoalha mais.
- [ ] **ABS**: freie forte a uns 80 km/h num carro com ABS: pulsação rápida.
- [ ] **Troca de marcha**: cada marcha engatada dá um tranquinho curto.
- [ ] **Batidas**: bata num muro: um tranco forte.
- [ ] Cada slider em 0% desliga só aquele efeito; desmarcar a caixa desliga tudo.
- [ ] Repita o básico no ETS2 (motor, pista, troca de marcha; o ETS2 não manda ABS).
- [ ] Se algo ficou fraco ou forte demais, me diga qual e quanto (o limite de cada efeito é ajustável no código).

## 3. Painel no celular (engrenagem do app → PAINEL NO CELULAR)
- [ ] Marque **Ligar o painel no celular**. O Windows pode perguntar sobre o firewall: permita em **redes privadas**.
- [ ] Aparece um QR code. Aponte a câmera do celular (mesmo Wi-Fi do PC): abre a página do G27 Hub.
- [ ] Com o BeamNG aberto: RPM, marcha, velocidade e LEDs mudam ao vivo no celular.
- [ ] Toque em **540°**: o volante muda de rotação (e a aba Volante do app mostra 540°).
- [ ] Mude a **intensidade do force feedback** e o **perfil** pelo celular: o app acompanha.
- [ ] **Trocar código**: o celular antigo para de funcionar (precisa ler o QR de novo).
- [ ] Desmarcar a caixa desliga o painel.

## 4. Aviso de atualização (engrenagem do app)
- [ ] Marque **Avisar quando houver versão nova**: aparece "Você está com a versão mais nova" (a 1.0.0 já está no GitHub;
      esta é a 1.1.0, ainda não publicada).
- [ ] Depois que você aprovar esta fase, eu publico a 1.1.0 no GitHub. Quem tiver a 1.1.0 com a opção ligada recebe o
      aviso das próximas versões.

## 5. Configurar volante (pedido do John, 23/10/2026: G27 do colega com outro mapa)
O log do colega mostrou o volante em modo nativo, mas a embreagem parada em 50% (o eixo Y não mexe nele) e as marchas
em outros botões. Agora o app tem **Configurar volante**: o usuário mexe cada controle e o app aprende onde ele está.
Também grava no log, ao conectar, o firmware, os eixos e quantos botões o volante tem.

No seu volante:
- [ ] Ao conectar, **não** deve aparecer o aviso "Este G27 parece ter outro mapa" (o seu está no padrão).
- [ ] Tela inicial → **Configurar volante**: siga os 28 passos (volante, 3 pedais, 6 marchas, ré e os botões). Cada passo
      avança sozinho; **Pular** mantém o que estava. No fim, **Salvar**.
- [ ] Abas Pedais, Câmbio e Teste: tudo igual a antes (o seu mapa aprendido deve ser o mesmo do padrão).
- [ ] **Voltar ao de fábrica** volta ao padrão.

No volante do colega (mande para ele o `installer\out\G27Hub-Setup-1.1.0.exe` e peça para instalar por cima):
- [ ] Ao conectar deve aparecer "Este G27 parece ter outro mapa" com o botão **Configurar**.
- [ ] Depois de configurar: a embreagem vai de 0 a 100% e as marchas aparecem certas na aba Câmbio.
- [ ] Me mande o log dele de novo (`%LOCALAPPDATA%\G27Hub\logs`): as linhas "Firmware", "eixos em repouso" e
      "Mapa do volante salvo" me mostram exatamente o que muda no volante dele.

Me mande o resultado de cada item (pode ser "ok" ou o que estranhou).
