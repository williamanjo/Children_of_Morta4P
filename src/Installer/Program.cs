using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Morta4P.Installer
{
    internal static class Program
    {
        private const string SteamAppId = "330020";
        private const string GameExe = "ChildrenOfMorta.exe";
        private const string GameProcess = "ChildrenOfMorta";
        private const string PluginFolder = @"BepInEx\plugins\Morta4P";

        // Files and folders the BepInEx zip puts in the game folder.
        private static readonly string[] BepInExFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" };

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Children of Morta 4P - Instalador";
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

            Console.WriteLine("==============================================");
            Console.WriteLine($"  Children of Morta 4P - Instalador v{version}");
            Console.WriteLine("  Co-op online para até 4 jogadores");
            Console.WriteLine("==============================================");
            Console.WriteLine();

            try
            {
                string gamePath = ParseArg(args, "--path") ?? FindGamePath();
                if (gamePath == null)
                {
                    Console.WriteLine("Não encontrei o Children of Morta nas bibliotecas da Steam.");
                    gamePath = AskForGamePath();
                    if (gamePath == null)
                    {
                        return Exit(1);
                    }
                }
                Console.WriteLine($"Jogo encontrado em:\n  {gamePath}\n");

                string action = ParseArg(args, "--action") ?? AskAction(gamePath);
                if (action == null)
                {
                    return Exit(0);
                }

                WaitForGameToClose();

                try
                {
                    if (action == "uninstall")
                    {
                        Uninstall(gamePath);
                    }
                    else if (action == "update")
                    {
                        Update(gamePath);
                    }
                    else
                    {
                        Install(gamePath);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return RestartAsAdmin(gamePath, action);
                }
                return Exit(0);
            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.WriteLine("Erro: " + e.Message);
                Console.WriteLine(e);
                return Exit(1);
            }
        }

        // --- Finding the game --------------------------------------------------------------

        private static string FindGamePath()
        {
            foreach (string library in FindSteamLibraries())
            {
                string manifest = Path.Combine(library, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                string installDir = null;
                if (File.Exists(manifest))
                {
                    Match match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (match.Success)
                    {
                        installDir = match.Groups[1].Value;
                    }
                }
                foreach (string name in new[] { installDir, "ChildrenOfMorta", "Children of Morta" })
                {
                    if (name == null)
                    {
                        continue;
                    }
                    string candidate = Path.Combine(library, "steamapps", "common", name);
                    if (IsGameFolder(candidate))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<string> FindSteamLibraries()
        {
            var roots = new List<string>();
            AddIfDir(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
            AddIfDir(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
            AddIfDir(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string);
            AddIfDir(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

            var libraries = new List<string>();
            foreach (string root in roots)
            {
                AddIfDir(libraries, root);
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf))
                {
                    continue;
                }
                // Both the old ("1" "D:\\Games") and new ("path" "D:\\Games") formats.
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"(?:path|\\d+)\"\\s+\"([^\"]+)\""))
                {
                    AddIfDir(libraries, match.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
            return libraries;
        }

        private static void AddIfDir(List<string> list, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            try
            {
                string full = Path.GetFullPath(path.Replace('/', '\\'));
                if (Directory.Exists(full) && !list.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(full);
                }
            }
            catch (Exception)
            {
                // Ignore malformed paths from the registry or the vdf.
            }
        }

        private static bool IsGameFolder(string path) =>
            !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, GameExe));

        private static string AskForGamePath()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Cole o caminho da pasta do jogo (onde fica o ChildrenOfMorta.exe)");
                Console.WriteLine("ou aperte Enter para sair:");
                Console.Write("> ");
                string input = Console.ReadLine()?.Trim().Trim('"');
                if (string.IsNullOrEmpty(input))
                {
                    return null;
                }
                if (File.Exists(input) && Path.GetFileName(input).Equals(GameExe, StringComparison.OrdinalIgnoreCase))
                {
                    input = Path.GetDirectoryName(input);
                }
                if (IsGameFolder(input))
                {
                    return Path.GetFullPath(input);
                }
                Console.WriteLine("Não achei o ChildrenOfMorta.exe nessa pasta.");
            }
        }

        // --- Actions -----------------------------------------------------------------------

        private static string AskAction(string gamePath)
        {
            string dll = Path.Combine(gamePath, PluginFolder, "Morta4P.dll");
            Version installed = InstalledVersion(dll);
            Console.WriteLine(installed != null
                ? $"Mod instalado: v{installed.ToString(3)}"
                : File.Exists(dll) ? "Mod instalado (versão 0.8.1 ou anterior)." : "O mod ainda não está instalado.");
            Console.WriteLine();
            Console.WriteLine("  [1] Instalar (BepInEx + mod mais recente)");
            Console.WriteLine("  [2] Atualizar mod (baixa a versão mais recente)");
            Console.WriteLine("  [3] Desinstalar o mod");
            Console.WriteLine("  [4] Sair");
            while (true)
            {
                Console.Write("> ");
                switch (Console.ReadLine()?.Trim())
                {
                    case "1":
                        return "install";
                    case "2":
                        return "update";
                    case "3":
                        return "uninstall";
                    case "4":
                    case "":
                    case null:
                        return null;
                }
            }
        }

        private static Version InstalledVersion(string dll)
        {
            try
            {
                if (!File.Exists(dll))
                {
                    return null;
                }
                Version version = AssemblyName.GetAssemblyName(dll).Version;
                // Builds up to 0.8.1 had no assembly version (1.0.0.0 by default).
                return version == new Version(1, 0, 0, 0) ? null : version;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WaitForGameToClose()
        {
            while (Process.GetProcessesByName(GameProcess).Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("O Children of Morta está aberto. Feche o jogo e aperte Enter para continuar.");
                if (Console.ReadLine() == null)
                {
                    throw new InvalidOperationException("O jogo continua aberto. Feche o jogo e rode o instalador de novo.");
                }
            }
        }

        private static void Install(string gamePath)
        {
            Console.WriteLine();
            InstallBepInEx(gamePath);
            InstallPlugin(gamePath);
        }

        private static void Update(string gamePath)
        {
            Console.WriteLine();
            if (!File.Exists(Path.Combine(gamePath, @"BepInEx\core\BepInEx.dll")))
            {
                Console.WriteLine("O BepInEx não está instalado; instalando junto.");
                InstallBepInEx(gamePath);
            }
            InstallPlugin(gamePath);
        }

        private static void InstallBepInEx(string gamePath)
        {
            if (File.Exists(Path.Combine(gamePath, @"BepInEx\core\BepInEx.dll")) && File.Exists(Path.Combine(gamePath, "winhttp.dll")))
            {
                Console.WriteLine("BepInEx já está instalado.");
                return;
            }
            Console.WriteLine("Instalando o BepInEx...");
            using (Stream zipStream = Resource("BepInEx.zip"))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(gamePath, entry.FullName));
                    if (!target.StartsWith(Path.GetFullPath(gamePath), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
        }

        // Downloads Morta4P.dll from the newest GitHub release; falls back to the copy
        // embedded in this installer when offline.
        private static void InstallPlugin(string gamePath)
        {
            string pluginDir = Path.Combine(gamePath, PluginFolder);
            string target = Path.Combine(pluginDir, "Morta4P.dll");
            Version before = InstalledVersion(target);
            byte[] previous = File.Exists(target) ? File.ReadAllBytes(target) : null;

            byte[] dll = null;
            string source;
            Console.WriteLine("Procurando a versão mais recente do mod no GitHub...");
            try
            {
                var latest = Releases.FindLatest();
                if (latest != null)
                {
                    Console.WriteLine($"Baixando o Morta4P {latest.Tag}...");
                    dll = Releases.Download(latest.DllUrl);
                    source = latest.Tag;
                }
                else
                {
                    source = null;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Não consegui baixar do GitHub: " + e.Message);
                source = null;
            }
            if (dll == null)
            {
                using (Stream embedded = Resource("Morta4P.dll"))
                using (var memory = new MemoryStream())
                {
                    embedded.CopyTo(memory);
                    dll = memory.ToArray();
                }
                source = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " (embutida no instalador)";
                Console.WriteLine("Usando a versão embutida no instalador.");
            }

            Directory.CreateDirectory(pluginDir);
            File.WriteAllBytes(target, dll);

            Console.WriteLine();
            if (previous != null && previous.SequenceEqual(dll))
            {
                Console.WriteLine($"O mod já estava na versão mais recente ({source}).");
            }
            else if (previous != null)
            {
                string from = before != null ? "v" + before.ToString(3) : "versão antiga";
                Console.WriteLine($"Mod atualizado: {from} -> {source}.");
            }
            else
            {
                Console.WriteLine($"Mod instalado: {source}.");
            }
            Console.WriteLine("Pronto! Abra o Children of Morta pela Steam.");
            Console.WriteLine("Lembre: todos os jogadores precisam da mesma versão do mod.");
        }

        private static void Uninstall(string gamePath)
        {
            Console.WriteLine();
            string pluginDir = Path.Combine(gamePath, PluginFolder);
            if (Directory.Exists(pluginDir))
            {
                Directory.Delete(pluginDir, recursive: true);
                Console.WriteLine("Mod Morta4P removido.");
            }
            else
            {
                Console.WriteLine("O mod Morta4P não estava instalado.");
            }

            string bepInEx = Path.Combine(gamePath, "BepInEx");
            if (!Directory.Exists(bepInEx))
            {
                return;
            }
            string plugins = Path.Combine(bepInEx, "plugins");
            bool otherMods = Directory.Exists(plugins) && Directory.EnumerateFileSystemEntries(plugins).Any();
            Console.WriteLine();
            if (otherMods)
            {
                Console.WriteLine("O BepInEx foi mantido porque há outros mods na pasta BepInEx\\plugins.");
                return;
            }
            Console.WriteLine("Remover também o BepInEx (carregador de mods)? Isso apaga a pasta BepInEx inteira,");
            Console.WriteLine("incluindo as configurações. [s/N]");
            Console.Write("> ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "s", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            Directory.Delete(bepInEx, recursive: true);
            foreach (string file in BepInExFiles)
            {
                string path = Path.Combine(gamePath, file);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            Console.WriteLine("BepInEx removido. O jogo voltou ao original.");
        }

        // --- Helpers -----------------------------------------------------------------------

        private static Stream Resource(string name) =>
            Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Recurso embutido ausente: " + name);

        private static string ParseArg(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        // The game folder can be under Program Files; retry elevated with the same choices.
        private static int RestartAsAdmin(string gamePath, string action)
        {
            Console.WriteLine();
            Console.WriteLine("Sem permissão para gravar na pasta do jogo. Vou pedir permissão de administrador.");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Assembly.GetExecutingAssembly().Location,
                    Arguments = $"--path \"{gamePath}\" --action {action}",
                    Verb = "runas",
                    UseShellExecute = true,
                });
                return 0;
            }
            catch (Exception)
            {
                Console.WriteLine("Permissão negada. Clique com o botão direito no instalador e escolha");
                Console.WriteLine("\"Executar como administrador\".");
                return Exit(1);
            }
        }

        private static int Exit(int code)
        {
            Console.WriteLine();
            Console.WriteLine("Aperte Enter para fechar.");
            Console.ReadLine();
            return code;
        }
    }
}
