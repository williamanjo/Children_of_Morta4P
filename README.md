# Children of Morta 4P

Mod para **Children of Morta** (Steam) que aumenta o co-op online de 2 para **até 4 jogadores**.

Status: **v0.9.2, experimental**. A seleção de personagem e o HUD já foram testados no solo com a prévia (F9), mas uma partida real com 3 ou 4 jogadores ainda não foi testada. Faça backup dos seus saves.

## Instalação

Faça isto **em todos os PCs** que vão jogar juntos.

### Jeito fácil: instalador

1. Feche o jogo.
2. Baixe o `Morta4P-Installer.exe` em [Releases](../../releases). Basta baixar uma vez: ele sempre busca a versão mais recente do mod no GitHub.
3. Execute. Ele procura o Children of Morta nas bibliotecas da Steam.
4. Escolha uma opção:
   - **[1] Instalar:** instala o BepInEx (se ainda não tiver) e a versão mais recente do mod.
   - **[2] Atualizar mod:** baixa e instala a versão mais recente do mod.
   - **[3] Desinstalar:** remove o mod e, se você quiser, o BepInEx também.

Observações:
- O Windows pode mostrar "O Windows protegeu o computador", porque o instalador não tem assinatura digital. Clique em **Mais informações → Executar assim mesmo**.
- Sem internet, ele instala a versão do mod que vem dentro do próprio instalador.
- Se ele não achar o jogo, cole o caminho da pasta do jogo quando ele pedir.
- Se o jogo estiver numa pasta protegida, ele pede permissão de administrador.

### Instalação manual

#### 1. Instalar o BepInEx

