# PlayKross portable for Windows

## Para quem recebeu o ZIP

1. Extraia o ZIP inteiro para uma pasta gravavel, por exemplo `Documentos/PlayKross`. Nao execute dentro do ZIP.
2. Abra a pasta extraida e de dois cliques em **PlayKross.exe**. Nao precisa instalar .NET, Node.js, Visual Studio ou PowerShell 7 para usar o aplicativo.
3. Cole o convite HTTPS da sala, escolha seu nome e entre. Para transmitir, selecione a janela ou tela e inicie a transmissao.
4. Quem so quiser assistir abre o convite no navegador; nao precisa deste aplicativo.

Use Windows 11 x64 e mantenha a pasta inteira junta. O padrao usa NVIDIA NVENC H.264 com driver compativel. Em outro hardware, escolha explicitamente a alternativa por CPU; ela limita a saida a 720p30. O candidato M2 permite transmissores simultaneos, com uma publicacao por sessao. Nao ha capacidade maxima comprovada; a aceitacao funcional continua pendente.

O audio comeca **Off**. **Application** captura o processo selecionado e seus filhos. **System output** exige video de monitor e um dispositivo de saida escolhido explicitamente; inclui a reproducao desse dispositivo, inclusive Discord. O som da sala dentro do aplicativo fica mudo durante essa captura para evitar recaptura. Navegadores externos ainda podem ser capturados: use outra saida ou Application. Escolher uma tela nao habilita audio automaticamente. Nao ha microfone nem troca automatica de modo/dispositivo quando a fonte desaparece. A exclusao rigorosa de outros sons nao e garantida.

O convite precisa apontar para um servidor acessivel pela internet. Um endereco `localhost` enviado a um amigo nao aponta para seu computador. Este ZIP e o aplicativo de transmissao/visualizacao; o responsavel pela sala continua executando o servidor separadamente.

O Windows pode precisar do [Microsoft Visual C++ Redistributable x64](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist) para componentes nativos e do [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2) para assistir dentro do aplicativo. Consulte as [instrucoes oficiais do WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution). O pacote nao instala esses componentes nem altera seu Windows automaticamente.

Depois de usar o aplicativo, sua pasta pode conter estado local, perfis, caches ou registros. **Para compartilhar com outra pessoa, envie o ZIP original**, sem compactar novamente a pasta usada. Convites sao privados.

## Quick start

Extract the entire ZIP into a writable folder, open that folder, and double-click **PlayKross.exe**. Paste the room's public HTTPS invitation, choose your name, and join. Select a video source before starting a broadcast. Windows 11 x64 is required; .NET is bundled. SDKs, Node.js, Visual Studio and PowerShell 7 are unnecessary for recipients. Browser-only viewers just open the invitation.

The default encoder needs compatible NVIDIA NVENC support. CPU fallback is an explicit choice capped at 720p30. Audio starts Off. Application captures the explicitly selected process and children. System output requires monitor video plus a concrete render endpoint and includes its mixed playback, including Discord. In-app room playback is muted during system sharing; external browser playback may still be recaptured. No microphone or automatic route fallback is provided. M2 supports concurrent publications with one publication per session and no fixed room participant/publisher quotas; no maximum capacity is proven. Functional acceptance remains pending in the repository's M2 results. Fullscreen hides bars/cursor immediately, movement reveals them, and two seconds of inactivity hides them again; keyboard/touch access remains available. The host runs separately; this ZIP does not start an internet server. WebView2 Evergreen and Visual C++ x64 runtime may need installation using the links above. Share the original ZIP, because a used folder can contain private state.

## Build this ZIP from the repository

These steps are for the maintainer, using the existing Windows development setup in [windows.md](https://github.com/vitor-tecc/PlayKross/blob/main/docs/setup/windows.md). They do not launch the app, capture media or run live tests.

First prepare the pinned OBS archive, .NET SDK and native instrumentation using the repository setup/build instructions. The instrumented capture DLL must be `.artifacts/instrumented-obs/Release/libobs-winrt.dll`, accompanied by `provenance.json` matching the current capture patch, source archive and binary. Then run from the repository root in PowerShell 7.4+:

```powershell
./scripts/Package-Portable.ps1 -Version milestone-2-candidate-UNIQUE
```

The command builds the owned plugin in Release without installing it into the active development OBS runtime, publishes a self-contained `win-x64` WPF app named `PlayKross.exe`, stages a clean runtime, and creates these files in `.artifacts/releases`:

- `PlayKross-win-x64-milestone-2-candidate-UNIQUE.zip`
- `PlayKross-win-x64-milestone-2-candidate-UNIQUE.sha256`
- `PlayKross-win-x64-milestone-2-candidate-UNIQUE.manifest.json`, containing the archive size/hash and every included file's relative path, size and SHA-256.

Use a new `-Version` for another package; existing releases are never overwritten. `-SkipNativeBuild` reuses `.artifacts/native/Release/jk-obs.dll` only when you have already built the current sources. `-KeepStaging` keeps successful staging for inspection. Failed builds retain logs and staging and do not produce a verified release ZIP. Packaging may restore missing .NET runtime packs through the configured NuGet source.

Portable publishing sets `PortableBuild=true` and uses locked restore with the checked-in `packages.portable.lock.json` beside each application/library project. These files contain the `win-x64` restore targets. Regular development and solution restores continue using `packages.lock.json` without runtime-specific targets, so packaging does not rewrite the default dependency locks. To verify both configurations without publishing or launching anything:

```powershell
./.tools/dotnet/dotnet.exe restore JKiller.sln --locked-mode --artifacts-path .artifacts/locked-package-restore
./.tools/dotnet/dotnet.exe restore src/JKiller.App -r win-x64 -p:PortableBuild=true --locked-mode
```

The package uses the hash-verified OBS 32.2.2 Windows archive, excluding debug symbols. Only the owned `jk-obs.dll` and instrumented `libobs-winrt.dll` replace/add binaries. Configuration comes from the small checked-in OBS template allowlist. Active `.runtime/obs/config`, sessions, caches, logs, invitations, Git, development tools and build caches are never copied. Clean OBS data and bundled third-party notices are preserved. Archive entries, source reparse points, the file allowlist and every compressed file hash are checked before release.

Optional isolated packaging-boundary checks (no applications or media):

```powershell
./tests/setup/PortablePackageChecks.ps1
```

This ZIP does not replace the existing host setup; see [internet hosting](https://github.com/vitor-tecc/PlayKross/blob/main/docs/setup/internet.md).

## Source and notices

The source repository is [vitor-tecc/PlayKross](https://github.com/vitor-tecc/PlayKross). `BUILD-INFO.json` records the checkout's base commit and binary provenance. Native source snapshots in `Source/native`, the capture patch, build scripts, the verified upstream OBS 32.2.2 source archive and the pinned nlohmann JSON header accompany the modified native components. The snapshot can contain changes beyond the recorded base commit. License texts and notices are preserved in `Licenses`, source headers and the upstream source archive.

To rebuild those native components, use the repository's development layout: copy `Source/native`, `Source/patches`, `Source/scripts` and `Source/config` into the matching repository paths; place the included OBS source tarball in `.tools/downloads` and JSON header under `.tools/json/include/nlohmann`; obtain the pinned clean OBS binary with `Setup-Dev.ps1 -Dependencies obs`. Run `Build-Native.ps1 -Plugin -NoInstall -Configuration Release` for the plugin and `Build-InstrumentedObs.ps1` for WGC instrumentation. The latter installs instrumentation only into that checkout's owned OBS runtime. See `Source/native/instrumented-obs/NOTICE.md` for the scope of the capture modification.
