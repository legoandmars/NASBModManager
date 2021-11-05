using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using ModAssistant.Libs;
using static ModAssistant.Http;
using TextBox = System.Windows.Controls.TextBox;

namespace ModAssistant.Pages
{
    /// <summary>
    /// Interaction logic for Mods.xaml
    /// </summary>
    public sealed partial class Mods : Page
    {
        public static Mods Instance = new Mods();

        //public List<string> DefaultMods = new List<string>() { "SongCore", "ScoreSaber", "BeatSaverDownloader", "BeatSaverVoting", "PlaylistManager", "ModelDownloader" };
        public List<string> DefaultMods = new List<string>() { "BepInExPack_NASB" };
        public Mod[] ModsList;
        public Mod[] AllModsList;
        public static List<Mod> InstalledMods = new List<Mod>();
        public static List<Mod> LibsToMatch = new List<Mod>();
        public List<string> CategoryNames = new List<string>();
        public CollectionView view;
        public bool PendingChanges;

        private readonly SemaphoreSlim _modsLoadSem = new SemaphoreSlim(1, 1);

        public List<ModListItem> ModList { get; set; }

        public Mods()
        {
            InitializeComponent();
        }

        private void RefreshModsList()
        {
            if (view != null)
            {
                view.Refresh();
            }
        }

        public void RefreshColumns()
        {
            if (MainWindow.Instance.Main.Content != Instance) return;
            double viewWidth = ModsListView.ActualWidth;
            double totalSize = 0;
            GridViewColumn description = null;

            if (ModsListView.View is GridView grid)
            {
                foreach (var column in grid.Columns)
                {
                    if (column.Header?.ToString() == FindResource("Mods:Header:Description").ToString())
                    {
                        description = column;
                    }
                    else
                    {
                        totalSize += column.ActualWidth;
                    }
                    if (double.IsNaN(column.Width))
                    {
                        column.Width = column.ActualWidth;
                        column.Width = double.NaN;
                    }
                }
                double descriptionNewWidth = viewWidth - totalSize - 35;
                description.Width = descriptionNewWidth > 200 ? descriptionNewWidth : 200;
            }
        }

        public async Task LoadMods()
        {
            var versionLoadSuccess = await MainWindow.Instance.VersionLoadStatus.Task;
            if (versionLoadSuccess == false) return;

            await _modsLoadSem.WaitAsync();

            try
            {
                MainWindow.Instance.InstallButton.IsEnabled = false;
                MainWindow.Instance.GameVersionsBox.IsEnabled = false;
                MainWindow.Instance.InfoButton.IsEnabled = false;

                if (ModsList != null)
                {
                    Array.Clear(ModsList, 0, ModsList.Length);
                }

                if (AllModsList != null)
                {
                    Array.Clear(AllModsList, 0, AllModsList.Length);
                }

                InstalledMods = new List<Mod>();
                CategoryNames = new List<string>();
                ModList = new List<ModListItem>();

                ModsListView.Visibility = Visibility.Hidden;

                /*
                if (App.CheckInstalledMods)
                {
                    MainWindow.Instance.MainText = $"{FindResource("Mods:CheckingInstalledMods")}...";
                    await Task.Run(async () => await CheckInstalledMods());
                    InstalledColumn.Width = double.NaN;
                    UninstallColumn.Width = 70;
                    DescriptionColumn.Width = 750;
                }
                else
                {
                    InstalledColumn.Width = 0;
                    UninstallColumn.Width = 0;
                    DescriptionColumn.Width = 800;
                }
                */

                InstalledColumn.Width = 0;
                UninstallColumn.Width = 70;

                MainWindow.Instance.MainText = $"{FindResource("Mods:LoadingMods")}...";
                await Task.Run(async () => await PopulateModsList());

                ModsListView.ItemsSource = ModList;

                view = (CollectionView)CollectionViewSource.GetDefaultView(ModsListView.ItemsSource);
                PropertyGroupDescription groupDescription = new PropertyGroupDescription("Category");
                view.GroupDescriptions.Add(groupDescription);

                this.DataContext = this;

                RefreshModsList();
                ModsListView.Visibility = ModList.Count == 0 ? Visibility.Hidden : Visibility.Visible;
                NoModsGrid.Visibility = ModList.Count == 0 ? Visibility.Visible : Visibility.Hidden;

                MainWindow.Instance.MainText = $"{FindResource("Mods:FinishedLoadingMods")}.";
                MainWindow.Instance.InstallButton.IsEnabled = ModList.Count != 0;
                MainWindow.Instance.GameVersionsBox.IsEnabled = true;
            }
            finally
            {
                _modsLoadSem.Release();
            }
        }

