# G27 Hub

App para Windows 10/11 que faz pelo **Logitech G27** o que o Logitech Gaming Software 5.10 fazia, com um visual inspirado no G HUB.
**Idealizado e desenvolvido por John Salim / Samosbourne, junto com IA** (Claude, da Anthropic): o código deste projeto
foi escrito com inteligência artificial, a partir das ideias, decisões e testes no volante do John.

> **English:** G27 Hub brings back what Logitech Gaming Software 5.10 did for the Logitech G27 on Windows 10/11
> (native mode, 900°, force feedback without a kernel driver, per-game profiles, button assignments, RPM LEDs),
> with a G HUB-like look. The app is available in Portuguese and English (globe button at the top).

## Baixar / Download
Baixe o `G27Hub-Setup-<versão>.exe` em **[Releases](https://github.com/Samosbourne/G27Hub/releases/latest)** e instale
(por usuário, sem administrador). Como o instalador não é assinado, o Windows pode mostrar "O Windows protegeu o computador":
clique em **Mais informações → Executar assim mesmo**.
Download `G27Hub-Setup-<version>.exe` from **Releases** and run it (per-user, no admin needed).

Projeto pessoal, sem relação com a Logitech. "G27" e "Logitech" são marcas da Logitech; nenhum código, ícone ou imagem do LGS/G HUB foi usado.

## O que faz
- **Modo nativo automático** a cada vez que o volante é ligado: 900°, pedais separados, embreagem e câmbio H.
- **Force feedback** para os jogos (32 e 64 bits) por um driver de efeitos DirectInput que roda **em modo usuário**:
  sem driver de kernel, funciona com a Integridade de Memória ligada. Intensidade, mola, amortecedor, força mínima,
  força central e inverter, por perfil.
- **Perfis por jogo**, trocados sozinhos quando o jogo abre; cadeado para fixar um perfil.
- **Atribuições**: teclas, combinações, macros, mídia, abrir programa/site, camada shift, pedal como tecla,
  câmbio H como sequencial, atalho de rotação no volante.
- **LEDs de RPM** com a telemetria dos jogos: BeamNG.drive (mod próprio), ETS2/ATS (plugin próprio), Assetto Corsa/ACC,
  F1 22–25, Forza e Live for Speed.
- **Detector de jogos** (opcional) com base de compatibilidade e dicas por jogo; correção automática dos controles do BeamNG.
- **Modo Compatibilidade Total** (opcional, desligado de fábrica): vJoy + HidHide para jogos que não aceitam o G27,
  com sensibilidade, pedais combinados e force feedback de volta ao G27.
- **Calibração dos pedais** gravada no próprio Windows (vale para todos os jogos) e **assistente da primeira abertura**.
- **Console de diagnóstico** (Ctrl+Shift+L) com log de tudo e testes rápidos; minijogo 3D para testar o force feedback.

## Como usar (desenvolvimento)
Requisitos: .NET 10 SDK e, para mexer no código nativo, Visual Studio Build Tools com C++ (o CMake vem junto).

```
.\src\native\build.ps1   # DLL de force feedback, g27fftest e plugin do ETS2 (x64/x86)
dotnet build
.\g27hub                 # abre o app
.\g27cli list            # ferramenta de linha de comando (veja .\g27cli sem argumentos)
dotnet test
```

Dados do usuário: `%APPDATA%\G27Hub\config.json`; logs: `%LOCALAPPDATA%\G27Hub\logs\`.
Tudo o que o Modo Padrão grava fica em HKCU (sem administrador) e pode ser desfeito pelo próprio app.

Documentação: `PLANO_G27HUB.md` (plano), `docs/protocolo-g27.md` (protocolo HID do G27), `docs/testes/` (roteiros de teste).

## Créditos
- **gcampa/g27-ffb-driverless** (MIT): base do driver de efeitos DirectInput em `src/native/g27ffb` (ver `LICENSE-gcampa.txt`).
- **SCS Software, SCS SDK** (MIT): cabeçalhos do SDK de telemetria em `src/native/scssdk` (ver `LICENSE-scssdk.txt`).
- **vJoy** (Shaul Eizikovich, njz3, BrunnerInnovation): volante virtual do Modo Compatibilidade Total, instalado pelo
  instalador oficial; estruturas de force feedback seguindo o SDK (MIT).
- **HidHide** (Nefarius Software Solutions): esconde o G27 dos jogos no Modo Compatibilidade Total, instalado pelo instalador oficial.
- **HidSharp** (James F. Bellinger, Apache 2.0): acesso HID ao volante.
- **Serilog** (Apache 2.0): log em arquivo.
- **Linux hid-lg4ff**: usado só como referência dos fatos do protocolo do G27 (nenhum código GPL copiado).
- Ideias de funcionalidades: **Jabelius/OpenG27** (MIT).

## Licença
MIT (ver `LICENSE`). © 2026 John Salim (Samosbourne).