1. Baixe o **BepInEx 5.4.23.x para Windows x64**, o arquivo `BepInEx_win_x64_5.4.23.x.zip`, na [página de releases do BepInEx](https://github.com/BepInEx/BepInEx/releases).
2. Abra a pasta do jogo: na Steam, clique com o botão direito em Children of Morta → **Gerenciar** → **Navegar pelos arquivos locais**. Normalmente é `...\steamapps\common\ChildrenOfMorta`.
3. Extraia o conteúdo do zip **dentro dessa pasta**, ao lado do `ChildrenOfMorta.exe`. Devem aparecer `winhttp.dll`, `doorstop_config.ini` e a pasta `BepInEx`.
4. Abra o jogo uma vez e feche. Isso cria as pastas `BepInEx\plugins` e `BepInEx\config`.

#### 2. Instalar o mod

1. Baixe o `Morta4P.dll` da última versão em [Releases](../../releases).
2. Crie a pasta `BepInEx\plugins\Morta4P\` dentro da pasta do jogo e coloque o `Morta4P.dll` nela.
3. Abra o jogo pela Steam.

Para conferir se carregou, abra `BepInEx\LogOutput.log`. Deve aparecer a linha `Morta4P 0.9.2 loaded. MaxPlayers=4`.

### Jogar

1. O host entra no jogo e, na casa, convida os amigos pelo convite da Steam, **um de cada vez**.
2. Todos escolhem personagem. A partida só começa quando os 4 tiverem escolhido.
3. Todos precisam da **mesma versão do jogo e do mod**, e do mesmo `MaxPlayers` (veja abaixo). Quem estiver sem o mod pode ver status errados ou ter erro no lobby.

> **Personagens desbloqueados:** cada jogador precisa de um personagem diferente; dois jogadores não podem usar o mesmo. A família tem até 8 personagens (John, Mark, Kevin, Linda, Lucy, Joey, Apon e Bec), mas eles são desbloqueados ao longo da campanha.
>
> - Com 3 ou mais jogadores online, o mod libera **John, Linda, Mark e Kevin** na seleção, mesmo que o save ainda não tenha desbloqueado. O save não é alterado, e a cena da história que desbloqueia cada um continua acontecendo normalmente. Para desligar, use a opção `UnlockStarterCharacters`.
> - Os outros personagens (Lucy, Joey, Apon e Bec) só aparecem se já estiverem desbloqueados no save.
> - Em algumas passagens da história, parte da família vira companheiro controlado pelo jogo e sai da lista de escolha. Para ter mais opções, prefira um save mais avançado na campanha.

### Desinstalar

- Para remover só o mod: apague `BepInEx\plugins\Morta4P`.
- Para tirar tudo: apague também `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt` e a pasta `BepInEx`.

## Configuração

O arquivo `BepInEx\config\morta4p.cfg` é criado na primeira execução. Se existir o `com.william.morta4p.cfg` de versões anteriores, ele é renomeado automaticamente e os valores são mantidos.

| Seção | Opção | Padrão | O que faz |
|---|---|---|---|
| `General` | `MaxPlayers` | `4` | total de jogadores online, contando o host (2 a 4). Tem que ser igual em todos os PCs |
| `CharacterSelect` | `PanelScale` | `0.5` | tamanho dos painéis de personagem quando há mais de 2 jogadores |
| `Gameplay` | `XPSplitMax` | `2` | a XP de cada inimigo é dividida pelo número de jogadores, até este limite, e cada um recebe a parte. Com `2`, 10 de XP dá 5 para cada um dos 4. `0` divide por todos (original) |
| `CharacterSelect` | `UnlockStarterCharacters` | `true` | com 3 ou mais jogadores online, libera John, Linda, Mark e Kevin na seleção mesmo que o save não tenha desbloqueado. O save não é alterado |
| `CharacterSelect` | `PanelSpacing` | `4` | espaço entre o painel de cima e o de baixo |
| `HUD` | `Spacing` | `14` | espaço entre os HUDs dos parceiros |
| `Lobby` | `Spacing` | `4` | espaço extra entre os jogadores no widget do lobby (menu de pausa) |
| `HUD` | `StackDirection` | `Auto` | para onde os HUDs extras empilham: `Auto`, `Up`, `Down`, `Left` ou `Right` |
| `Debug` | `PreviewKey` | `F9` | prévia solo do layout para 4 jogadores |
| `Debug` | `ReloadKey` | `F10` | recarrega o arquivo de config sem fechar o jogo |

Com o **F9**, na seleção de personagem aparecem os 4 painéis e as marcações P3/P4 em dois botões. Dentro de uma partida aparecem 3 HUDs de parceiro, todos com o seu personagem. Com o menu de pausa aberto, o widget do lobby mostra 3 jogadores. Isso serve para ajustar o layout sem precisar de outros jogadores.

## O que o mod muda

- **Lobby:** a Steam aceita 4 membros, e o jogo aceita até 3 convidados em vez de 1 (`CheckLobbyIsFull` e `CheckIsJoiningPossible`).
- **Seleção de personagem:** cada convidado usa o número de jogador que o host atribuiu, em vez de ser sempre P2. O host pode continuar convidando enquanto houver vaga, e a partida espera todos escolherem. Com 3 ou mais jogadores, cada lado da tela mostra dois painéis: P1 em cima e P3 embaixo à esquerda, P2 em cima e P4 embaixo à direita.
- **XP:** a XP é dividida por 2 e cada jogador recebe a parte, mesmo com 3 ou 4 (10 de XP dá 5 para cada um). No jogo original, com 4, cada um ganharia só 2,5.
- **Personagens iniciais:** cada jogador precisa de um personagem diferente. Com 3 ou mais jogadores online, John, Linda, Mark e Kevin ficam liberados na seleção mesmo num save do começo da campanha. O desbloqueio da história continua acontecendo normalmente.
- **HUD:** cada jogador remoto ganha o próprio HUD de parceiro. No jogo original existia só um.
- **Status do lobby:** "Pronto", "Escolhendo personagem", "Na masmorra" e os outros status aparecem no HUD do jogador certo. O host repassa o status de cada convidado para os demais, então todo mundo vê o status de todo mundo.
- **Widget do lobby (menu de pausa):** mostra todos os jogadores remotos, um embaixo do outro, cada um com status e submenu de perfil.
- **Câmera:** sem mudança. No online, cada PC já segue só o próprio jogador. O tremor de câmera também chega para P3 e P4.
- **Revive:** todos os convidados veem o aviso de "reviver" e a seta de quem caiu, não só o host. Se o último jogador em pé sair da partida com o resto caído, a partida termina em vez de travar.
- **Itens:** relíquias, consumíveis e runas que o jogo dava para "os dois jogadores" agora valem para os quatro. A loja de cura cura cada jogador em pé uma vez, em vez de curar o comprador várias vezes.
- **Posições:** inícios de fase, passagens e esperas de cutscene que só tinham 2 posições ganham posições extras ao lado, em vez de fechar o jogo.
- **Casa:** cada convidado recebe do host só os próprios dados de cutscene e animações da casa, sem duplicar.

## Limitações conhecidas

- **Cutscenes:** as cenas foram animadas para 2 jogadores. P3 e P4 não têm papel nelas e podem ficar parados ou escondidos. Fora da cena, quem não é o "protagonista" pode aparecer no mesmo ponto que os outros.
- **Dificuldade:** o jogo não aumenta inimigos nem vida por número de jogadores. Com 4, a dificuldade é a mesma do co-op de 2. No Endless, o co-op só liga um ajuste fixo.
- **Companheiros controlados pelo jogo** (passagens, chefe final) usam o número de jogador P4 quando há 4 jogadores. O jogo original fecharia nesse caso.
- **Fim de fase do chefe:** a cutscene de saída verifica só o jogador do host. Isso já era assim no jogo original.

## Reportar problema

Se algo quebrar, abra uma issue anexando os dois logs do host e do jogador que teve o problema:

- `BepInEx\LogOutput.log`, na pasta do jogo
- `%USERPROFILE%\AppData\LocalLow\Dead Mage\Children of Morta\Player.log`

## Compilar

É preciso ter o .NET SDK 8 e o BepInEx instalado na pasta do jogo.

```bash
dotnet build src/Morta4P/Morta4P.csproj -c Release -p:GamePath="C:\caminho\para\ChildrenOfMorta"
```

`GamePath` aponta para a pasta do jogo, onde ficam as DLLs de referência. Depois do build, o DLL é copiado automaticamente para `BepInEx\plugins\Morta4P`.

Para gerar o instalador (compila o mod e embute o DLL e o zip do BepInEx, baixado na primeira vez):

```bash
dotnet build src/Installer/Installer.csproj -c Release -p:GamePath="C:\caminho\para\ChildrenOfMorta"
```

O executável fica em `src/Installer/bin/Release/net472/Morta4P-Installer.exe`.