        public async Task CheckInstalledMods()
        {
            await GetAllMods();

            //GetBSIPAVersion();
            CheckInstallDir("IPA/Pending/Plugins");
            CheckInstallDir("IPA/Pending/Libs");
            CheckInstallDir("Plugins");
            CheckInstallDir("Libs");
        }

        public async Task GetAllMods()
        {
            /*var resp = await HttpClient.GetAsync(Utils.Constants.BeatModsAPIUrl + "mod");
            var body = await resp.Content.ReadAsStringAsync();
            AllModsList = JsonSerializer.Deserialize<Mod[]>(body);*/
        }

        private void CheckInstallDir(string directory)
        {
            if (!Directory.Exists(Path.Combine(App.BeatSaberInstallDirectory, directory)))
            {
                return;
            }

            foreach (string file in Directory.GetFileSystemEntries(Path.Combine(App.BeatSaberInstallDirectory, directory)))
            {
                string fileExtension = Path.GetExtension(file);

                if (File.Exists(file) && (fileExtension == ".dll" || fileExtension == ".manifest"))
                {
                    Mod mod = GetModFromHash(Utils.CalculateMD5(file));
                    if (mod != null)
                    {
                        if (fileExtension == ".manifest")
                        {
                            LibsToMatch.Add(mod);
                        }
                        else
                        {
                            if (directory.Contains("Libs"))
                            {
                                if (!LibsToMatch.Contains(mod)) continue;

                                LibsToMatch.Remove(mod);
                            }

                            AddDetectedMod(mod);
                        }
                    }
                }
            }
        }

        public void GetBSIPAVersion()
        {
            string InjectorPath = Path.Combine(App.BeatSaberInstallDirectory, "Beat Saber_Data", "Managed", "IPA.Injector.dll");
            if (!File.Exists(InjectorPath)) return;

            string InjectorHash = Utils.CalculateMD5(InjectorPath);
            /*foreach (Mod mod in AllModsList)
            {
                if (mod.name.ToLowerInvariant() == "bsipa")
                {
                    foreach (Mod.DownloadLink download in mod.downloads)
                    {
                        foreach (Mod.FileHashes fileHash in download.hashMd5)
                        {
                            if (fileHash.hash == InjectorHash)
                            {
                                AddDetectedMod(mod);
                            }
                        }
                    }
                }
            }*/
        }

        private void AddDetectedMod(Mod mod)
        {
            if (!InstalledMods.Contains(mod))
            {
                InstalledMods.Add(mod);
                if (App.SelectInstalledMods && !DefaultMods.Contains(mod.name))
                {
                    DefaultMods.Add(mod.name);
                }
            }
        }

        private Mod GetModFromHash(string hash)
        {
            /*foreach (Mod mod in AllModsList)
            {
                if (mod.name.ToLowerInvariant() != "bsipa" && mod.status != "declined")
                {
                    foreach (Mod.DownloadLink download in mod.downloads)
                    {
                        foreach (Mod.FileHashes fileHash in download.hashMd5)
                        {
                            if (fileHash.hash == hash)
                                return mod;
                        }
                    }
                }
            }*/

            return null;
        }

        public async Task PopulateModsList()
        {
            try
            {
                //var resp = await HttpClient.GetAsync(Utils.Constants.BeatModsAPIUrl + Utils.Constants.BeatModsModsOptions + "&gameVersion=" + MainWindow.GameVersion);
                try
                {
                    var resp = await HttpClient.GetAsync(Utils.Constants.NASBModInfo);
                    var body = await resp.Content.ReadAsStringAsync();
                    ModsList = JsonSerializer.Deserialize<Mod[]>(body);
                    Array.Reverse(ModsList);

                    // actual pinning system tbd lmao
                    var modsList = new List<Mod>();
                    var skinList = new List<Mod>();
                    var voiceList = new List<Mod>();
                    var otherList = new List<Mod>();
                    var pinnedMod = new List<Mod>();
                    var pinnedVoice = new List<Mod>();

                    string[] pinnedMods = new string[] { "BepInExPack_NASB", "AltSkins", "Voice_Mod", "CustomMusicMod" };
                    string[] pinnedVoicepacks = new string[] { "Complete_Basic_Voice_Pack" };

                    // please fix this later oh my god
                    for (int i = 0; i < ModsList.Length; i++)
                    {
                        var addedYet = false;
                        var mod = ModsList[i];

                        if (mod.categories.Contains("Voicepacks"))
                        {
                            for (int j = 0; j < pinnedVoicepacks.Length; j++)
                            {
                                if (mod.name == pinnedVoicepacks[j] && !addedYet)
                                {
                                    pinnedVoice.Add(mod);
                                    addedYet = true;
                                }
                            }
                            if (!addedYet) voiceList.Add(mod);
                        }
                        else if (mod.categories.Contains("Skins"))
                        {
                            skinList.Add(mod);
                        }
                        else if (mod.categories == null || mod.categories.Length == 0) // used to break smm lol
                        {
                            mod.categories = new string[] { "Other" };
                            otherList.Add(mod);
                        }
                        else
                        {
                            for (int j = 0; j < pinnedMods.Length; j++)
                            {
                                if (mod.name == pinnedMods[j] && !addedYet)
                                {
                                    pinnedMod.Add(mod);
                                    addedYet = true;
                                }
                            }
                            if (!addedYet) modsList.Add(mod);
                        }
                    }
                    pinnedMod.AddRange(modsList);
                    pinnedMod.AddRange(skinList);
                    pinnedMod.AddRange(pinnedVoice);
                    pinnedMod.AddRange(voiceList);
                    pinnedMod.AddRange(otherList);

                    ModsList = pinnedMod.ToArray();
                }
                catch (Exception e)
                {
                    System.Windows.MessageBox.Show($"{FindResource("Mods:LoadFailed")}.\n\n" + e);
                    return;
                }

                foreach (Mod mod in ModsList)
                {
                    //bool preSelected = mod.required;
                    bool preSelected = false;
                    bool required = false;
                    if ((App.SaveModSelection && App.SavedMods.Contains(mod.name)))
                    {
                        preSelected = true;
                        if (!App.SavedMods.Contains(mod.name))
                        {
                            App.SavedMods.Add(mod.name);
                        }
                    }

                    if (DefaultMods.Contains(mod.name))
                    {
                        preSelected = true;
                        required = true;
                    }

                    RegisterDependencies(mod);

                    bool isMod = false;
                    for (int i = 0; i < mod.categories.Length; i++)
                    {
                        if (mod.categories[i] == "Mods") isMod = true;
                    }

                    ModListItem ListItem = new ModListItem()
                    {
                        IsSelected = preSelected,
                        IsEnabled = !required,
                        ModName = mod.name.Replace('_', ' '),
                        ModAuthor = mod.owner,
                        ModVersion = mod.LatestVersion.version_number,
                        ModDescription = mod.LatestVersion.description,
                        ModInfo = mod,
                        ModImage = mod.LatestVersion.icon,
                        Category = isMod ? "Mods" : mod.categories[0] // what the hell
                    };

                    foreach (Promotion promo in Promotions.List)
                    {
                        if (promo.Active && mod.name == promo.ModName)
                        {
                            ListItem.PromotionTexts = new string[promo.Links.Count];
                            ListItem.PromotionLinks = new string[promo.Links.Count];
                            ListItem.PromotionTextAfterLinks = new string[promo.Links.Count];

                            for (int i = 0; i < promo.Links.Count; ++i)
                            {
                                PromotionLink link = promo.Links[i];
                                ListItem.PromotionTexts[i] = link.Text;
                                ListItem.PromotionLinks[i] = link.Link;
                                ListItem.PromotionTextAfterLinks[i] = link.TextAfterLink;
                            }
                        }
                    }

                    foreach (Mod installedMod in InstalledMods)
                    {
                        if (mod.name == installedMod.name)
                        {
                            ListItem.InstalledModInfo = installedMod;
                            ListItem.IsInstalled = true;
                            ListItem.InstalledVersion = installedMod.LatestVersion.version_number;
                            break;
                        }
                    }

                    mod.ListItem = ListItem;

                    ModList.Add(ListItem);
                }

                foreach (Mod mod in ModsList)
                {
                    ResolveDependencies(mod);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

        }

        public async void InstallMods()
        {
            MainWindow.Instance.InstallButton.IsEnabled = false;
            string installDirectory = App.BeatSaberInstallDirectory;

            foreach (Mod mod in ModsList)
            {
                // Ignore mods that are newer than installed version
                if (mod.ListItem.GetVersionComparison > 0) continue;

                // Ignore mods that are on current version if we aren't reinstalling mods
                if (mod.ListItem.GetVersionComparison == 0 && !App.ReinstallInstalledMods) continue;

                if (mod.name.ToLowerInvariant() == "bsipa")
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstallingMod"), mod.name)}...";
                    await Task.Run(async () => await InstallMod(mod, installDirectory));
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstalledMod"), mod.name)}.";
                    if (!File.Exists(Path.Combine(installDirectory, "winhttp.dll")))
                    {
                        await Task.Run(() =>
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = Path.Combine(installDirectory, "IPA.exe"),
                                WorkingDirectory = installDirectory,
                                Arguments = "-n"
                            }).WaitForExit()
                        );
                    }

                    //Options.Instance.YeetBSIPA.IsEnabled = true;
                }
                else if (mod.ListItem.IsSelected)
                {

                    if (mod.categories.Contains("Mods"))
                    {
                        var directoryName = Path.Combine(installDirectory, "BepInEx", "plugins", $"{mod.owner}-{mod.name}");
                        var manifestPath = Path.Combine(directoryName, "manifest.json");
                        if (File.Exists(manifestPath))
                        {
                            dynamic manifest = JsonSerializer.Deserialize<dynamic>(File.ReadAllText(manifestPath));
                            
                            var modVersion = new Version(mod.LatestVersion.version_number);
                            var curVersion = new Version(manifest["version_number"]);

                            if (modVersion <= curVersion && Directory.GetFiles(directoryName).Any(x => x.ToLower().EndsWith(".dll"))) continue;
                        }
                    }


                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstallingMod"), mod.name)}...";
                    await Task.Run(async () => await InstallMod(mod, Path.Combine(installDirectory)));
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstalledMod"), mod.name)}.";
                }
            }

            MainWindow.Instance.MainText = $"{FindResource("Mods:FinishedInstallingMods")}.";
            MainWindow.Instance.InstallButton.IsEnabled = true;
            RefreshModsList();
        }

        string[] BepInExSubDirectories = new string[] { "core", "patchers", "monomod", "plugins", "config", "customsongs" };

        public async Task InstallMod(Mod mod, string directory)
        {
            //int filesCount = 0;
            string downloadLink = null;

            /*foreach (Mod.DownloadLink link in mod.downloads)
            {
                filesCount = link.hashMd5.Length;

                if (link.type == "universal")
                {
                    downloadLink = link.url;
                    break;
                }
                else if (link.type.ToLowerInvariant() == App.BeatSaberInstallType.ToLowerInvariant())
                {
                    downloadLink = link.url;
                    break;
                }
            }*/

            downloadLink = mod.LatestVersion.download_url;

            if (string.IsNullOrEmpty(downloadLink))
            {
                System.Windows.MessageBox.Show(string.Format((string)FindResource("Mods:ModDownloadLinkMissing"), mod.name));
                return;
            }

            while (true)
            {
                List<ZipArchiveEntry> files = new List<ZipArchiveEntry>();

                using (Stream stream = await DownloadMod(downloadLink))
                using (ZipArchive archive = new ZipArchive(stream))
                {
                    foreach (ZipArchiveEntry file in archive.Entries)
                    {
                        /*string fileDirectory = Path.GetDirectoryName(Path.Combine(directory, file.FullName));
                        if (!Directory.Exists(fileDirectory))
                        {
                            Directory.CreateDirectory(fileDirectory);
                        }*/
                        if (!string.IsNullOrEmpty(file.Name))
                        {
                            using (Stream fileStream = file.Open())
                            {
                                /*if (fileHash.hash == Utils.CalculateMD5FromStream(fileStream))
                                {*/
                                files.Add(file);
                                /* break;
                             }*/
                            }
                        }
                        else if (!string.IsNullOrEmpty(file.FullName) && mod.owner != "BepInEx")
                        {
                            var fileDirectory = file.FullName;
                            var fullPathName = Path.GetDirectoryName(file.FullName);

                            foreach (string subdirectory in BepInExSubDirectories)
                            {
                                if (fullPathName.ToLower().StartsWith(subdirectory))
                                {
                                    fileDirectory = Path.Combine("BepInEx", fileDirectory);
                                }
                            }

                            string fileInstallPath = Path.Combine(directory, fileDirectory);

                            if (!Directory.Exists(fileInstallPath)) Directory.CreateDirectory(fileInstallPath);
                            // Directory.CreateDirectory(file.FullName);
                        }
                    }

                    //if (files.Count == filesCount)
                    //{
                    foreach (ZipArchiveEntry file in files)
                    {
                        var defaultPath = Path.Combine("BepInEx", "plugins", $"{mod.owner}-{mod.name}");
                        var defaultVoicepackPath = Path.Combine("BepInEx", "Voicepacks", $"{mod.owner}-{mod.name}");
                        var defaultSkinsPath = Path.Combine("BepInEx", "Skins", $"{mod.owner}-{mod.name}");

                        var fileDirectory = file.FullName;
                        var fullPathName = Path.GetDirectoryName(file.FullName);

                        if (file.Name.ToLower() == "readme.md" || file.Name.ToLower() == "icon.png") continue;
                        if (!mod.categories.Contains("Mods") && file.Name.ToLower() == "manifest.json") continue;

                        // really should make these rules better but I have too much to do
                        // logic (hardcoded lol) for installing bepinex
                        if (fileDirectory.StartsWith(Utils.Constants.BepinExFolderName)) fileDirectory = fileDirectory.Substring(Utils.Constants.BepinExFolderName.Length);

                        // TODO make bepinex the default extraction place for songs and the like
                        // force specific rules into proper bepinex folders
                        string[] BepInExSubDirectories = new string[] { "core", "patchers", "monomod", "plugins", "config", "customsongs" };

                        foreach (string subdirectory in BepInExSubDirectories)
                        {
                            if (fullPathName.ToLower().StartsWith(subdirectory))
                            {
                                fileDirectory = Path.Combine("BepInEx", fileDirectory);
                            }
                        }

                        // logic for installing into plugins by default
                        if (mod.owner != "BepInEx")
                        {
                            if (Path.GetExtension(fileDirectory) == ".voicepack")
                            {
                                if (fullPathName == null || fullPathName == "") fileDirectory = Path.Combine(defaultVoicepackPath, fileDirectory);
                                else if (!fileDirectory.StartsWith("BepInEx")) fileDirectory = Path.Combine(defaultVoicepackPath, fileDirectory);
                            }
                            else if (Path.GetExtension(fileDirectory) == ".nasbskin")
                            {
                                if (fullPathName == null || fullPathName == "") fileDirectory = Path.Combine(defaultSkinsPath, fileDirectory);
                                else if (!fileDirectory.StartsWith("BepInEx")) fileDirectory = Path.Combine(defaultSkinsPath, fileDirectory);
                            }
                            else
                            {
                                if (fullPathName == null || fullPathName == "") fileDirectory = Path.Combine(defaultPath, fileDirectory);
                                else if (!fileDirectory.StartsWith("BepInEx")) fileDirectory = Path.Combine(defaultPath, fileDirectory);
                            }
                        }

                        string fileInstallPath = Path.Combine(directory, fileDirectory);

                        if (Path.GetExtension(fileInstallPath) == ".cfg" && File.Exists(fileInstallPath)) continue;

                        await ExtractFile(file, fileInstallPath, 3.0, mod.name, 10);
                        if (!mod.downloadedFilePaths.Contains(fileInstallPath)) mod.downloadedFilePaths.Add(fileInstallPath);
                    }

                    break;
                    //}
                }
            }

            if (App.CheckInstalledMods)
            {
                try
                {
                    mod.ListItem.IsInstalled = true;
                    mod.ListItem.InstalledVersion = mod.LatestVersion.version_number;
                    mod.ListItem.InstalledModInfo = mod;
                }
                catch
                {
                    // for oneclick
                }
            }
        }

        private async Task ExtractFile(ZipArchiveEntry file, string path, double seconds, string name, int maxTries, int tryNumber = 0)
        {
            if (tryNumber < maxTries)
            {
                try
                {
                    if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));
                    file.ExtractToFile(path, true);
                }
                catch (UnauthorizedAccessException)
                {
                    MainWindow.Instance.MainText = $"Unauthorized Access! Skipping {file.Name}.";
                }
                catch
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:FailedExtract"), name, seconds, tryNumber + 1, maxTries)}";
                    await Task.Delay((int)(seconds * 1000));
                    await ExtractFile(file, path, seconds, name, maxTries, tryNumber + 1);
                }
            }
            else
            {
                System.Windows.MessageBox.Show($"{string.Format((string)FindResource("Mods:FailedExtractMaxReached"), name, maxTries)}.", "Failed to install " + name);
            }
        }

        private async Task<Stream> DownloadMod(string link)
        {
            var resp = await HttpClient.GetAsync(link);
            return await resp.Content.ReadAsStreamAsync();
        }

        private void RegisterDependencies(Mod dependent)
        {
            if (dependent.LatestVersion.dependencies == null || dependent.LatestVersion.dependencies.Length == 0)
                return;

            foreach (Mod mod in ModsList)
            {
                foreach (string dep in dependent.LatestVersion.dependencies)
                {
                    if (mod.MatchesDependencyString(dep))
                    {
                        //dep.Mod = mod;
                        mod.Dependents.Add(dependent);
                    }
                }
            }
        }

        private void ResolveDependencies(Mod dependent)
        {
            if (dependent.ListItem.IsSelected && dependent.LatestVersion.dependencies != null && dependent.LatestVersion.dependencies.Length > 0)
            {
                foreach (string dependency in dependent.LatestVersion.dependencies)
                {
                    foreach (Mod mod in ModsList)
                    {
                        if (mod.MatchesDependencyString(dependency) && mod.ListItem.IsEnabled)
                        {
                            mod.ListItem.PreviousState = mod.ListItem.IsSelected;
                            mod.ListItem.IsSelected = true;
                            mod.ListItem.IsEnabled = false;
                            ResolveDependencies(mod);
                        }
                    }
                }
            }
        }

        private void UnresolveDependencies(Mod dependent)
        {
            if (!dependent.ListItem.IsSelected && dependent.LatestVersion.dependencies != null && dependent.LatestVersion.dependencies.Length > 0)
            {
                foreach (string dependency in dependent.LatestVersion.dependencies)
                {
                    foreach (Mod mod in ModsList)
                    {
                        if (mod.MatchesDependencyString(dependency) && !mod.ListItem.IsEnabled)
                        {
                            /*mod.ListItem.PreviousState = mod.ListItem.IsSelected;
                            mod.ListItem.IsSelected = true;
                            mod.ListItem.IsEnabled = false;
                            ResolveDependencies(mod);*/
                            bool needed = false;
                            foreach (Mod dep in mod.Dependents)
                            {
                                if (dep.ListItem.IsSelected)
                                {
                                    needed = true;
                                    break;
                                }
                            }
                            //if (!needed && !mod.required)
                            if (!needed && !DefaultMods.Contains(mod.name))
                            {
                                mod.ListItem.IsSelected = mod.ListItem.PreviousState;
                                mod.ListItem.IsEnabled = true;
                                UnresolveDependencies(mod);
                            }
                        }
                    }
                }
            }
        }

        private void ModCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Mod mod = (sender as System.Windows.Controls.CheckBox).Tag as Mod;
            mod.ListItem.IsSelected = true;
            ResolveDependencies(mod);
            App.SavedMods.Add(mod.name);
            Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();

            RefreshModsList();
        }

        private void ModCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Mod mod = (sender as System.Windows.Controls.CheckBox).Tag as Mod;
            mod.ListItem.IsSelected = false;
            UnresolveDependencies(mod);
            App.SavedMods.Remove(mod.name);
            Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();

            RefreshModsList();
        }

        public class Category
        {
            public string CategoryName { get; set; }
            public List<ModListItem> Mods = new List<ModListItem>();
        }

        public class ModListItem
        {
            public string ModName { get; set; }
            public string ModImage { get; set; } = "https://i.imgur.com/wkSCAKG.png";
            public string ModAuthor { get; set; }
            public string ModVersion { get; set; }
            public string ModDescription { get; set; }
            public bool PreviousState { get; set; }

            public bool IsEnabled { get; set; }
            public bool IsSelected { get; set; }
            public Mod ModInfo { get; set; }
            public string Category { get; set; }

            public Mod InstalledModInfo { get; set; }
            public bool IsInstalled { get; set; }
            private SemVersion _installedVersion { get; set; }
            public string InstalledVersion
            {
                get
                {
                    if (!IsInstalled || _installedVersion == null) return "-";
                    return _installedVersion.ToString();
                }
                set
                {
                    if (SemVersion.TryParse(value, out SemVersion tempInstalledVersion))
                    {
                        _installedVersion = tempInstalledVersion;
                    }
                    else
                    {
                        _installedVersion = null;
                    }
                }
            }

            public string GetVersionColor
            {
                get
                {
                    if (!IsInstalled) return "Black";
                    return _installedVersion >= ModVersion ? "Green" : "Red";
                }
            }

            public string GetVersionDecoration
            {
                get
                {
                    if (!IsInstalled) return "None";
                    return _installedVersion >= ModVersion ? "None" : "Strikethrough";
                }
            }

            public int GetVersionComparison
            {
                get
                {
                    if (!IsInstalled || _installedVersion < ModVersion) return -1;
                    if (_installedVersion > ModVersion) return 1;
                    return 0;
                }
            }

            public bool CanDelete
            {
                get
                {
                    //return (!ModInfo.required && IsInstalled);
                    return (IsInstalled);
                }
            }

            public string CanSeeDelete
            {
                get
                {
                    //if (!ModInfo.required && IsInstalled)
                    if (IsInstalled)
                        return "Visible";
                    else
                        return "Hidden";
                }
            }

            public string[] PromotionTexts { get; set; }
            public string[] PromotionLinks { get; set; }
            public string[] PromotionTextAfterLinks { get; set; }
            public string PromotionMargin
            {
                get
                {
                    if (PromotionTexts == null || string.IsNullOrEmpty(PromotionTexts[0])) return "-15,0,0,0";
                    return "0,0,5,0";
                }
            }
        }

        private void ModsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((Mods.ModListItem)Instance.ModsListView.SelectedItem == null)
            {
                MainWindow.Instance.InfoButton.IsEnabled = false;
            }
            else
            {
                MainWindow.Instance.InfoButton.IsEnabled = true;
            }
        }

        /*public void UninstallBSIPA(Mod.DownloadLink links)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(App.BeatSaberInstallDirectory, "IPA.exe"),
                WorkingDirectory = App.BeatSaberInstallDirectory,
                Arguments = "--revert -n"
            }).WaitForExit();

            foreach (Mod.FileHashes files in links.hashMd5)
            {
                string file = files.file.Replace("IPA/", "").Replace("Data", "Beat Saber_Data");
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, file));
            }
            Options.Instance.YeetBSIPA.IsEnabled = false;
        }*/

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.Button).Tag as Mod);

            string title = string.Format((string)FindResource("Mods:UninstallBox:Title"), mod.name);
            string body1 = string.Format((string)FindResource("Mods:UninstallBox:Body1"), mod.name);
            string body2 = string.Format((string)FindResource("Mods:UninstallBox:Body2"), mod.name);
            var result = System.Windows.Forms.MessageBox.Show($"{body1}\n{body2}", title, MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {
                UninstallModFromList(mod);
            }
        }

        private void UninstallModFromList(Mod mod)
        {
            UninstallMod(mod.ListItem.InstalledModInfo);
            mod.ListItem.IsInstalled = false;
            mod.ListItem.InstalledVersion = null;
            if (App.SelectInstalledMods)
            {
                mod.ListItem.IsSelected = false;
                UnresolveDependencies(mod);
                App.SavedMods.Remove(mod.name);
                Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
                Properties.Settings.Default.Save();
                RefreshModsList();
            }
            view.Refresh();
        }

        public void UninstallMod(Mod mod)
        {
            if (mod.downloadedFilePaths != null && mod.downloadedFilePaths.Count > 0)
            {
                foreach (string filepath in mod.downloadedFilePaths)
                {
                    if (File.Exists(filepath)) File.Delete(filepath);
                }
            }
            /*Mod.DownloadLink links = null;
            foreach (Mod.DownloadLink link in mod.downloads)
            {
                if (link.type.ToLowerInvariant() == "universal" || link.type.ToLowerInvariant() == App.BeatSaberInstallType.ToLowerInvariant())
                {
                    links = link;
                    break;
                }
            }
            if (mod.name.ToLowerInvariant() == "bsipa")
            {
                var hasIPAExe = File.Exists(Path.Combine(App.BeatSaberInstallDirectory, "IPA.exe"));
                var hasIPADir = Directory.Exists(Path.Combine(App.BeatSaberInstallDirectory, "IPA"));

                if (hasIPADir && hasIPAExe)
                {
                    UninstallBSIPA(links);
                }
                else
                {
                    var title = (string)FindResource("Mods:UninstallBSIPANotFound:Title");
                    var body = (string)FindResource("Mods:UninstallBSIPANotFound:Body");

                    System.Windows.Forms.MessageBox.Show(body, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            foreach (Mod.FileHashes files in links.hashMd5)
            {
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, files.file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, files.file));
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, "IPA", "Pending", files.file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, "IPA", "Pending", files.file));
            }*/
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshColumns();
        }

        private void CopyText(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!(sender is TextBlock textBlock)) return;
            var text = textBlock.Text;

            // Ensure there's text to be copied
            if (string.IsNullOrWhiteSpace(text)) return;

            Utils.SetClipboard(text);
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBar.Height == 0)
            {
                SearchBar.Focus();
                Animate(SearchBar, 0, 16, new TimeSpan(0, 0, 0, 0, 300));
                Animate(SearchText, 0, 16, new TimeSpan(0, 0, 0, 0, 300));
                ModsListView.Items.Filter = new Predicate<object>(SearchFilter);
            }
            else
            {
                Animate(SearchBar, 16, 0, new TimeSpan(0, 0, 0, 0, 300));
                Animate(SearchText, 16, 0, new TimeSpan(0, 0, 0, 0, 300));
                ModsListView.Items.Filter = null;
            }
        }

        private void SearchBar_TextChanged(object sender, TextChangedEventArgs e)
        {
            ModsListView.Items.Filter = new Predicate<object>(SearchFilter);
            if (SearchBar.Text.Length > 0)
            {
                SearchText.Text = null;
            }
            else
            {
                SearchText.Text = (string)FindResource("Mods:SearchLabel");
            }
        }

        private bool SearchFilter(object mod)
        {
            ModListItem item = mod as ModListItem;
            if (item.ModName.ToLowerInvariant().Contains(SearchBar.Text.ToLowerInvariant())) return true;
            if (item.ModDescription.ToLowerInvariant().Contains(SearchBar.Text.ToLowerInvariant())) return true;
            if (item.ModName.ToLowerInvariant().Replace(" ", string.Empty).Contains(SearchBar.Text.ToLowerInvariant().Replace(" ", string.Empty))) return true;
            if (item.ModDescription.ToLowerInvariant().Replace(" ", string.Empty).Contains(SearchBar.Text.ToLowerInvariant().Replace(" ", string.Empty))) return true;
            return false;
        }

        private void Animate(TextBlock target, double oldHeight, double newHeight, TimeSpan duration)
        {
            target.Height = oldHeight;
            DoubleAnimation animation = new DoubleAnimation(newHeight, duration);
            target.BeginAnimation(HeightProperty, animation);
        }

        private void Animate(TextBox target, double oldHeight, double newHeight, TimeSpan duration)
        {
            target.Height = oldHeight;
            DoubleAnimation animation = new DoubleAnimation(newHeight, duration);
            target.BeginAnimation(HeightProperty, animation);
        }
    }
}
